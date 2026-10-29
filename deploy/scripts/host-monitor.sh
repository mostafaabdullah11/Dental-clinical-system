#!/usr/bin/env bash
# Host checks every 5 minutes (DEPLOYMENT_ARCHITECTURE.md §3.5), run by clinic-host-monitor.timer. Pings a Healthchecks-style
# dead-man's switch: success = all good; "/fail" with a FIXED short reason otherwise. Never sends names, ids, paths or text.
# URLs live in root-owned files (no PII in the URL): /opt/clinic/secrets/monitor_ready_url, monitor_host_url.
set -Euo pipefail
CLINIC_HOME="${CLINIC_HOME:-/opt/clinic}"
COMPOSE=(docker compose -f "${CLINIC_HOME}/deploy/compose/docker-compose.prod.yml" --env-file "${CLINIC_HOME}/env/clinic.env"
         --env-file "${CLINIC_HOME}/env/release.env")
url() { [[ -r "${CLINIC_HOME}/secrets/$1" ]] && tr -d '\r\n' < "${CLINIC_HOME}/secrets/$1"; }
ping() { local u; u="$(url "$1")"; [[ -n "$u" ]] && curl -fsS -m 10 -o /dev/null --data-raw "$2" "$u${3:-}" || true; }

# 1. Database connectivity and schema compatibility via the internal readiness probe (not routed publicly).
if "${COMPOSE[@]}" exec -T api dotnet api/Clinic.Api.dll --healthcheck >/dev/null 2>&1; then
  ping monitor_ready_url "ready"
else
  ping monitor_ready_url "not-ready" /fail
fi

# 2. Disk, memory, container restarts outside a deploy window, worker health.
problems=()
disk="$(df -P / | awk 'NR==2 { gsub("%", "", $5); print $5 }')"
(( disk > 90 )) && problems+=("disk-critical") || { (( disk > 80 )) && problems+=("disk-warning"); }
avail="$(awk '/MemAvailable/ { a=$2 } /MemTotal/ { t=$2 } END { printf "%d", a * 100 / t }' /proc/meminfo)"
(( avail < 15 )) && problems+=("memory-low")
dmesg 2>/dev/null | tail -200 | grep -qi 'killed process' && problems+=("oom-kill")
for s in web api worker; do
  id="$("${COMPOSE[@]}" ps -q "$s" 2>/dev/null)"
  [[ -z "$id" ]] && { problems+=("${s}-down"); continue; }
  restarts="$(docker inspect -f '{{.RestartCount}}' "$id")"
  last="${CLINIC_HOME}/run/${s}.restarts"; prev="$(cat "$last" 2>/dev/null || echo "$restarts")"; echo "$restarts" > "$last"
  (( restarts > prev )) && [[ ! -e "${CLINIC_HOME}/deploy.lock.active" ]] && problems+=("${s}-restarted")
done
[[ "$(docker inspect -f '{{.State.Health.Status}}' "$("${COMPOSE[@]}" ps -q worker)" 2>/dev/null)" == healthy ]] || problems+=("worker-unhealthy")

if (( ${#problems[@]} == 0 )); then ping monitor_host_url "ok"; else ping monitor_host_url "${problems[*]}" /fail; fi
