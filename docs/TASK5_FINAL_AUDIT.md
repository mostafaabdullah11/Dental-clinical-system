# Task 5 Audit — Integration, Security, Deployment, Handover

**Baseline:** Design v1.1 (frozen). No Task 1–4 business behavior changed.
**Branch:** `main` (uncommitted; nothing committed, pushed or tagged).
**Date:** 2026-10-27

**Verdict: TASK 5 INCOMPLETE.** The blockers are in §4. Everything that can run on this machine passed.

This file did not exist at the start of this session; it was created here. It records only what was executed in this session.

---

## 1. Gate results (executed on this machine)

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build Clinic.sln` | **PASS**: 0 errors, 0 warnings. A truncated `tests/Clinic.E2ETests/StackFixture.cs` had broken the build; it is now complete. |
| Architecture tests | `Clinic.ArchitectureTests.exe` (test assembly run directly) | **PASS**: 36 / 36 |
| Integration tests (real PostgreSQL 18) | `Clinic.IntegrationTests.exe` (test assembly run directly) | **PASS**: 220 / 220, 0 failed (final regression re-run in the second session: 297 s). Includes the Task 5 suites: AuthorizationMatrix, AuditLogDataProtection, InputSecurity, MigrationSafety, OpenApiContract, DeploymentConfigLint, BackupRestoreDrill. |
| `dotnet test --solution Clinic.sln` | as specified | **WORKS IN THE OWNER SHELL** (discovered 480 tests). It does not work inside the assistant's tool sandbox ("Zero tests ran"). First owner result: 36 passed, 444 failed, all fixture-lifecycle / missing-prerequisite errors (§3.1). Corrected result: **pending the owner's re-run** (see §3.1). |
| Frontend lint | `npm.cmd run lint` | **PASS** (61 files, 5 rules) |
| Frontend unit tests | `npm.cmd run test` | **PASS**: 3 / 3 |
| Frontend typecheck | `npm.cmd run typecheck` | **PASS** |
| Frontend build | `npm.cmd run build` | **PASS**: 436.34 kB JS (121.67 kB gzip) |
| NuGet vulnerability scan | `dotnet list Clinic.sln package --vulnerable --include-transitive` | **PASS**: none |
| npm vulnerability scan | `npm.cmd audit --audit-level=high` | **PASS**: 0 vulnerabilities |
| Shell syntax | `bash -n` on all 10 scripts under `deploy/` | **PASS** |
| Secret pattern scan (manual regex, not gitleaks) | key blocks, AKIA, age secret keys, tokens, quoted password literals over tracked and untracked files | **PASS**: one hit, a test canary string in `AuditLogDataProtectionTests.cs` (intentional). This does not replace gitleaks. |

## 2. Backup and restore

**Snapshot communication fix.** `deploy/backup/backup.sh` already used a Bash `coproc` over anonymous pipes. No FIFO (`mkfifo`) remains. I verified that it works with the native Windows `psql` 18 under Git Bash/MSYS: `pg_export_snapshot()` returns the id and the `dump`, `integrity`, and `manifest` steps all use that snapshot. After a failure (size anomaly, integrity exit 3) no `psql` process and no database remains.

**Backup test (`deploy/backup/test-backup.sh`).** It passed, with the encryption stubbed. A throwaway `age` / `age-keygen` shim, which copies files and does not encrypt, was placed on `PATH` because the real `age` binary is not installed. The shim is in the session scratchpad, not in the repository. Verified:
- backup as `clinic_backup` and the encrypted-artefact naming
- the integrity file reporting 2 recipients
- archive listing, with the key table absent
- restore drill into a throwaway database: manifest identical (81 items), 11 integrity checks all 0, 39 triggers, 227 grants, key table recreated empty, `clinic_app` cannot delete ledger rows
- the drill refuses a non-throwaway target
- a size drop below 80% fails
- an injected ledger violation exits 3 with `g8_payment_conservation=1`

**Not verified:** real `age` encryption to two recipients and decryption with each private key alone.

## 3. Findings

1. **Owner-shell `dotnet test --solution Clinic.sln` (480 tests): 36 passed, 444 failed. Root causes, none an application defect.**
   - *Runner:* the runner works in the owner's terminal. The earlier "Zero tests ran" / handshake failure is specific to the assistant's tool sandbox (reproduced there with a standalone one-test project too), so the earlier claim that `dotnet test` is unusable on this machine is withdrawn.
   - *Masking defect (fixed):* 440 integration errors were `Assembly fixture 'ApiFixture' threw in DisposeAsync` / `NullReferenceException` at `ApiFixture.cs:38`. `InitializeAsync` threw first (before `Factory` was assigned), then `DisposeAsync` dereferenced the null `Factory` and replaced the real exception. Fixed: `ApiFixture` now holds nullable backing fields and disposes only what was created; `TestDatabase.CreateAsync` / `CreateEmptyAsync` drop a half-built database on failure (best effort, never replacing the original exception). Nothing was skipped or weakened.
   - *Original initialization failure:* with the masking removed, the first exception is `CLINIC_TEST_PG_ADMIN is not set. Point it at a local development PostgreSQL server (see .env.example)`. This was reproduced here by unsetting the variable. The owner's shell must export it (for example `Host=localhost;Port=5432;Username=postgres;Database=postgres`, with the password in `PGPASSWORD` / pgpass, as in `.env.example`). I cannot see the owner's environment, so I cannot prove this is the variable that was missing there. If the owner's variable is set, the now-unmasked exception will name the real cause.
   - *E2E (4 errors):* proven by output, not assumed. The first exception is `CLINIC_TEST_PG_ADMIN is required`; with that set, it is `CLINIC_E2E_CADDY is required`. The E2E fixture needs the real Caddy binary. Two repository defects were fixed: (a) the PR gate (`ci.yml` backend job) runs `dotnet test --solution`, which includes the E2E project but never provided Caddy, so it would always fail; E2E tests now report **SKIPPED** with an explicit reason when prerequisites are absent, and `CLINIC_E2E_REQUIRED=1` turns a missing prerequisite into a **failure**; (b) the nightly E2E job never installed Caddy; it now downloads a pinned Caddy 2.10.2 release with checksum verification and sets `CLINIC_E2E_REQUIRED=1`. The nightly step has not been executed here, and the pinned version and checksum file name are unverified until its first run.
2. **E2E tests were written but could not run.** `tests/Clinic.E2ETests` had a truncated fixture and no tests. I completed the production-topology fixture and added `JourneyTests.cs`: Arabic RTL login, wrong-password rejection, session cookie flags, anonymous API rejection, and the XSS payload rule. Both compile; neither has been executed, so selectors and expectations are unproven. The fixture needs the real Caddy binary (`CLINIC_E2E_CADDY`), which is absent.
3. `docs/TASK5_FINAL_AUDIT.md` was not present at the start; it was created here.

## 4. Status by category

Tool availability was re-checked in a second session: `caddy`, `age`, `age-keygen`, `gitleaks`, `docker`, `trivy` and `hadolint` are all still absent from `PATH` (Git Bash and PowerShell). Downloading binaries is blocked by this session's permission policy; no workaround was attempted.

### PASS — executed successfully
Build; architecture 36 / 36; integration 220 / 220 (assemblies run directly); frontend lint, tests (3 / 3), typecheck, build; NuGet and npm vulnerability scans; `bash -n` on all deploy scripts; backup / restore-drill logic with the non-encrypting shim (§2); manual regex secret scan.

### BLOCKED — local engineering / tool blocker (implementable once the tool is present)
| Item | Needs |
|---|---|
| Playwright E2E (`tests/Clinic.E2ETests`, written, compiled, never executed) | Caddy v2 binary (`CLINIC_E2E_CADDY`) |
| Process-level production-like smoke (`deploy/local/run-process-smoke.sh`) | Caddy v2 binary (`CADDY_BIN`) |
| Real `age` backup test (`deploy/backup/test-backup.sh`, two-recipient encrypt, each key decrypts alone) | `age`, `age-keygen` |
| Repository secret scan | `gitleaks` |
| `dotnet test --solution Clinic.sln` corrected result | Owner re-run with `CLINIC_TEST_PG_ADMIN` exported (§3.1); the assistant sandbox cannot run `dotnet test` |

### READY — EXTERNAL INPUT REQUIRED
| Item | Needs |
|---|---|
| Production-like Compose smoke (`deploy/local/run-compose-smoke.sh`, `docker compose config`) | Docker |
| Image scan, Dockerfile lint | Docker, Trivy, hadolint |
| Staging deploy smoke, OWASP ZAP baseline | Staging environment and URL |
| Restore drill on a separate VM; production go-live | Provider, domain, backup-key custody (OPS-1) |
