#!/usr/bin/env bash
# F5.21 production-like validation WITHOUT Docker (for machines where Docker is unavailable). Runs the real artefacts as
# processes: the Caddy binary with deploy/caddy/Caddyfile, the published Release builds of the API (Production environment,
# Data Protection certificate from a secret file, *_FILE connection secrets) and the Worker, the migrator, and the built SPA,
# against a throwaway database on a local PostgreSQL server. Synthetic data only; everything is removed afterwards.
#   CADDY_BIN=/path/to/caddy PGHOST=localhost PGPORT=5432 PGUSER=postgres bash deploy/local/run-process-smoke.sh
# The admin login comes from the developer's pgpass; role privileges are exercised with `-c role=…` (clinic_app,
# clinic_worker, clinic_migrator), exactly as the integration tests do. No password is written to disk or printed.
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
ROOT="$PWD"
: "${CADDY_BIN:?set CADDY_BIN to a Caddy v2 binary}"
PGHOST="${PGHOST:-localhost}"; PGPORT="${PGPORT:-5432}"; PGUSER="${PGUSER:-postgres}"
DB="clinic_smoke_$(date +%s)"
WORK="$(mktemp -d)"; S="${WORK}/secrets"; mkdir -p "$S"
PIDS=()
step() { printf '\n== %s\n' "$*"; }
cleanup() {
  for p in "${PIDS[@]:-}"; do
    [[ -n "$p" ]] || continue
    # Git Bash on Windows: stop the native process behind the MSYS pid as well.
    if [[ -r "/proc/$p/winpid" ]]; then taskkill //F //T //PID "$(cat "/proc/$p/winpid")" >/dev/null 2>&1 || true; fi
    kill "$p" 2>/dev/null || true
  done
  sleep 1
  psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -qAtc "DROP DATABASE IF EXISTS \"${DB}\" WITH (FORCE)" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

conn() { printf 'Host=%s;Port=%s;Username=%s;Database=%s;Options=-c role=%s' "$PGHOST" "$PGPORT" "$PGUSER" "$DB" "$1"; }

step "throwaway database ${DB} with the real role bootstrap and database script"
sed -e 's/{{CLINIC_[A-Z]*_PASSWORD}}/'"$(openssl rand -hex 16)"'/g' deploy/db/bootstrap-roles.sql \
  | psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -q -v ON_ERROR_STOP=1 >/dev/null
sed -e "s/{{DATABASE_NAME}}/${DB}/g" deploy/db/create-database.sql | grep -v '^--' | grep -v '^$' \
  | while IFS= read -r stmt; do psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d postgres -q -v ON_ERROR_STOP=1 -c "$stmt" >/dev/null; done
conn clinic_app > "$S/app_db_conn"; conn clinic_worker > "$S/worker_db_conn"; conn clinic_migrator > "$S/migrator_db_conn"

step "Data Protection certificate (api only, throwaway)"
openssl rand -hex 24 | tr -cd '0-9a-f' > "$S/dp_cert_password"
MSYS2_ARG_CONV_EXCL="/CN" openssl req -x509 -newkey rsa:3072 -nodes -days 1 -subj "/CN=clinic-smoke-dp" -keyout "$WORK/dp.key" -out "$WORK/dp.crt" >/dev/null 2>&1
CLINIC_DP_PFX_PASSWORD="$(cat "$S/dp_cert_password")" openssl pkcs12 -export -inkey "$WORK/dp.key" -in "$WORK/dp.crt" -out "$S/dp_cert.pfx" -passout env:CLINIC_DP_PFX_PASSWORD
rm -f "$WORK/dp.key"

step "publish Release builds (api, worker, migrator) and build the SPA"
dotnet publish src/Clinic.Api -c Release -o "$WORK/api" --nologo -v q >/dev/null
dotnet publish src/Clinic.Worker -c Release -o "$WORK/worker" --nologo -v q >/dev/null
dotnet publish src/Clinic.Migrator -c Release -o "$WORK/migrator" --nologo -v q >/dev/null
(cd src/web && npm run build >/dev/null)

step "migrator (first run) and second run = no-op"
CLINIC_MIGRATOR_CONNECTION_FILE="$S/migrator_db_conn" dotnet "$WORK/migrator/Clinic.Migrator.dll"
CLINIC_MIGRATOR_CONNECTION_FILE="$S/migrator_db_conn" dotnet "$WORK/migrator/Clinic.Migrator.dll" | tee "$WORK/second.txt"
grep -q "No pending migration (no-op)." "$WORK/second.txt"

step "initial Manager via the operator command (synthetic smoke user)"
SMOKE_PW="Smoke-$(openssl rand -hex 12)-Aa1"; printf '%s' "$SMOKE_PW" > "$S/smoke_password"
printf '%s\n%s\n' "$SMOKE_PW" "$SMOKE_PW" | ASPNETCORE_ENVIRONMENT=Production CLINIC_APP_DB_CONNECTION_FILE="$S/app_db_conn" \
  DataProtection__CertificatePath="$S/dp_cert.pfx" DataProtection__CertificatePasswordFile="$S/dp_cert_password" \
  dotnet "$WORK/api/Clinic.Api.dll" create-initial-manager smoke.manager > /dev/null
unset SMOKE_PW

step "start api (Production), worker and Caddy"
(cd "$WORK/api" && ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=18080 CLINIC_APP_DB_CONNECTION_FILE="$S/app_db_conn" \
  DataProtection__CertificatePath="$S/dp_cert.pfx" DataProtection__CertificatePasswordFile="$S/dp_cert_password" \
  Security__TrustedProxyNetworks__0=127.0.0.1/32 exec dotnet Clinic.Api.dll > "$WORK/api.log" 2>&1) &
PIDS+=($!)
(cd "$WORK/worker" && DOTNET_ENVIRONMENT=Production CLINIC_WORKER_DB_CONNECTION_FILE="$S/worker_db_conn" \
  CLINIC_HEARTBEAT_FILE="$WORK/heartbeat" exec dotnet Clinic.Worker.dll > "$WORK/worker.log" 2>&1) &
PIDS+=($!)
CLINIC_DOMAIN=localhost CLINIC_API_UPSTREAM=127.0.0.1:18080 CLINIC_WEB_ROOT="$ROOT/src/web/dist" CLINIC_HTTP_PORT=18081 \
  CLINIC_HTTPS_PORT=18443 CLINIC_CADDY_LOCAL_OPTIONS=skip_install_trust XDG_DATA_HOME="$WORK/caddy-data" XDG_CONFIG_HOME="$WORK/caddy-config" \
  "$CADDY_BIN" run --config deploy/caddy/Caddyfile --adapter caddyfile > "$WORK/caddy.log" 2>&1 &
PIDS+=($!)

step "wait for internal readiness"
for i in $(seq 1 60); do
  [[ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:18080/health/ready)" == 200 ]] && break
  sleep 2; [[ $i == 60 ]] && { echo "api not ready"; tail -20 "$WORK/api.log"; exit 1; }
done
(cd "$WORK/api" && ASPNETCORE_HTTP_PORTS=18080 dotnet Clinic.Api.dll --healthcheck) && echo "api --healthcheck: 0"
for i in $(seq 1 30); do curl -sk -o /dev/null https://localhost:18443/health/live && break; sleep 2; done

step "read-only smoke through Caddy (TLS, headers, routing, SPA, cookies, canary login)"
SMOKE_HTTP_BASE=http://localhost:18081 SMOKE_USER=smoke.manager SMOKE_PASSWORD_FILE="$S/smoke_password" \
  bash deploy/scripts/smoke.sh https://localhost:18443 --insecure

step "worker heartbeat and registered jobs"
for i in $(seq 1 20); do [[ -f "$WORK/heartbeat" ]] && break; sleep 2; done
(cd "$WORK/worker" && CLINIC_HEARTBEAT_FILE="$WORK/heartbeat" dotnet Clinic.Worker.dll --healthcheck) && echo "worker --healthcheck: 0"
grep -q 'Clinic worker started with jobs J1-.*J2-.*J3-.*J4-.*J5-.*J7-' "$WORK/worker.log" && echo "worker registered J1, J2, J3, J4, J5, J7"

step "access log scrubbing and production configuration"
curl -sk -o /dev/null "https://localhost:18443/api/sales/patients/00000000-0000-0000-0000-000000000abc?phone=01011112222"
sleep 1
! grep -q '01011112222' "$WORK/caddy.log" || { echo "query string leaked into the access log"; exit 1; }
! grep -q '00000000-0000-0000-0000-000000000abc' "$WORK/caddy.log" || { echo "id leaked into the access log"; exit 1; }
! grep -qi 'cookie' "$WORK/caddy.log" || { echo "headers leaked into the access log"; exit 1; }
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:18080/openapi/manager.json)" == 404 ]] && echo "OpenAPI not mapped in Production"
! grep -qiE 'password=|Options=-c role|BEGIN (RSA|PRIVATE)' "$WORK/api.log" "$WORK/worker.log" || { echo "secret material in app logs"; exit 1; }

printf '\nPROCESS SMOKE: PASS\n'
