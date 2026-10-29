#!/usr/bin/env bash
# F5.21 local production-like validation (developer machine with Docker, and the CI "compose-smoke" job). Synthetic only.
# Proves: images build; migrations apply and a second migrator run is a no-op; web/api/worker start healthy; read-only smoke
# (headers, routing, SPA fallback, hidden internal routes, cookies); per-container secret mounts; only web publishes ports;
# database role separation; the backup job produces a verified dump decryptable by EACH test recovery key. Tears down after.
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
DC=(docker compose -f deploy/compose/docker-compose.prod.yml -f deploy/compose/docker-compose.local.yml --env-file deploy/local/.env)
trap '"${DC[@]}" --profile migrate --profile backup down -v --remove-orphans >/dev/null 2>&1 || true' EXIT
step() { printf '\n== %s\n' "$*"; }

[[ -f deploy/local/.env ]] || bash deploy/local/generate-secrets.sh
step "build images";              "${DC[@]}" --profile migrate --profile backup build
step "start postgres";            "${DC[@]}" up -d --wait postgres
step "migrate (first run)";       "${DC[@]}" --profile migrate run --rm migrator
step "migrate again (no-op)";     "${DC[@]}" --profile migrate run --rm migrator | tee /dev/stderr | grep -q "No pending migration (no-op)."
step "start web, api, worker";    "${DC[@]}" up -d --wait web api worker
step "read-only smoke";           bash deploy/scripts/smoke.sh https://localhost:8443 --insecure

step "secret mounts per container"
mounts() { docker inspect -f '{{range .Mounts}}{{.Destination}} {{end}}' "$("${DC[@]}" ps -q "$1")"; }
[[ "$(mounts worker | tr ' ' '\n' | grep '^/run/secrets/' | sort | tr '\n' ' ')" == "/run/secrets/worker_db_conn " ]] || { echo "worker secrets wrong"; exit 1; }
[[ "$(mounts api | tr ' ' '\n' | grep '^/run/secrets/' | sort | tr '\n' ' ')" == "/run/secrets/app_db_conn /run/secrets/dp_cert /run/secrets/dp_cert_password " ]] \
  || { echo "api secrets wrong"; exit 1; }
[[ "$(mounts web | tr ' ' '\n' | grep -c '^/run/secrets/' || true)" == 0 ]] || { echo "web has secrets"; exit 1; }

step "only web publishes ports"
for s in api worker postgres; do
  [[ -z "$(docker port "$("${DC[@]}" ps -q "$s")")" ]] || { echo "${s} publishes a port"; exit 1; }
done

step "database role separation"
psql_as() { "${DC[@]}" exec -T postgres psql -U postgres -d clinic -v ON_ERROR_STOP=1 -Atc "SET ROLE $1; $2"; }
! psql_as clinic_app "DELETE FROM finance.payments" 2>/dev/null || { echo "clinic_app could delete"; exit 1; }
! psql_as clinic_worker "SELECT 1 FROM infra.data_protection_keys" 2>/dev/null || { echo "worker read DP keys"; exit 1; }
! psql_as clinic_backup "SELECT 1 FROM infra.data_protection_keys" 2>/dev/null || { echo "backup read DP keys"; exit 1; }
psql_as clinic_backup "SELECT count(*) FROM finance.payments" >/dev/null

step "backup job + decrypt with each test recovery key"
"${DC[@]}" --profile backup run --rm -e BACKUP_OUTPUT_DIR=/var/lib/clinic-backup/out backup
for k in 1 2; do
  "${DC[@]}" --profile backup run --rm -v "$PWD/deploy/local/secrets/test-recovery-${k}.agekey:/key:ro" --entrypoint sh backup -c \
    'f=$(ls -t /var/lib/clinic-backup/out/*.dump.age | head -1); age -d -i /key -o /tmp/d "$f" && pg_restore --list /tmp/d >/dev/null && echo "key '"$k"' decrypts"'
done

printf '\nCOMPOSE SMOKE: PASS\n'
