# DEPLOYMENT_ARCHITECTURE.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL
**Scope:** Steps 12 (Deployment), 13 (CI/CD) and 14 (Backup, Restore, Monitoring, Alerting) as approved, including the Steps 12–13 corrections (schema compatibility, non-transactional migrations, Data Protection key grants) and the Step 14 corrections (post-restore password security, `clinic_backup` grants, J7 inactive-owner lookup, backup-key custody). This document adds no business rule, no business table and no permission assignment.

Labels: **CONFIRMED** = from the user's requirements (Docker, Linux, HTTPS, cloud-hosted server, managed PostgreSQL preferred, reverse proxy, Development/Staging/Production, configurable clinic timezone, GitHub Actions, no destructive migration at startup, no Kubernetes, automated encrypted off-site backups, retention, restore testing, PITR if supported, simple affordable monitoring, RPO/RTO in clinic language). **TECHNICAL** = engineering design. **DEFAULT** = veto-able value. **OPEN** = undecided.

---

# PART 1: DEPLOYMENT (Step 12)

## 1.1 Topology (TECHNICAL)

```
Internet
   │  443 (HTTPS), 80 (redirect and certificate issuance only)
   ▼
┌──────────────────────── Linux VM (cloud, one host) ───────────────────────────┐
│ ┌── web ──────────────┐  internal Docker network (no published ports)         │
│ │ Caddy: TLS, headers,│──► api    (ASP.NET Core, Clinic.Api)                  │
│ │ SPA static files,   │                                                       │
│ │ /api reverse proxy  │    worker (Hangfire server, Clinic.Worker)            │
│ └─────────────────────┘    migrator (one-shot, deploy time only)              │
│                            backup   (one-shot, nightly)                       │
│  SSH: key only, forced deploy command; Hangfire dashboard on 127.0.0.1 only   │
└──────────────┬────────────────────────────────────────────────────────────────┘
               │ TLS (verify-full), allow-listed to the VM only
               ▼
      Managed PostgreSQL ──► automated backups / point-in-time recovery
```

One host, three long-running containers (web, api, worker), two one-shot containers (migrator, backup), one database. Follows the "no Kubernetes" rule and is operable by one person.

## 1.2 Components

| Container | Contents | Notes |
|---|---|---|
| **web** | Caddy plus the built React app | The only published ports (80, 443). Static files and `/api/*` proxy. One origin, so the cookie design needs no CORS. |
| **api** | ASP.NET Core, non-root | Not published. Reached only by `web`. Hangfire server off. **The only container holding `dp_cert`.** |
| **worker** | Same backend image, Worker entrypoint | Hangfire server and all jobs. One instance. Own DB role and secrets. Dashboard binds to `127.0.0.1` on the host (SSH tunnel). **No `dp_cert`, no migrator/app secret.** |
| **migrator** | Self-contained EF Core migration bundle | Started only by the deploy script with `clinic_migrator` credentials; never running during normal operation |
| **backup** | Dump + `age` encryption + upload tooling | One-shot under a profile (like the migrator); holds `clinic_backup` credentials, write-only upload credentials and the two **public** keys; **no private key** |

One backend image serves api and worker.

## 1.3 Reverse proxy

**Caddy** (automatic Let's Encrypt, few lines of configuration). HTTPS with HTTP→HTTPS redirect, HSTS, TLS ≥ 1.2. Routing: `/api/*` and `/health/live` to `api`; everything else serves the SPA with fallback to `index.html`. **`/health/ready` is not exposed publicly** (internal network: Docker healthchecks and the deploy script). The Hangfire dashboard and Swagger are not routed (404 in production); **no `/api/unassigned/*` route is mapped by the application**. Security headers (CSP, `nosniff`, `Referrer-Policy: no-referrer`, `frame-ancestors 'none'`, `Permissions-Policy`) are applied at the proxy for static files too; the API sets `no-store` on its own responses. Hashed assets cached long; `index.html` `no-store`. Caddy sets `X-Forwarded-For`/`-Proto`; the API trusts them **only from the internal Docker network**. Access log: method, route, status; **no query strings, no bodies**. 1 MB body limit (no uploads in V1) and sensible timeouts.

## 1.4 Managed PostgreSQL versus a container

**Recommendation: managed PostgreSQL for staging and production**; a PostgreSQL container on the VM is Development only. If budget forces a container in production, the minimum is nightly encrypted dumps plus WAL archiving, an off-host copy and a tested restore (clearly higher risk; not recommended).

Provider selection criteria (provider itself undecided, non-blocking): `pg_trgm` and `btree_gist` allowed; non-superuser creation of roles and table ownership; TLS with verifiable CA, firewall allow-listing (optionally private networking); point-in-time recovery, encrypted backups and documented retention; PostgreSQL 16 or newer; region near the clinic with compliance checked; documented PITR granularity and cross-region backup support.

## 1.5 Host and container hardening

SSH key-only, no root login, deploy key limited by a **forced command** to one deploy script; provider firewall + `ufw`, only 80/443/SSH open, `fail2ban` on SSH; unattended security upgrades with reboot window outside clinic hours; Docker non-root, `no-new-privileges`, capabilities dropped (Caddy keeps only what it needs to bind 80/443), read-only root filesystems with tmpfs where needed, CPU/memory limits, no privileged containers, no Docker socket mounts; slim base images pinned **by digest** in production and rebuilt on a schedule; egress restricted to what is needed (certificate issuance, the database, OS updates, backup storage endpoint, heartbeat service, error tracker); NTP on the host, containers in UTC (the clinic timezone is a database setting, never an OS setting); JSON logs to stdout with Docker log rotation.

## 1.6 Environments

| | Development | Staging | Production |
|---|---|---|---|
| Purpose | Day-to-day coding | Production-like rehearsal and UAT | The clinic |
| Where | Developer machine, Docker Compose | Small VM, real domain and certificate | Production VM |
| Database | PostgreSQL container, same roles bootstrapped by the same script | Separate managed instance (DEFAULT) | Managed PostgreSQL |
| Data | **Synthetic seed only** | **Synthetic only; production data is never copied down** | Real |
| Swagger/OpenAPI UI | On | On, behind login | **Off** |
| CORS | Local SPA origin only | Explicit origin list | **Off**, except the clinic-website origin on the one public feedback route (if chosen) |
| Secrets | Local dev values | Staging-only values | Production-only values, never reused |
| Background jobs | On | On | On |
| Sensitive EF logging | May be on | **Off** (startup check) | **Off** (startup check) |
| Domain | `localhost` | Staging subdomain | Clinic domain |

Staging has its own cookie scope through its own host. Real patient data is not allowed in staging.

## 1.7 Configuration and secrets

Non-secret settings: environment variables in a per-environment compose environment file. Secrets: Docker secrets mounted as files under `/run/secrets` (`*_FILE` convention), sourced from a root-owned `600` directory on the VM, rotated by the operator; **never** in images, the repository or CI logs. Connection strings: one per role (`clinic_app` for api, `clinic_worker` for worker, `clinic_migrator` **only for the migrator container**, `clinic_backup` only for the backup container); the api and worker never see the migrator password. Business settings (clinic timezone, visibility switches, lab percentage) live in the database, are audited and changed by the Manager. Certificates managed by Caddy in a named volume (loss only triggers re-issuance). Role bootstrap: `deploy/db/bootstrap-roles.sql` creates the roles once per environment (passwords cannot live in migrations); migrations then `GRANT` to those names.

## 1.8 Data Protection keys (least privilege)

Key ring stored in `infra.data_protection_keys`, encrypted by a certificate supplied as a Docker secret.

| Item | Decision |
|---|---|
| Table | Created and owned by `clinic_migrator`; identity-column id (no sequence grant needed) |
| `clinic_app` | **SELECT and INSERT only** (read the ring, insert new keys and revocation entries). No UPDATE/DELETE/TRUNCATE. If the integration test shows another privilege is required, only that one is added, narrowly and documented. |
| `clinic_worker` | **No access.** The Worker does not authenticate browser sessions, issue cookies/tokens or use anti-forgery; its composition root does not register persisted Data Protection. |
| `clinic_backup`, `clinic_doctor_read`, PUBLIC | **No access** |
| Deletion | Never by the application. Cleanup, if ever wanted, is an operator action using migrator credentials. |
| Audit | The audit interceptor skips this table |
| Certificate | Docker secret **mounted only in the `api` container** |
| Operator commands | First-Manager command, reset-token issuance and `post-restore-lockdown` run with `docker compose exec api …` (it has the certificate and the app role) |

Lost keys invalidate every session and outstanding reset token; no data is lost. After a restore, the key table is empty and the application creates a fresh key ring on first use.

## 1.9 Compose sketch (illustrative)

```yaml
services:
  web:
    image: ghcr.io/ORG/clinic-web@${WEB_DIGEST}
    ports: ["80:80", "443:443", "443:443/udp"]
    networks: [edge, internal]
    volumes: [caddy_data:/data]
    depends_on: { api: { condition: service_healthy } }
    restart: unless-stopped
  api:
    image: ghcr.io/ORG/clinic-backend@${BACKEND_DIGEST}
    command: ["dotnet", "Clinic.Api.dll"]
    networks: [internal]
    secrets: [app_db_conn, dp_cert]
    read_only: true
    healthcheck: { test: ["CMD", "dotnet", "Clinic.Api.dll", "--healthcheck"], interval: 15s }
    restart: unless-stopped
  worker:
    image: ghcr.io/ORG/clinic-backend@${BACKEND_DIGEST}
    command: ["dotnet", "Clinic.Worker.dll"]
    ports: ["127.0.0.1:5050:5050"]   # dashboard, SSH tunnel only
    networks: [internal]
    secrets: [worker_db_conn]        # no dp_cert
    restart: unless-stopped
  migrator:
    image: ghcr.io/ORG/clinic-migrator@${MIGRATOR_DIGEST}
    profiles: ["migrate"]
    networks: [internal]
    secrets: [migrator_db_conn]
  backup:
    image: ghcr.io/ORG/clinic-backup@${BACKUP_DIGEST}
    profiles: ["backup"]
    networks: [internal]
    secrets: [backup_db_conn, backup_upload_credentials, backup_public_keys]   # no private key
networks: { edge: {}, internal: { internal: false } }
volumes: { caddy_data: {} }
secrets:
  app_db_conn:                { file: /opt/clinic/secrets/app_db_conn }
  worker_db_conn:             { file: /opt/clinic/secrets/worker_db_conn }
  migrator_db_conn:           { file: /opt/clinic/secrets/migrator_db_conn }
  backup_db_conn:             { file: /opt/clinic/secrets/backup_db_conn }
  backup_upload_credentials:  { file: /opt/clinic/secrets/backup_upload_credentials }
  backup_public_keys:         { file: /opt/clinic/secrets/backup_public_keys }
  dp_cert:                    { file: /opt/clinic/secrets/dp_cert.pfx }
```

The `internal` network needs outbound reach to the managed database, so it is not marked isolated; the provider firewall and database allow-list control that path. Production uses image **digests**; staging may use tags.

## 1.10 Availability and host loss

One VM is a single point of failure: if it dies the clinic is down until a replacement is up; the data is safe in the managed database. The VM is replaceable: state lives in the managed database, the secrets directory (backed up separately, encrypted), the repository (`deploy/` holds compose files, Caddyfile, bootstrap script and rebuild procedure) and the registry. Rebuild: provision, bootstrap, restore secrets, point DNS, deploy. Deploys restart api/worker in seconds and sessions survive (keys in the database); run outside clinic hours by convention. High availability across two hosts is **not** proposed.

## 1.11 Sizing defaults (DEFAULT)

VM 2 vCPU / 4 GB RAM / 40 GB disk; managed PostgreSQL smallest production tier with ≥ 2 GB RAM and 20 GB storage (autoscaling if offered); staging half size. Measured and adjusted after UAT.

## 1.12 Points to be aware of

1. **Data residency and legal compliance for health data** need a legal check before choosing the region; nothing is assumed (OPEN, OPS-2).
2. Cost versus risk: managed database plus a separate staging instance is safer and more expensive.
3. Provider choice is deliberately open; nothing depends on a vendor.

## 1.13 Deployment verification checklist (the smoke test)

1. Only ports 80, 443 and SSH respond; `api` and `worker` unreachable from outside.
2. HTTP redirects to HTTPS; certificate valid; HSTS and security headers present.
3. `/health/live` OK externally; `/health/ready` not routed externally.
4. Swagger and the Hangfire dashboard return 404 in production; **no `/api/unassigned/*` route is mapped**.
5. `GET /api/me` without a session returns 401.
6. Cookie is `__Host-`, Secure, HttpOnly, SameSite=Strict.
7. api and worker connect as `clinic_app` / `clinic_worker`; a forbidden statement fails; the migrator secret is not present in api or worker; **the worker container's `/run/secrets` holds only `worker_db_conn`**.
8. `EnableSensitiveDataLogging` off; production CORS off except the single public route.
9. The worker heartbeat reports every recurring job registered.
10. Restarting `api` keeps sessions valid (Data Protection keys persist).
11. The clinic timezone setting is present; VM and containers run UTC.
12. Readiness uses the compatibility check (section 2.5).

---

# PART 2: CI/CD (Step 13)

**CONFIRMED** GitHub Actions. PR pipeline: restore, backend build and tests, frontend install/build/tests. Main/release pipeline: build, test, Docker images, push, deploy, safe database migration, health check. **No destructive migration at application startup.**

## 2.1 Repository and branching

One monorepo (`backend/`, `frontend/`, `deploy/`, `docs/`). Trunk-based; short-lived branches into `main`; `main` always releasable; release tag `vYYYY.MM.DD.N`. Branch protection: PR required, required checks green, no direct or force pushes. `CODEOWNERS` marks the highest-cost paths: permission seed and role mappings, `CrossModuleReferences.cs`, migrations, the finance module, `deploy/`, `.github/workflows/`.

## 2.2 Pull request pipeline

| Job | Steps | Fails when |
|---|---|---|
| **backend** | Restore; build with warnings as errors; unit tests; **architecture tests** (module levels, no contact type under `Features/Doctor`, no `doctorId` on own routes, every FK listed in the cross-module file); **integration tests against real PostgreSQL** (Testcontainers) including canary scans, generated role matrix, startup route test, finance and notification tests, **database grant tests** as `clinic_app` and `clinic_worker` | Any failure |
| **frontend** | `npm ci`, lint (incl. ban on `dangerouslySetInnerHTML`), type check, unit tests, production build | Any failure |
| **contract** | Generate the OpenAPI documents and TypeScript clients, check the working tree is unchanged | Drift, a contact property in the Doctor document, or a clinical property in the Sales document |
| **migrations** | No model change without a migration; generate idempotent SQL; **destructive-operation scan**; apply to an empty database and to the previous release's schema; every table in its owning module's schema; **N-1 compatibility job** (2.6); non-transactional marker checks (2.5) | Pending model changes, unapproved destructive operation, table outside its schema, or a new table/column with **no grant decision and no backup-grant decision** |
| **security** | `gitleaks`; `dotnet list package --vulnerable --include-transitive`; `npm audit --audit-level=high`; CodeQL (C# and JS); Dockerfile lint | Secret, high/critical vulnerability, CodeQL error |
| **images** | Build without pushing; Trivy scan | Critical vulnerability with a fix available |

Integration tests use the real role bootstrap script so tests exercise the same grants and triggers (G1–G7) as production. Compose lint: `worker` has no `dp_cert` and no migrator/app secret; `api` has no migrator secret; `backup` secrets exist only in the `backup` service. End-to-end tests (Playwright against the Compose stack, including XSS payloads) run nightly and as a required gate before a production release. A PR comment attaches the generated migration SQL.

## 2.3 Main and release pipeline

On push to `main`: re-run the full PR checks (reusable workflow); build the images (`clinic-web`, `clinic-backend`, `clinic-migrator`, `clinic-backup`) with Buildx and cache; tag with the **commit SHA** and push to GHCR, recording **digests**; SBOM and Trivy scan; optional cosign keyless signing verified by the deploy script; **automatically deploy to staging**. **Production release:** a tag `vYYYY.MM.DD.N` or manual `workflow_dispatch` choosing an already-built SHA. It builds nothing and deploys the exact digests tested in staging.

## 2.4 Deployment

GitHub Environments `staging` and `production`; production needs **manual approval by a required reviewer**; one deploy per environment at a time. GitHub holds only deploy credentials (SSH key, host, pinned host key). The workflow connects over SSH with a key restricted to the **forced command** `/opt/clinic/bin/deploy <release>`.

| # | Deploy-script step | On failure |
|---|---|---|
| 1 | Verify image signatures and digests; pull | Abort; nothing changed |
| 2 | **Record the UTC time** (and an on-demand provider backup if offered): the restore point | Abort |
| 3 | Run the migrator one-shot with `clinic_migrator` | Abort; the running app is untouched and still compatible |
| 4 | Start new api, worker, web; save previous digests as the rollback target | |
| 5 | Wait for readiness (internal `/health/ready`) and run the smoke test | Rollback (2.7) |
| 6 | Verify worker heartbeat and registered recurring jobs | Rollback |
| 7 | Report to the workflow; record release metadata | |

The production smoke test makes no writes and uses no real patient data. Staging smoke adds a canary run (login as seeded synthetic users per role).

## 2.5 Safe database migrations

**The application never migrates itself.** No `Database.Migrate()` at startup. Only the migrator, run by the deploy script, changes the schema.

**Readiness is compatibility, not equality.** The app requires the migrations compiled into its own assembly (list embedded at build time). `/health/ready` reads `infra.__EFMigrationsHistory` and computes `required − applied`: empty = pass; non-empty = not ready (`schema.behind`). **Extra applied migrations are ignored**; "latest applied" is never compared with "latest expected". `clinic_app` and `clinic_worker` have SELECT on the history table only; the query doubles as the connectivity check; result cached 30 seconds; the Worker heartbeat applies the same check.

**Expand and contract.** Every migration must work with the previous release's code.

| Change | How |
|---|---|
| New table/column | Additive; a new NOT NULL column is added nullable/with default first, tightened later |
| Rename/move | Add new, copy, switch code, drop the old one in a **later** release |
| Drop column/table, change type | Only in a later release; blocked by the destructive scan until a reviewer adds an explicit approval marker with the reason. A contract migration may ship only in a release whose **immediately previous release no longer references the dropped object**; the marker records that statement. |
| New index on a large table | `CREATE INDEX CONCURRENTLY` in a marked non-transactional migration |
| Large backfill | Batched job, not a migration |
| Permission/role mapping changes | Reviewed migration; idempotent inserts |
| Reference data | Idempotent migrations |
| Grants | Every new table/column gets explicit grants in its migration **and a backup-grant decision** |
| Triggers G1–G7, cross-module FKs | `CODEOWNERS` review; cross-module FKs in a separate `Xref` migration |

**Transactions (corrected wording).** Most PostgreSQL DDL migrations run transactionally: each EF migration runs in its own transaction and a failed one leaves that migration's changes unapplied. A release with several migrations can stop midway; earlier migrations stay committed and each is backward-compatible; re-running the migrator continues from the next. Migrations with explicitly non-transactional operations (e.g. `CREATE INDEX CONCURRENTLY`) are exceptions; a failure there may leave partial state.

**Non-transactional migration rule**

| Rule | Detail |
|---|---|
| Identified in CI | Must carry `[NonTransactionalMigration("reason")]`. CI fails if the source contains `suppressTransaction` or the generated SQL contains `CONCURRENTLY`, `VACUUM` or `CREATE/DROP DATABASE` and the marker is missing; also fails if the marker is present with no such operation. PR comment flags them. |
| Single purpose | Only the non-transactional operations; CI fails on other DDL/DML in the same migration |
| One statement per command | Each operation is its own `Sql()` call |
| Safe to retry | `IF NOT EXISTS` / `IF EXISTS` throughout; history row written only after success |
| Invalid-index trap | A failed `CREATE INDEX CONCURRENTLY` leaves an INVALID index; the migrator **pre-flight** finds `NOT indisvalid` indexes in application schemas and drops them with `DROP INDEX CONCURRENTLY IF EXISTS` (logged) |
| Failure behavior | Deployment stops at step 3; partial state is additive only |
| Retry | Re-running the deploy runs the migrator again safely after the cause is fixed |

**Destructive-operation scan** fails on `DROP TABLE`, `DROP COLUMN`, `TRUNCATE`, `DELETE`, `ALTER COLUMN … TYPE`, `SET NOT NULL` on an existing column unless approved. **Down migrations are not relied on in production**; fix forward, or point-in-time restore from the recorded time. The production run uses the **same migration bundle** CI applied to an empty database and to the previous release's schema. The first Manager account is created by a one-time operator command, never CI or a migration.

## 2.6 CI "N-1 compatibility" job

Runs on every PR and on `main` once a previous release exists (first release skips it):
1. Create a database at the previous release's schema using that release's bundle.
2. Start the previous release's image against it and confirm it is Ready.
3. Apply the new migrator while the previous app keeps running under light smoke traffic.
4. Assert the previous app stays Ready and the smoke suite passes (login per synthetic role, patient search, an idempotent payment on synthetic data, one reminder-job tick), with no log errors.
5. Start the new app; confirm Ready.
6. **Rollback drill:** stop the new app, start the previous one; assert Ready and the same smoke suite.

## 2.7 Rollback

| Failure | Response |
|---|---|
| Image pull, migrator or health check fails after step 4 | **Automatic application rollback** to the saved previous digests; re-run the smoke test. The previous image starts against the newer schema and is Ready because everything it requires is present and the extra migrations are additive. |
| Rollback itself fails | Deploy fails loudly; operator alerted |
| A migration damaged data | Forward fix first; otherwise point-in-time restore to the recorded time (manual operator decision) |
| Manual rollback | `workflow_dispatch` deploying a previous digest. **Only the immediately previous release is guaranteed compatible**; going further back needs review. |

There is **no automatic database downgrade**. Rolling back the application never rolls back the database.

## 2.8 Supply chain and workflow security

Third-party Actions pinned by commit SHA; Dependabot (NuGet, npm, Docker, Actions) weekly; minimum `GITHUB_TOKEN` permissions; forks receive no secrets; deploy workflows run only from `main` and tags; production deploy secrets only in the `production` environment; build logs never print secrets; images built from lockfiles.

## 2.9 Where earlier tests run

Canary scans, role matrix, startup route test, architecture and scope tests: PR backend job. Grant/trigger tests, finance and notification tests: PR backend job against real PostgreSQL. OpenAPI/client drift: contract job. XSS/CSRF/cookies: backend job for headers/cookies, Playwright nightly and pre-release for XSS. OWASP ZAP baseline against staging after each staging deploy (advisory). Smoke and worker heartbeat: every deploy. Manual penetration checklist: go-live gate.

## 2.10 Tests added for Steps 12–13

| # | Test | Where |
|---|---|---|
| 1 | Previous app stays Ready and functional after the next release's additive migration is applied while it runs | CI N-1 |
| 2 | Immediate rollback works while the newer compatible schema remains | CI N-1 |
| 3 | New app against a database missing a required migration is not Ready (`schema.behind`); old app against a newer database is Ready | CI |
| 4 | Transaction-suppressed operations are detected: missing marker fails, marker without an operation fails, extra statements in a marked migration fail | CI |
| 5 | A failed non-transactional migration retries safely (kill mid index build, re-run; invalid index cleaned up; final schema dump identical to a clean apply) | CI |
| 6 | Running the migrator twice on a finished database is a no-op | CI |
| 7 | `clinic_app` has only SELECT/INSERT on the key table; a cookie issued by instance A validates on instance B; rotation inserts a row | CI |
| 8 | `clinic_worker` cannot SELECT `infra.data_protection_keys`; it can SELECT the migration history only | CI |
| 9 | Compose lint (worker has no `dp_cert`/migrator/app secret; api has no migrator secret; backup secrets only in backup); smoke test confirms `/run/secrets` contents per container | CI and deploy |

---

# PART 3: BACKUP, RESTORE, MONITORING, ALERTING (Step 14)

## 3.1 What is protected

| Asset | Where | Recovery |
|---|---|---|
| Business data (patients, clinical, ledger, payroll, audit, Hangfire state) | Managed PostgreSQL | **Backups** |
| Code, images, deploy scripts, logo asset | GitHub, registry, `deploy/` | Rebuildable; no backup |
| Secrets (DB role passwords, `dp_cert`) | VM secrets directory | Encrypted copy off the VM; passwords re-issuable |
| Caddy certificates | Docker volume | Re-issued automatically |
| Logs | Host, rotated | Not backed up; audit history is in the database |
| Uploaded files | **None exist in V1** | If expense attachments are later required (Q-EXP-1), file backup is added then |

No business data is encrypted with an application-held key, so a lost certificate only signs everyone out. The **backup decryption key** is the one key whose loss is serious.

## 3.2 RPO and RTO in clinic language

RPO: how much recent work might have to be re-entered. RTO: how long the system might be unavailable. **DEFAULT, for the Manager's acceptance, not blocking:**

| Scenario | Clinic experience | RPO | RTO |
|---|---|---|---|
| **A. VM fails, database fine** | Down until a replacement server is up; no data lost | **None** | **4 h** (about 1–2 h once rehearsed) |
| **B. Database damaged / bad change corrupts data** | Restore to just before the problem; later entries re-entered | **5 min** | **4 h** |
| **C. Cloud provider or account lost** | Rebuild elsewhere from the off-site nightly copy; up to a day re-entered | **24 h** | **1 business day** |

Scenario B's 5 minutes depends on the provider's documented PITR granularity and is not claimed until verified at provider selection. The clinic needs a manual paper process for scenarios A–C; that is the clinic's operational decision and no feature is designed for it.

## 3.3 Backup design (three layers)

| Layer | What | Protects against |
|---|---|---|
| 1. Provider backups with PITR | Continuous, encrypted at rest, other zone/region if offered | Scenarios A and B |
| 2. Independent off-site nightly logical backup | `pg_dump` by the one-shot `backup` container, encrypted client-side, uploaded to a **different provider or account** | Scenario C, provider account compromise |
| 3. Pre-deploy restore point | UTC time + on-demand backup recorded at deploy step 2 | A bad release/migration |

**Layer 2 details**

| Item | Decision |
|---|---|
| Schedule (DEFAULT) | Daily 23:00 UTC via systemd timer on the VM (fixed UTC, DST never moves it) |
| Runs as | `docker compose run backup` |
| **DB access: `clinic_backup`** | Explicit SELECT only. **No `pg_read_all_data`.** Grants: USAGE on each business schema, `identity`, `audit`, `configuration`, `hangfire`; SELECT on every table in those schemas and on their sequences; SELECT on `infra.__EFMigrationsHistory`. **`infra.data_protection_keys`: no grant (SELECT fails).** Anything else in `infra` and every write privilege: none. Connection from the VM address only, TLS required, `CONNECTION LIMIT 2`. Held **only** in the backup container's secret. The CI grant-decision rule requires a backup-grant decision for every new table. **v1.1:** `staff.assistants` and `payroll.assistant_payrolls` are inside the granted schemas and are explicitly SELECT-able by `clinic_backup`; `clinic_worker` has no access to either; no new secret, container or topology change. |
| `pg_dump` flags | `--exclude-table-data=infra.data_protection_keys` (definition kept so a restore is structurally complete; no data read, so no SELECT needed). Triggers, functions, constraints, ACLs read from catalogs. |
| Consistency | One transaction snapshot for the dump, manifest and integrity checks |
| Contents | The whole database incl. `hangfire` and `infra.__EFMigrationsHistory`; **no Data Protection key rows** |
| **Encryption** | Client-side with `age` to **two independent recovery recipients**. **Either private key alone decrypts the backup**: redundancy, **not two-person approval** (two-person control needs a different key-management design, not in V1). **Custody is OPEN (OPS-1) and not tied to any application role**, including the Manager role: a backup holds password hashes, the audit log and every clinical and financial record. Neither private key is ever on the VM; losing both makes off-site backups unreadable. Public keys are deployment configuration. |
| Upload | **Write-only** credentials (put object only); bucket versioning and **object lock** for 30 days; expiry by lifecycle rules set with separate admin credentials. A compromised VM can add backups but cannot read or delete them. |
| Verification (every run) | Exit code; size ≥ 80% of the previous dump and non-zero; `pg_restore --list`; SHA-256 recorded; uploaded object size/checksum compared |
| Manifest (every run) | Per-table row counts, latest audit id, ledger totals (Σ payments, Σ allocations, Σ earnings), stored beside the dump |
| Integrity checks (every run, same snapshot) | Read-only ledger queries: a distribution's parts not summing to its allocation; a payment whose allocations exceed it; a charge whose Collected exceeds Billed; an earning with no allocation; a pending attribution that also has an earning. Any hit fails the job and raises a **critical** alert. The "allocations equal the payment amount" check mirrors the V1 validation default and is relaxed if that default is. |
| Heartbeat | Success pings the monitor; **no ping within 26 hours = critical alert** |

**Retention (DEFAULT):** provider PITR window 14 days (or provider maximum if cheap); nightly off-site 30 days; monthly 12 months; yearly 3 years. The live database keeps all clinical and financial history forever (no physical deletes); backups exist for recovery and are **not** the legal archive. **OPEN (OPS-2):** legal retention periods for medical/financial records and whether any controlled-erasure workflow must reach backups; nothing assumed, no erasure workflow designed.

## 3.4 Restore and restore testing

| Drill | Frequency (DEFAULT) | Procedure |
|---|---|---|
| **Logical restore** | Monthly for the first 3 months, then quarterly, and before go-live | Restore the latest off-site dump into a throwaway database; compare the manifest (counts, ledger totals); run integrity queries and migration-history check; confirm G1–G7 triggers, grants, constraints; destroy |
| **Point-in-time restore** | Quarterly and before go-live | Restore the provider backup to a **new** instance at a chosen timestamp; verify as above; delete |

Drills use real patient data so they **never run in staging**; they run in a throwaway production-grade environment (recommended: short-lived instance/VM in the production account created and destroyed by script; fallback: encrypted operator workstation with secure deletion). A drill overdue by more than 35 days raises a warning. Each result is recorded in the runbook log. "Backup restore tested" is a go-live gate.

**Runbooks (to be written in the operations phase):** R1 bad data/migration (stop writes; restore to the recorded point into a new instance; verify; repoint the connection secret; keep the old instance read-only for forensics; use its audit log to list entries made after the restore point). R2 VM loss (provision, bootstrap, restore secrets, point DNS, deploy). R3 provider loss (new managed instance with required extensions; run `bootstrap-roles.sql` **before** restoring; decrypt and restore the latest dump as `clinic_migrator`; run the migrator (expected no-op); verify; deploy).

### Mandatory post-restore lockdown (security)

The audit log never holds passwords, hashes or reset tokens, so **a password change after the restore point cannot be reconstructed from it**; the restored Identity row holds the old hash and Password A would be valid again. Operator command **`post-restore-lockdown`** runs inside the `api` container before the restored system opens to users:

1. **Rotate every security stamp** (signs everyone out; invalidates outstanding reset tokens).
2. **Passwords.** *Default conservative action:* set `password_hash = NULL` and `password_reset_required = true` for **all staff accounts**; login is refused until a new reset completes. *Narrowed action, only with reliable evidence:* if the pre-restore instance's audit log is intact, password-change/reset **events** (never values) after the restore point identify the affected users and only they are forced to reset; if the audit log is unavailable or incomplete the default applies.
3. **Re-apply deactivations and role changes** after the restore point where the old instance's audit gives reliable evidence; anything unverifiable is unknown and the Manager reviews the staff list before reopening.
4. Re-run integrity queries and the Finance invariants.
5. Notifications: dedupe keys prevent duplicate rows; reminders sent after the restore point may be regenerated (minor, expected).

Delivery of the new reset tokens stays the **open account-recovery configuration**; no delivery workflow is assumed. Lost Manager access uses the existing operator recovery command. This is a security recovery mechanism, not a clinic business rule. Restore runs as `clinic_migrator` after `bootstrap-roles.sql` so ACLs and ownership restore correctly; after a restore the key table is empty and the application creates a fresh Data Protection key ring.

## 3.5 Monitoring: simple, affordable, low-maintenance

Few moving parts, no inbound exposure, and **no personal data in any alert, event or ping** (scrubbing rules apply).

| Need | Approach (DEFAULT, not a vendor decision) |
|---|---|
| External uptime | Hosted monitor checks `https://<domain>/health/live` every minute, plus TLS and domain expiry |
| Heartbeats / dead-man's switch | Healthchecks-style service; each check pings a URL and alerts when pings stop |
| Host checks | One small script every 5 minutes (cron/systemd timer) pings the heartbeat service |
| Exceptions | Sentry-compatible tracker for API, Worker and SPA behind the scrubber, request bodies disabled. Hosted tracking is a data-residency question (OPS-2); fallback: self-hosted GlitchTip on a separate small VM, or none, relying on logs plus log-derived alerts. |
| Database | Provider metrics and alert rules |
| Dashboards | None in V1; Hangfire dashboard stays behind the SSH tunnel |

| Signal | Source | Threshold | Severity |
|---|---|---|---|
| Server health | External `/health/live` | 2 consecutive failures | Critical |
| Database connectivity and schema | Host script calls internal `/health/ready` then pings | No ping for 5 min | Critical |
| API errors | Error tracker; Caddy log (status/route only) | > 5 exceptions in 10 min or a new type; > 5% 5xx with ≥ 20 requests in 5 min | Warning, then Critical |
| CPU | Provider VM metrics | > 85% for 15 min | Warning |
| Memory | Host script | < 15% available for 10 min; any OOM kill | Warning, Critical |
| Disk | Host script | > 80%, > 90% | Warning, Critical |
| Container health | Host script | Any restart outside a deploy window | Warning |
| Database usage | Provider metrics | Storage > 80%/90%; connections > 80%; CPU > 80% for 15 min | Warning, Critical |
| Background job failures | Worker job J7 | A recurring job without success within 3× its interval; any failed Hangfire job | Critical |
| Notification failures | J7 | Dispatcher lag > 10 min; any row at 5 failed attempts | Warning, Critical |
| Backup failures | Backup heartbeat; provider event | No success within 26 h; verification/integrity failure; size anomaly; drill overdue | Critical, Critical, Warning, Warning |
| Authentication failures | Caddy log counts (route/status only) | > 30 failed logins in 10 min; > 20 rate-limit responses in 10 min; > 20 forbidden responses on role routes in 10 min | Warning |
| Certificates and domain | External monitor | TLS < 14 days (warning), < 5 days (critical); domain < 30 days | Warning, Critical |

**Job J7 "operations health"** (Notifications module, Worker, every 5 minutes, only the Worker's existing grants): job freshness/heartbeat; dispatcher lag; exhausted retries; Hangfire failed-job count; count of follow-ups assigned to an inactive Sales employee (information only, weekly digest, because Q-EMP-1 is OPEN). **Inactive-owner lookup:** contract `Staff.Contracts.IActiveSalesEmployeeDirectory.IsActive(salesEmployeeId)` (plus batch form) backed by the minimal view `staff.v_sales_employee_activity (sales_employee_id, is_active)` (owned by `clinic_migrator`, not `security_invoker`). The Worker has SELECT on that view only: no access to `staff.sales_employees` or `staff.doctors`, so salary, commission, percentages and profile details are unreachable. Notifications cannot depend on Staff, so the count is produced by **Crm** (which may depend on Staff) through a Notifications-defined probe port. The same directory answers "nothing is sent to an inactive owner" in J3. J7 pings success, or failure with a fixed message and counts; never names, ids or text.

**Alert routing (DEFAULT):** Critical = immediate SMS/phone push **and** email to the operator, escalating to a named backup contact if unacknowledged for 30 minutes; Warning = email plus weekly digest; Info = weekly digest. One freelancer is a single point of failure for alerts; a named backup contact and a handover runbook are recommended (operational, not a design change). Weekly 10-minute review: open warnings, last backup log, next drill date, OS/image patch status, certificate dates.

## 3.6 Logging retention

Docker log rotation bounded by size and file count (e.g. 5 × 100 MB per container), so the disk cannot fill; typically weeks, **bounded by size, not days** (30 days is a ceiling). Logs stay on the host and are not shipped off-host in V1; the error tracker keeps scrubbed exceptions; the audit log in the database is the durable record.

## 3.7 Verification and tests (Step 14)

| # | Test |
|---|---|
| 1 | Backup job produces an encrypted dump, passes verification and uploads; manifest matches live counts |
| 2 | With the upload credentials, get, list-and-overwrite and delete are denied; object lock prevents early deletion |
| 3 | `clinic_backup` can read everything it was granted and cannot write anything; its secret is not in api or worker |
| 4 | An injected ledger violation (test database, constraint disabled) fails the nightly integrity check and raises a critical alert |
| 5 | A missed backup heartbeat raises a critical alert (simulated in staging) |
| 6 | **Logical restore drill passes** (manifest counts, ledger totals, integrity queries, migration history, triggers, grants) |
| 7 | **Point-in-time restore drill passes** at a chosen timestamp |
| 8 | After a restore, all stamps rotate and a user deactivated after the restore point cannot sign in |
| 9 | Each signal in 3.5 is simulated in staging and reaches the right channel |
| 10 | Canary scrub: error-tracker events and every ping/alert body contain no contact or clinical canary |
| 11 | Caddy access logs contain no query strings, bodies or identifiers beyond route templates |
| 12 | The backup decrypts independently with **each** configured recovery private key; custody identities are deployment configuration and no test or code refers to a role |
| 13 | **Password regression:** a user has Password A, changes to Password B, the database is restored to before the change. Without lockdown A would work; after `post-restore-lockdown` login with A fails, the account is in reset-required state and normal use is impossible until a new reset completes |
| 14 | Lockdown rotates all stamps; pre-restore sessions and outstanding reset tokens fail; a user deactivated after the restore point is handled by step 3 or Manager review |
| 15 | The narrowed action covers only users with a password event in the old audit; a missing/incomplete audit falls back to all staff |
| 16 | A dump produced by the restricted `clinic_backup` role **restores completely** (manifest counts, ledger totals, triggers, grants, constraints) |
| 17 | `clinic_backup` SELECT on `infra.data_protection_keys` fails; no write privilege anywhere; not a member of `pg_read_all_data` |
| 18 | The dump contains **no Data Protection key rows**; after restore the application creates a fresh key ring and old cookies are rejected |
| 19 | The CI grant test fails when a new table lacks a backup-grant decision |
| 20 | J7 reports inactive-owner counts using only the staff view; the Worker cannot SELECT salary, commission or any column of `staff.sales_employees` / `staff.doctors` |
| 21 | J3 sends nothing for a follow-up whose owner is inactive; the dispatcher and J7 use the same directory |
| 22 *(v1.1)* | `clinic_backup` can SELECT `staff.assistants` and `payroll.assistant_payrolls`; a dump restores both with their exclusion and CHECK constraints; `clinic_worker` cannot SELECT either |

## 3.8 Points to be aware of (none blocking)

1. Legal and residency check for the off-site region/provider, hosted error tracking and retention periods (OPS-2).
2. **Backup-key custody** (who holds the two recovery private keys and where copies are stored) must be decided before go-live; custody is independent of application roles (OPS-1).
3. RPO/RTO acceptance, retention numbers, on-call and backup contacts, monitoring vendors are defaults that may be vetoed.
4. Provider PITR granularity and cross-region backup support are verified at provider selection.
5. Open business items are unchanged (Q-FIN-1, Q-FIN-2, Q-FIN-3, redefined Q-FIN-6, Q-FIN-9, Q-ORT-2, Q-CLN-1, Q-EMP-1, Q-EXP-1, …); no alert or check encodes a decision on any of them.

**Serious-issue check:** none. A restore can reactivate a deactivated employee or an old password, so the lockdown is mandatory; restore drills use real data, so they run in a throwaway production-grade environment and never in staging. No Inventory and no X-ray appear anywhere.
