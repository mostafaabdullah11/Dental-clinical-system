#!/usr/bin/env bash
# Logical restore drill (DEPLOYMENT_ARCHITECTURE.md §3.4; runbooks R1 / R3). Restores an encrypted nightly dump into a
# THROWAWAY database and verifies it. Never targets a live database: the target name must start with
# `clinic_restore_drill_` and must not exist yet. Run on the operator's encrypted workstation or a short-lived drill VM —
# the recovery private key is never placed on the production VM.
#   PGHOST=… PGPORT=… PGUSER=<admin> bash deploy/backup/restore-drill.sh \
#     --dump clinic-….dump.age --manifest clinic-….manifest.tsv.age --identity /path/to/recovery.agekey \
#     --target-db clinic_restore_drill_YYYYMMDD [--keep]
# Verifies: archive restores as clinic_migrator in one transaction; key table recreated empty; manifest (row counts, latest
# audit id, ledger totals, latest migration) identical; all integrity checks 0; triggers and role grants present.
set -Eeuo pipefail
umask 077
cd "$(dirname "$0")/../.."
SQL="src/Clinic.Persistence/Operations"
KEEP=0; DUMP=""; MANIFEST=""; IDENTITY=""; TARGET=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dump) DUMP="$2"; shift 2 ;;
    --manifest) MANIFEST="$2"; shift 2 ;;
    --identity) IDENTITY="$2"; shift 2 ;;
    --target-db) TARGET="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    *) echo "unknown argument $1"; exit 2 ;;
  esac
done
[[ -r "$DUMP" && -r "$MANIFEST" && -r "$IDENTITY" ]] || { echo "dump, manifest and identity files are required"; exit 2; }
[[ "$TARGET" =~ ^clinic_restore_drill_[a-z0-9_]+$ ]] || { echo "target must be a throwaway database named clinic_restore_drill_*"; exit 2; }
admin() { psql -X -q -v ON_ERROR_STOP=1 "$@"; }
as_role() { psql -X -q -A -t -F $'\t' -v ON_ERROR_STOP=1 -d "$TARGET" -c "SET ROLE $1;" "${@:2}"; }
EXISTS="$(admin -d postgres -Atc "SELECT count(*) FROM pg_database WHERE datname = '${TARGET}'")"
[[ "$EXISTS" == 0 ]] || { echo "refusing: ${TARGET} already exists"; exit 2; }

W="$(mktemp -d)"
cleanup() {
  rm -rf "$W"                                       # the decrypted dump never outlives the drill
  if [[ $KEEP -eq 0 ]]; then admin -d postgres -c "DROP DATABASE IF EXISTS \"${TARGET}\" WITH (FORCE)" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

echo "== roles (idempotent bootstrap; existing roles untouched) and throwaway database"
sed -e 's/{{CLINIC_[A-Z]*_PASSWORD}}/'"$(openssl rand -hex 16 | tr -cd '0-9a-f')"'/g' deploy/db/bootstrap-roles.sql | admin -d postgres >/dev/null
sed -e "s/{{DATABASE_NAME}}/${TARGET}/g" deploy/db/create-database.sql | grep -v '^--' | grep -v '^$' \
  | while IFS= read -r stmt; do admin -d postgres -c "$stmt" >/dev/null; done

echo "== decrypt and restore as clinic_migrator (single transaction)"
age -d -i "$IDENTITY" -o "$W/restore.dump" "$DUMP"
age -d -i "$IDENTITY" -o "$W/expected.tsv" "$MANIFEST"
pg_restore -d "$TARGET" --role=clinic_migrator --exit-on-error --single-transaction --no-password "$W/restore.dump"
as_role clinic_migrator -f "$SQL/restore-data-protection-table.sql" >/dev/null

echo "== verify"
FAIL=0
as_role clinic_backup -f "$SQL/backup-manifest.sql" | tr -d '\r' | grep -v '^SET$' | grep -v '^$' > "$W/actual.tsv"
tr -d '\r' < "$W/expected.tsv" | grep -v '^$' > "$W/expected.clean.tsv"
if diff -u "$W/expected.clean.tsv" "$W/actual.tsv" > "$W/manifest.diff"; then
  echo "  ok    manifest identical ($(wc -l < "$W/actual.tsv" | tr -d ' ') items)"
else
  echo "  FAIL  manifest differs:"; head -20 "$W/manifest.diff"; FAIL=1
fi
as_role clinic_backup -f "$SQL/integrity-checks.sql" | tr -d '\r' | grep -v '^SET$' | grep -v '^$' > "$W/integrity.tsv"
if awk -F'\t' '$2 != 0 { bad=1 } END { exit bad }' "$W/integrity.tsv"; then
  echo "  ok    integrity checks all 0 ($(wc -l < "$W/integrity.tsv" | tr -d ' ') checks)"
else
  echo "  FAIL  integrity: $(awk -F'\t' '$2 != 0' "$W/integrity.tsv" | tr '\n' ' ')"; FAIL=1
fi
TRIGGERS="$(admin -d "$TARGET" -Atc "SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal")"
GRANTS="$(admin -d "$TARGET" -Atc "SELECT count(*) FROM information_schema.role_table_grants WHERE grantee IN ('clinic_app','clinic_worker','clinic_backup')")"
KEYS="$(admin -d "$TARGET" -Atc "SELECT count(*) FROM infra.data_protection_keys")"
[[ "$TRIGGERS" -gt 20 ]] && echo "  ok    ${TRIGGERS} guard triggers present" || { echo "  FAIL  triggers missing"; FAIL=1; }
[[ "$GRANTS" -gt 100 ]] && echo "  ok    ${GRANTS} role grants present" || { echo "  FAIL  grants missing"; FAIL=1; }
[[ "$KEYS" == 0 ]] && echo "  ok    Data Protection key table recreated empty" || { echo "  FAIL  key rows restored"; FAIL=1; }
if as_role clinic_app -c "DELETE FROM finance.payments" >/dev/null 2>&1; then echo "  FAIL  clinic_app could delete"; FAIL=1; else echo "  ok    clinic_app still cannot delete ledger rows"; fi

[[ $FAIL -eq 0 ]] && echo "RESTORE DRILL: PASS" || echo "RESTORE DRILL: FAIL"
echo "Next (real recovery only): run the migrator (expected no-op) and complete the post-restore security steps of the operator guide before opening to users."
exit $FAIL
