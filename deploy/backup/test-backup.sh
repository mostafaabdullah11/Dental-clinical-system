#!/usr/bin/env bash
# F5.22 / F5.23 automated backup test (developer machine and the CI "backup" job). Uses TEST age keys generated here and
# deleted afterwards — never production keys. Requires psql/pg_dump/pg_restore (18), age, age-keygen, dotnet.
#   PGHOST=localhost PGPORT=5432 PGUSER=postgres bash deploy/backup/test-backup.sh
# Proves: the real backup.sh dumps as clinic_backup in one snapshot, writes manifest + integrity, verifies the archive,
# encrypts to two recipients; EACH private key alone decrypts and the archive lists; an injected ledger violation exits 3;
# a dump below 80% of the previous size fails.
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
PGHOST="${PGHOST:-localhost}"; PGPORT="${PGPORT:-5432}"; PGUSER="${PGUSER:-postgres}"
DB="clinic_backuptest_$(date +%s)"
W="$(mktemp -d)"
trap 'psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -qAtc "DROP DATABASE IF EXISTS \"${DB}\" WITH (FORCE)" >/dev/null 2>&1 || true; rm -rf "$W"' EXIT
admin() { psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -q -v ON_ERROR_STOP=1 "$@"; }

echo "== throwaway database and migrations"
sed -e 's/{{CLINIC_[A-Z]*_PASSWORD}}/'"$(openssl rand -hex 16 | tr -cd '0-9a-f')"'/g' deploy/db/bootstrap-roles.sql | admin -d postgres >/dev/null
sed -e "s/{{DATABASE_NAME}}/${DB}/g" deploy/db/create-database.sql | grep -v '^--' | grep -v '^$' \
  | while IFS= read -r stmt; do admin -d postgres -c "$stmt" >/dev/null; done
printf 'Host=%s;Port=%s;Username=%s;Database=%s;Options=-c role=clinic_migrator' "$PGHOST" "$PGPORT" "$PGUSER" "$DB" > "$W/migrator_conn"
CLINIC_MIGRATOR_CONNECTION_FILE="$W/migrator_conn" dotnet run --project src/Clinic.Migrator -c Release >/dev/null

echo "== test recovery keys (two independent recipients)"
age-keygen -o "$W/k1.agekey" 2>/dev/null; age-keygen -o "$W/k2.agekey" 2>/dev/null
{ age-keygen -y "$W/k1.agekey"; age-keygen -y "$W/k2.agekey"; } | tr -d '\r' > "$W/recipients"
printf "host=%s port=%s dbname=%s user=%s options='-c role=clinic_backup'" "$PGHOST" "$PGPORT" "$DB" "$PGUSER" > "$W/backup_conn"

run_backup() {
  BACKUP_DB_CONN_FILE="$W/backup_conn" BACKUP_RECIPIENTS_FILE="$W/recipients" BACKUP_SQL_DIR="src/Clinic.Persistence/Operations" \
  BACKUP_OUTPUT_DIR="$W/out" BACKUP_STATE_DIR="$W/state" bash deploy/backup/backup.sh
}

echo "== backup run"
run_backup
DUMP="$(ls -t "$W"/out/*.dump.age | head -1)"
grep -q '^recipients=2$' "${DUMP%.dump.age}.integrity.txt"
for k in 1 2; do
  age -d -i "$W/k${k}.agekey" -o "$W/plain${k}.dump" "$DUMP"
  pg_restore --list "$W/plain${k}.dump" > "$W/list${k}.txt"
  grep -q "TABLE DATA finance payments" "$W/list${k}.txt"
  echo "   key ${k} alone decrypts; archive lists"
done
age -d -i "$W/k1.agekey" "${DUMP%.dump.age}.manifest.tsv.age" | grep -q $'^latest_migration\t.*Infra_BackupGrants'
if age -d -i "$W/k1.agekey" "${DUMP%.dump.age}.manifest.tsv.age" | grep -q data_protection_keys; then echo "key table in manifest"; exit 1; fi

echo "== restore drill of that backup into a throwaway database (key 2)"
bash deploy/backup/restore-drill.sh --dump "$DUMP" --manifest "${DUMP%.dump.age}.manifest.tsv.age" --identity "$W/k2.agekey" \
  --target-db "clinic_restore_drill_test_$(date +%s)" > "$W/drill.log" 2>&1 || { cat "$W/drill.log"; exit 1; }
grep -E "  (ok|FAIL)|RESTORE DRILL" "$W/drill.log"
if bash deploy/backup/restore-drill.sh --dump "$DUMP" --manifest "${DUMP%.dump.age}.manifest.tsv.age" --identity "$W/k2.agekey" \
     --target-db "$DB" >/dev/null 2>&1; then echo "drill accepted a non-throwaway target"; exit 1; fi
echo "   drill refuses a non-throwaway target"

echo "== size anomaly (< 80% of the previous dump) fails"
echo 999999999 > "$W/state/last-size"
if run_backup >/dev/null 2>&1; then echo "size anomaly not detected"; exit 1; fi
rm -f "$W/state/last-size"

echo "== injected ledger violation fails with exit 3"
admin -d "$DB" -f deploy/backup/test-inject-violation.sql >/dev/null
set +e
run_backup > "$W/violation.log" 2>&1
CODE=$?
set -e
[[ $CODE -eq 3 ]] || { echo "expected exit 3, got ${CODE}"; cat "$W/violation.log"; exit 1; }
grep -q "FINANCIAL INTEGRITY FAILURE: g8_payment_conservation=1" "$W/violation.log"
echo "   integrity failure detected (exit 3)"

echo "BACKUP TEST: PASS"
