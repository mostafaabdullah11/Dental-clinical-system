#!/usr/bin/env bash
# Nightly independent logical backup (DEPLOYMENT_ARCHITECTURE.md §3.3 layer 2). Runs in the one-shot `backup` container
# (`docker compose --profile backup run --rm backup`) as the restricted clinic_backup role. Never holds a private key.
#
# Inputs (files, *_FILE convention; nothing secret on the command line or in logs):
#   BACKUP_DB_CONN_FILE            libpq connection string for clinic_backup (sslmode=verify-full in production)
#   BACKUP_RECIPIENTS_FILE         age PUBLIC recipients, one per line; at least TWO distinct (either private key decrypts)
#   BACKUP_UPLOAD_CREDENTIALS_FILE optional: AWS_ACCESS_KEY_ID=... / AWS_SECRET_ACCESS_KEY=... (write-only, put-object)
#   BACKUP_HEARTBEAT_URL_FILE      optional: dead-man's-switch URL (success ping; "<url>/fail" on failure)
# Settings (environment): BACKUP_S3_ENDPOINT, BACKUP_S3_BUCKET, BACKUP_S3_REGION (upload when all set),
#   BACKUP_OUTPUT_DIR (where encrypted artefacts are kept locally; default /tmp/clinic-backup-out),
#   BACKUP_STATE_DIR (last dump size for the 80% check; default /var/lib/clinic-backup), BACKUP_SQL_DIR.
# Exit codes: 0 ok; 1 failure; 3 FINANCIAL INTEGRITY FAILURE (critical alert).
set -Eeuo pipefail
umask 077

SQL_DIR="${BACKUP_SQL_DIR:-/opt/clinic-backup/sql}"
OUT_DIR="${BACKUP_OUTPUT_DIR:-/tmp/clinic-backup-out}"
STATE_DIR="${BACKUP_STATE_DIR:-/var/lib/clinic-backup}"
WORK="$(mktemp -d)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
NAME="clinic-${STAMP}"
HEARTBEAT=""
EXIT_CODE=1

log() { printf '%s backup: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
need() { [[ -n "${!1:-}" && -r "${!1}" ]] || { log "missing or unreadable ${1}"; exit 1; }; }

cleanup() {
  local status=$?
  if [[ -n "${SNAP_PID:-}" ]]; then
    # EOF first (psql ends its read-only transaction and exits), then make sure the coprocess is gone.
    [[ -n "${SNAP_IN:-}" ]] && eval "exec ${SNAP_IN}>&-" 2>/dev/null
    kill "${SNAP_PID}" 2>/dev/null || true
  fi
  rm -rf "${WORK}"                                   # the plaintext dump never outlives the run
  if [[ ${status} -ne 0 && -n "${HEARTBEAT}" ]]; then curl -fsS -m 10 -o /dev/null "${HEARTBEAT}/fail" || true; fi
  exit "${status}"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

need BACKUP_DB_CONN_FILE
need BACKUP_RECIPIENTS_FILE
DB="$(tr -d '\r\n' < "${BACKUP_DB_CONN_FILE}")"
if [[ -n "${BACKUP_HEARTBEAT_URL_FILE:-}" && -r "${BACKUP_HEARTBEAT_URL_FILE}" ]]; then HEARTBEAT="$(tr -d '\r\n' < "${BACKUP_HEARTBEAT_URL_FILE}")"; fi

mapfile -t RECIPIENTS < <(grep -E '^age1[0-9a-z]+$' "${BACKUP_RECIPIENTS_FILE}" | sort -u)
if [[ ${#RECIPIENTS[@]} -lt 2 ]]; then log "at least two distinct age recipients are required (found ${#RECIPIENTS[@]})"; exit 1; fi
RECIPIENT_ARGS=(); for r in "${RECIPIENTS[@]}"; do RECIPIENT_ARGS+=(-r "$r"); done

mkdir -p "${OUT_DIR}" "${STATE_DIR}" 2>/dev/null || true

# 1. One exported snapshot for dump, manifest and integrity checks (the session stays open until all three are done).
# A coprocess talks over anonymous pipes (portable to every bash, including native psql on Windows; no FIFOs).
coproc SNAPSESSION { psql "${DB}" -X -q -A -t -v ON_ERROR_STOP=1 2>"${WORK}/snap.err"; }
SNAP_PID="${SNAPSESSION_PID}"
SNAP_IN="${SNAPSESSION[1]}"; SNAP_OUT="${SNAPSESSION[0]}"
echo "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;" >&"${SNAP_IN}"
echo "SELECT pg_export_snapshot();" >&"${SNAP_IN}"
read -r -t 30 SNAPSHOT <&"${SNAP_OUT}" || { log "could not export a snapshot"; exit 1; }
SNAPSHOT="${SNAPSHOT%$'\r'}"
[[ "${SNAPSHOT}" =~ ^[0-9A-F-]+$ ]] || { log "unexpected snapshot id"; exit 1; }

in_snapshot() { # $1 = SQL file; output rows to stdout
  { echo "BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;"; echo "SET TRANSACTION SNAPSHOT '${SNAPSHOT}';"; cat "$1"; echo "COMMIT;"; } \
    | psql "${DB}" -X -q -A -t -F $'\t' -v ON_ERROR_STOP=1
}

# 2. Dump. The key table is excluded entirely: clinic_backup can neither lock nor read it (restore recreates it empty).
log "dumping (snapshot exported)"
pg_dump --dbname="${DB}" --snapshot="${SNAPSHOT}" --format=custom --no-password \
  --exclude-table=infra.data_protection_keys --file="${WORK}/${NAME}.dump"

# 3. Financial integrity in the same snapshot: any violation fails loudly (exit 3 = critical alert).
in_snapshot "${SQL_DIR}/integrity-checks.sql" > "${WORK}/integrity.tsv"
if awk -F'\t' '$2 != 0 { bad=1 } END { exit bad ? 0 : 1 }' "${WORK}/integrity.tsv"; then
  log "FINANCIAL INTEGRITY FAILURE: $(awk -F'\t' '$2 != 0 { printf "%s=%s ", $1, $2 }' "${WORK}/integrity.tsv")"
  EXIT_CODE=3; exit 3
fi
CHECKS="$(wc -l < "${WORK}/integrity.tsv" | tr -d ' ')"

# 4. Manifest in the same snapshot (table counts, latest audit id, ledger totals, latest migration).
in_snapshot "${SQL_DIR}/backup-manifest.sql" > "${WORK}/${NAME}.manifest.tsv"

# End the snapshot session: COMMIT, then close its stdin (EOF) so psql exits.
echo "COMMIT;" >&"${SNAP_IN}"; eval "exec ${SNAP_IN}>&-"; wait "${SNAP_PID}" 2>/dev/null || true; SNAP_PID=""

# 5. Verification: non-empty, not shrunk below 80% of the previous dump, readable archive, checksum.
SIZE="$(stat -c %s "${WORK}/${NAME}.dump")"
[[ "${SIZE}" -gt 0 ]] || { log "empty dump"; exit 1; }
if [[ -r "${STATE_DIR}/last-size" ]]; then
  PREVIOUS="$(cat "${STATE_DIR}/last-size")"
  if [[ "${PREVIOUS}" =~ ^[0-9]+$ && $(( SIZE * 100 )) -lt $(( PREVIOUS * 80 )) ]]; then
    log "size anomaly: ${SIZE} bytes < 80% of previous ${PREVIOUS}"; exit 1
  fi
fi
pg_restore --list "${WORK}/${NAME}.dump" > "${WORK}/listing.txt"
grep -q "TABLE DATA finance payments" "${WORK}/listing.txt" || { log "archive listing incomplete"; exit 1; }
if grep -q "data_protection_keys" "${WORK}/listing.txt"; then log "key table found in the dump"; exit 1; fi
DUMP_SHA="$(sha256sum "${WORK}/${NAME}.dump" | cut -d' ' -f1)"

# 6. Client-side encryption to every recipient (either private key alone decrypts). Manifest encrypted too.
age "${RECIPIENT_ARGS[@]}" -o "${OUT_DIR}/${NAME}.dump.age" "${WORK}/${NAME}.dump"
age "${RECIPIENT_ARGS[@]}" -o "${OUT_DIR}/${NAME}.manifest.tsv.age" "${WORK}/${NAME}.manifest.tsv"
ENC_SHA="$(sha256sum "${OUT_DIR}/${NAME}.dump.age" | cut -d' ' -f1)"
{
  echo "name=${NAME}"
  echo "created_utc=${STAMP}"
  echo "dump_bytes=${SIZE}"
  echo "dump_sha256=${DUMP_SHA}"
  echo "encrypted_sha256=${ENC_SHA}"
  echo "recipients=${#RECIPIENTS[@]}"
  echo "integrity_checks=${CHECKS}"
} > "${OUT_DIR}/${NAME}.integrity.txt"

# 7. Upload with write-only credentials (put object only). The returned ETag (MD5 of a single PUT) is compared locally.
upload() { # $1 = file
  local file="$1" key md5 etag
  key="$(basename "$file")"
  md5="$(md5sum "$file" | cut -d' ' -f1)"
  etag="$(curl -fsS -m 600 --aws-sigv4 "aws:amz:${BACKUP_S3_REGION}:s3" --user "${AWS_ACCESS_KEY_ID}:${AWS_SECRET_ACCESS_KEY}" \
    -H "x-amz-content-sha256: UNSIGNED-PAYLOAD" -T "$file" -D - -o /dev/null \
    "${BACKUP_S3_ENDPOINT%/}/${BACKUP_S3_BUCKET}/${key}" | tr -d '\r' | awk -F'"' 'tolower($0) ~ /^etag:/ { print $2 }')"
  [[ "${etag}" == "${md5}" ]] || { log "upload checksum mismatch for ${key}"; return 1; }
}
if [[ -n "${BACKUP_S3_ENDPOINT:-}" && -n "${BACKUP_S3_BUCKET:-}" && -n "${BACKUP_S3_REGION:-}" ]]; then
  need BACKUP_UPLOAD_CREDENTIALS_FILE
  # shellcheck disable=SC1090
  set -a; . "${BACKUP_UPLOAD_CREDENTIALS_FILE}"; set +a
  for f in "${OUT_DIR}/${NAME}.dump.age" "${OUT_DIR}/${NAME}.manifest.tsv.age" "${OUT_DIR}/${NAME}.integrity.txt"; do upload "$f"; done
  log "uploaded 3 objects"
else
  log "upload not configured: encrypted artefacts kept in ${OUT_DIR}"
fi

# 8. Record and report success (no personal data, no secrets).
echo "${SIZE}" > "${STATE_DIR}/last-size" 2>/dev/null || true
if [[ -n "${HEARTBEAT}" ]]; then curl -fsS -m 10 -o /dev/null "${HEARTBEAT}" || log "heartbeat ping failed"; fi
log "OK ${NAME} dump_bytes=${SIZE} integrity_checks=${CHECKS} recipients=${#RECIPIENTS[@]}"
EXIT_CODE=0
