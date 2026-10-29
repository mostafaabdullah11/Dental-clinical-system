#!/usr/bin/env bash
# Versioned deploy script (DEPLOYMENT_ARCHITECTURE.md §2.4, §2.7). Installed as /opt/clinic/bin/deploy and reached only through
# the SSH forced command. Usage:
#   deploy web=<registry>/clinic-web@sha256:<64> backend=...@sha256:<64> migrator=...@sha256:<64> backup=...@sha256:<64>
# Steps: 1 verify + pull digests; 2 record the UTC restore point (+ optional provider backup hook); 3 migrator (clinic_migrator);
# 4 start new web/api/worker, previous digests kept as the rollback target; 5 internal readiness + read-only smoke;
# 6 worker heartbeat + registered jobs; 7 record release metadata.
# Failure before step 4 changes nothing. Failure at 5/6 rolls the APPLICATION back to the previous digests (re-smoke).
# The database is NEVER downgraded: a damaged migration is fixed forward, or point-in-time restored by an operator decision.
set -Euo pipefail
umask 027

CLINIC_HOME="${CLINIC_HOME:-/opt/clinic}"
COMPOSE_FILE="${CLINIC_COMPOSE_FILE:-${CLINIC_HOME}/deploy/compose/docker-compose.prod.yml}"
ENV_FILE="${CLINIC_ENV_FILE:-${CLINIC_HOME}/env/clinic.env}"     # non-secret settings (domain, subnet, S3 endpoint …)
RELEASE_ENV="${CLINIC_HOME}/env/release.env"                    # the four image digests currently deployed
PREVIOUS_ENV="${CLINIC_HOME}/env/release.previous.env"
RELEASES="${CLINIC_HOME}/releases"
STAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
DIGEST_RE='^[a-z0-9][a-z0-9._/-]*(:[0-9]+)?/[a-z0-9._/-]+@sha256:[0-9a-f]{64}$'

log() { printf '%s deploy: %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
die() { log "ABORT: $*"; exit 1; }
dc()  { docker compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" --env-file "$1" "${@:2}"; }

exec 9>"${CLINIC_HOME}/deploy.lock"
flock -n 9 || die "another deployment is running (one deploy per environment at a time)"
mkdir -p "${RELEASES}"

declare -A IMG=()
for arg in "$@"; do
  key="${arg%%=*}"; value="${arg#*=}"
  [[ "$key" =~ ^(web|backend|migrator|backup)$ ]] || die "unknown argument '${key}'"
  [[ "$value" =~ $DIGEST_RE ]] || die "${key} must be an image reference pinned by @sha256 digest"
  IMG[$key]="$value"
done
for k in web backend migrator backup; do [[ -n "${IMG[$k]:-}" ]] || die "missing ${k}=<image@sha256:…>"; done

# 1. Verify (optional cosign keyless verification) and pull.
for k in web backend migrator backup; do
  if [[ -n "${COSIGN_IDENTITY_REGEXP:-}" ]]; then
    cosign verify --certificate-identity-regexp "${COSIGN_IDENTITY_REGEXP}" \
      --certificate-oidc-issuer https://token.actions.githubusercontent.com "${IMG[$k]}" >/dev/null || die "signature check failed for ${k}"
  fi
  docker pull -q "${IMG[$k]}" >/dev/null || die "pull failed for ${k}"
done
log "step 1: digests verified and pulled"

# 2. Restore point (UTC) and optional on-demand provider backup.
echo "${STAMP}" > "${RELEASES}/${STAMP}.restore-point"
if [[ -x "${CLINIC_HOME}/hooks/pre-deploy-backup" ]]; then
  "${CLINIC_HOME}/hooks/pre-deploy-backup" "${STAMP}" || die "pre-deploy provider backup hook failed"
fi
log "step 2: restore point ${STAMP}"

NEW_ENV="${RELEASES}/${STAMP}.release.env"
cat > "${NEW_ENV}" <<EOF
CLINIC_WEB_IMAGE=${IMG[web]}
CLINIC_BACKEND_IMAGE=${IMG[backend]}
CLINIC_MIGRATOR_IMAGE=${IMG[migrator]}
CLINIC_BACKUP_IMAGE=${IMG[backup]}
EOF

# 3. Migrations, with clinic_migrator only (the running application is untouched and stays compatible on failure).
dc "${NEW_ENV}" --profile migrate run --rm migrator || die "migrator failed; nothing else was changed"
log "step 3: migrations applied"

wait_ready() { # $1 = env file; internal readiness of api and health of worker
  local env="$1" i
  for i in $(seq 1 40); do
    if dc "$env" exec -T api dotnet api/Clinic.Api.dll --healthcheck >/dev/null 2>&1 \
       && [[ "$(docker inspect -f '{{.State.Health.Status}}' "$(dc "$env" ps -q worker)" 2>/dev/null)" == healthy ]]; then
      return 0
    fi
    sleep 5
  done
  return 1
}

verify_runtime() { # $1 = env file: readiness, smoke, worker jobs, secret separation
  local env="$1" mounts
  wait_ready "$env" || { log "readiness timeout"; return 1; }
  "${CLINIC_HOME}/deploy/scripts/smoke.sh" "https://${CLINIC_DOMAIN}" || return 1
  dc "$env" logs --no-color worker 2>/dev/null | grep -q 'Clinic worker started with jobs J1-.*J2-.*J3-.*J4-.*J5-.*J7-' \
    || { log "worker did not register every recurring job"; return 1; }
  mounts="$(docker inspect -f '{{range .Mounts}}{{.Destination}} {{end}}' "$(dc "$env" ps -q worker)")"
  [[ "$(tr ' ' '\n' <<<"$mounts" | grep -c '^/run/secrets/')" == 1 && "$mounts" == */run/secrets/worker_db_conn* ]] \
    || { log "worker secrets are not exactly worker_db_conn"; return 1; }
  mounts="$(docker inspect -f '{{range .Mounts}}{{.Destination}} {{end}}' "$(dc "$env" ps -q api)")"
  [[ "$mounts" != *migrator_db_conn* && "$mounts" != *backup_* ]] || { log "api holds a forbidden secret"; return 1; }
  return 0
}

# shellcheck disable=SC1090
CLINIC_DOMAIN="$(set -a; . "${ENV_FILE}"; echo "${CLINIC_DOMAIN:-}")"; [[ -n "${CLINIC_DOMAIN}" ]] || die "CLINIC_DOMAIN missing in ${ENV_FILE}"

# 4. Start the new application; keep the previous digests as the rollback target.
[[ -f "${RELEASE_ENV}" ]] && cp "${RELEASE_ENV}" "${PREVIOUS_ENV}"
cp "${NEW_ENV}" "${RELEASE_ENV}"
dc "${RELEASE_ENV}" up -d --no-build --remove-orphans web api worker || log "start reported an error; verifying"
log "step 4: new application started"

# 5 + 6. Readiness, smoke, worker heartbeat / jobs, secret separation.
if verify_runtime "${RELEASE_ENV}"; then
  log "steps 5-6: ready, smoke passed, worker healthy with all jobs"
  printf '{"deployed_utc":"%s","restore_point_utc":"%s","web":"%s","backend":"%s","migrator":"%s","backup":"%s","result":"ok"}\n' \
    "${STAMP}" "${STAMP}" "${IMG[web]}" "${IMG[backend]}" "${IMG[migrator]}" "${IMG[backup]}" > "${RELEASES}/${STAMP}.json"
  log "step 7: release recorded (${RELEASES}/${STAMP}.json)"
  exit 0
fi

# Automatic APPLICATION rollback (never the database).
log "verification failed: rolling the application back to the previous digests"
[[ -f "${PREVIOUS_ENV}" ]] || { log "no previous release to roll back to — operator action required"; exit 2; }
cp "${PREVIOUS_ENV}" "${RELEASE_ENV}"
dc "${RELEASE_ENV}" up -d --no-build --remove-orphans web api worker
if verify_runtime "${RELEASE_ENV}"; then
  printf '{"deployed_utc":"%s","result":"rolled_back"}\n' "${STAMP}" > "${RELEASES}/${STAMP}.json"
  log "rollback succeeded; the previous release is serving (schema stays at the newer, compatible version)"
  exit 1
fi
log "ROLLBACK FAILED — operator alert"; exit 2
