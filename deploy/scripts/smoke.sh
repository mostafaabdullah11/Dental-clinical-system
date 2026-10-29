#!/usr/bin/env bash
# Read-only deployment smoke test (DEPLOYMENT_ARCHITECTURE.md §1.13, §2.4 step 5). Makes NO writes and uses NO patient data.
#   smoke.sh https://<domain> [--insecure]
# Optional staging canary (synthetic user only): SMOKE_USER + SMOKE_PASSWORD_FILE → login, cookie flags, /api/me, logout.
# SMOKE_HTTP_BASE overrides the plain-HTTP base used for the redirect check (non-default local ports).
set -Euo pipefail
BASE="${1:?usage: smoke.sh https://<domain> [--insecure]}"; BASE="${BASE%/}"
CURL=(curl -sS -m 15 --proto '=https,http')
[[ "${2:-}" == "--insecure" ]] && CURL+=(-k)
FAIL=0
ok()   { printf '  ok    %s\n' "$*"; }
bad()  { printf '  FAIL  %s\n' "$*"; FAIL=1; }
code() { "${CURL[@]}" -o /dev/null -w '%{http_code}' "$@" || echo 000; }
hdrs() { "${CURL[@]}" -D - -o /dev/null "$@" | tr -d '\r'; }

echo "smoke: ${BASE}"
[[ "$(code "${BASE}/health/live")" == 200 ]] && ok "/health/live 200" || bad "/health/live not 200"
[[ "$(code "${BASE}/health/ready")" == 404 ]] && ok "/health/ready not routed externally" || bad "/health/ready reachable externally"
for p in /openapi/manager.json /swagger /hangfire /api/unassigned/x; do
  c="$(code "${BASE}${p}")"; [[ "$c" == 404 ]] && ok "${p} -> 404" || bad "${p} -> ${c}"
done
[[ "$(code "${BASE}/api/me")" == 401 ]] && ok "/api/me without session -> 401" || bad "/api/me without session not 401"

HTTP_BASE="${SMOKE_HTTP_BASE:-http://${BASE#https://}}"
c="$(code "${HTTP_BASE}/")"; [[ "$c" =~ ^(301|302|307|308)$ ]] && ok "HTTP redirects to HTTPS (${c})" || bad "HTTP did not redirect (${c})"

H="$(hdrs "${BASE}/")"
for h in "strict-transport-security: max-age=" "x-content-type-options: nosniff" "referrer-policy: no-referrer" \
         "frame-ancestors 'none'" "permissions-policy:" "cache-control: no-store"; do
  grep -qi -- "$h" <<<"$H" && ok "SPA header: ${h}" || bad "SPA header missing: ${h}"
done
grep -qi '^server:' <<<"$H" && bad "Server header exposed" || ok "no Server header"
c="$(code "${BASE}/patients/some-spa-route")"; [[ "$c" == 200 ]] && ok "SPA fallback to index.html" || bad "SPA fallback ${c}"
"${CURL[@]}" "${BASE}/" | grep -q 'dir="rtl"' && ok "index.html is Arabic RTL" || bad "index.html not RTL"

A="$(hdrs "${BASE}/api/auth/csrf")"
grep -qiE '^cache-control:.*no-store' <<<"$A" && ok "API no-store" || bad "API response cacheable"
grep -i '^set-cookie: __Host-clinic.csrf' <<<"$A" | grep -qi 'secure' && grep -i '^set-cookie: __Host-clinic.csrf' <<<"$A" | grep -qi 'samesite=strict' \
  && ok "anti-forgery cookie __Host-, Secure, SameSite=Strict" || bad "anti-forgery cookie flags"

if [[ -n "${SMOKE_USER:-}" && -r "${SMOKE_PASSWORD_FILE:-}" ]]; then
  JAR="$(mktemp)"; trap 'rm -f "$JAR"' EXIT
  TOKEN="$("${CURL[@]}" -c "$JAR" -b "$JAR" "${BASE}/api/auth/csrf" | sed -E 's/.*"token":"([^"]+)".*/\1/')"
  PW="$(tr -d '\r\n' < "${SMOKE_PASSWORD_FILE}")"
  L="$("${CURL[@]}" -c "$JAR" -b "$JAR" -D - -o /dev/null -H "X-CSRF-TOKEN: ${TOKEN}" -H 'Content-Type: application/json' \
       --data "{\"userName\":\"${SMOKE_USER}\",\"password\":\"${PW}\"}" "${BASE}/api/auth/login" | tr -d '\r')"
  unset PW
  S="$(grep -i '^set-cookie: __Host-clinic.session' <<<"$L")"
  [[ "$S" =~ [Ss]ecure && "$S" =~ [Hh]ttp[Oo]nly && "$S" =~ [Ss]ame[Ss]ite=[Ss]trict && ! "$S" =~ [Dd]omain= ]] \
    && ok "session cookie __Host-, Secure, HttpOnly, SameSite=Strict, no Domain" || bad "session cookie flags"
  [[ "$("${CURL[@]}" -b "$JAR" -o /dev/null -w '%{http_code}' "${BASE}/api/me")" == 200 ]] && ok "canary login /api/me 200" || bad "canary /api/me"
  TOKEN="$("${CURL[@]}" -c "$JAR" -b "$JAR" "${BASE}/api/auth/csrf" | sed -E 's/.*"token":"([^"]+)".*/\1/')"
  "${CURL[@]}" -b "$JAR" -o /dev/null -X POST -H "X-CSRF-TOKEN: ${TOKEN}" "${BASE}/api/auth/logout" && ok "canary logout"
fi

[[ $FAIL -eq 0 ]] && echo "smoke: PASS" || echo "smoke: FAIL"
exit $FAIL
