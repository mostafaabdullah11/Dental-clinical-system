#!/usr/bin/env bash
# Generates THROWAWAY secrets for the local production-like stack only (never for staging / production; never committed:
# deploy/local/secrets and deploy/local/.env are gitignored). Requires openssl and age-keygen.
set -Eeuo pipefail
umask 077
HERE="$(cd "$(dirname "$0")" && pwd)"
S="${HERE}/secrets"
mkdir -p "$S"
rand() { openssl rand -hex 24 | tr -cd '0-9a-f'; }

ADMIN="$(rand)"; MIG="$(rand)"; APP="$(rand)"; WRK="$(rand)"; BKP="$(rand)"
printf '%s' "$ADMIN" > "$S/postgres_admin_password"
conn() { printf 'Host=postgres;Port=5432;Database=clinic;Username=%s;Password=%s;SSL Mode=Disable' "$1" "$2"; }
conn clinic_app "$APP"       > "$S/app_db_conn"
conn clinic_worker "$WRK"    > "$S/worker_db_conn"
conn clinic_migrator "$MIG"  > "$S/migrator_db_conn"
printf 'host=postgres port=5432 dbname=clinic user=clinic_backup password=%s sslmode=disable' "$BKP" > "$S/backup_db_conn"

# Data Protection certificate (api only) — self-signed, local only.
DPPW="$(rand)"; printf '%s' "$DPPW" > "$S/dp_cert_password"
MSYS2_ARG_CONV_EXCL="/CN" openssl req -x509 -newkey rsa:3072 -nodes -days 30 -subj "/CN=clinic-local-dataprotection" \
  -keyout "$S/dp.key" -out "$S/dp.crt" >/dev/null 2>&1
CLINIC_DP_PFX_PASSWORD="$(cat "$S/dp_cert_password")" openssl pkcs12 -export -inkey "$S/dp.key" -in "$S/dp.crt" -out "$S/dp_cert.pfx" -passout env:CLINIC_DP_PFX_PASSWORD
rm -f "$S/dp.key" "$S/dp.crt"

# Two TEST age recipients (their private halves stay here for the local restore drill; production never has them on a VM).
age-keygen -o "$S/test-recovery-1.agekey" 2>/dev/null; age-keygen -o "$S/test-recovery-2.agekey" 2>/dev/null
{ age-keygen -y "$S/test-recovery-1.agekey"; age-keygen -y "$S/test-recovery-2.agekey"; } > "$S/backup_public_keys"
: > "$S/backup_upload_credentials"; : > "$S/backup_heartbeat_url"

cat > "${HERE}/.env" <<EOF
CLINIC_ENVIRONMENT=Production
CLINIC_DOMAIN=localhost
CLINIC_ACME_EMAIL=admin@localhost
CLINIC_INTERNAL_SUBNET=172.30.0.0/24
CLINIC_SECRETS_DIR=../local/secrets
CLINIC_WEB_IMAGE=clinic-web:local
CLINIC_BACKEND_IMAGE=clinic-backend:local
CLINIC_MIGRATOR_IMAGE=clinic-migrator:local
CLINIC_BACKUP_IMAGE=clinic-backup:local
CLINIC_MIGRATOR_PASSWORD=${MIG}
CLINIC_APP_PASSWORD=${APP}
CLINIC_WORKER_PASSWORD=${WRK}
CLINIC_BACKUP_PASSWORD=${BKP}
EOF
echo "Local throwaway secrets written to ${S} (gitignored)."
