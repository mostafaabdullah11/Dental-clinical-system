# Task 2 / Step 2.1 — Foundation + Auth + User / Staff Foundation

**Status:** DRAFT COMPLETE for this step. Task 2 is **not** complete.
**Baseline:** Design Baseline 1.1 (frozen). Nothing has been committed or pushed.

## 1. Implemented capabilities

| Area | What exists |
|---|---|
| Solution | Modular monolith on .NET 10 with one `ClinicDbContext` and one schema per module. Modules: `Identity`, `Audit`, `Configuration` (L1) and `Staff` (L2). Two hosts share the modules: `Clinic.Api` and `Clinic.Worker`. Frontend in `src/web`: React 19 + TypeScript + Vite. |
| Database | Six migrations, one per module plus a cross-module one (DB §2 naming): `Identity_Initial`, `Audit_Initial`, `Configuration_Initial`, `Staff_Initial`, `Infra_Initial`, `Xref_InitialForeignKeys`. Migrations are applied as `clinic_migrator`, which owns every object. The API runs as `clinic_app`; least-privilege grants are in the migrations (SEC §15). |
| Roles | Exactly `MANAGER`, `DOCTOR` and `SALES_RECEPTION`, enforced by a CHECK. `user_roles` is unique on `user_id`, so each account has one role. There is no Assistant role and no Patient role. |
| Bootstrap | `create-initial-manager` and `set-manager-password` are CLI commands only (`dotnet Clinic.Api.dll <command> <user>`). The password is read without echo and never logged or audited. Each run is audited with the `SYSTEM:OPERATOR` marker. No default account exists. |
| Authentication | Login uses PBKDF2-HMAC-SHA512 with 210k iterations and a dummy hash check for unknown usernames. Unknown, wrong-password, inactive and locked accounts all get the same response. Lockout is 5 failures for 15 minutes; the auth rate limit is 10 requests per minute per IP. The session cookie is `__Host-` with HttpOnly, Secure and SameSite=Strict. Every request re-checks the security stamp and `is_active`. Sessions expire after 20 minutes idle and 10 hours absolute. Logout sends `Clear-Site-Data`. Other endpoints: CSRF token fetch with Origin / Sec-Fetch-Site checks, change own password, and `/api/me` (role, menu permissions, clinic timezone, `ar`/`rtl`). |
| Manager accounts | Create an account (username, initial password, exactly one role, matching staff link). Set a NEW password (rotates the stamp and clears lockout). Change role (refused when the staff row does not fit). Force sign-out. List users. No route ever returns a password or hash, and no self-service reset exists. |
| Staff | **Doctor** and **Sales** records are created together with their account in one transaction. **Sales commission is required**, and creation writes revision 1 of the effective-dated history. The Manager can also change the Doctor percentage, append commission revisions (future collections only; no back-dating input is accepted), view commission history and change base salary. **Assistant** records have no `user_id`, account or role; edits require `If-Match`. Doctor, Sales and Assistant records can be deactivated and reactivated. Nothing is ever deleted, and no DELETE route or grant exists. |
| Deactivation seam | Sales deactivation publishes `SalesEmployeeDeactivated` inside the deactivation transaction so the CRM slice can redistribute in the same transaction. No redistribution logic was invented. |
| Authorization | Four layers: 401 → permission key 403 → scoped 404 → audience DTO. The fallback policy denies any endpoint without metadata. A startup check refuses to start if a route has no authorization kind or an anonymous route is not on the allow-list. Integration tests verify that every route key is mapped to a role and that no contact-bearing key is mapped to DOCTOR. |
| Audit | Atomic with the business change and fails closed. Level A comes from a SaveChanges interceptor, level A+ from explicit events. Password, hash and stamp properties are excluded. The audit log is insert-only for `clinic_app`. The Manager-only audit read is itself audited. |
| Platform | Errors are RFC 7807 with `code`, `traceId` and field codes, and never echo input. Unknown JSON members → 400. Money and percentages travel as two-decimal strings, with no floating point anywhere. ETag / If-Match returns 428 or 412. Security headers and `no-store` are set on every response. Clinic settings default the timezone to `Africa/Cairo`, accept only valid IANA ids, require If-Match and are audited. `/health/live` and `/health/ready` (which includes a pending-migration check) have no body. |
| Frontend | Arabic-only RTL (`<html lang="ar" dir="rtl">`), with no language switch and no browser storage. API codes map to Arabic messages. Pages: Login, shell, Users, Doctors, Sales / Reception, Assistants, Settings and Change password. |
| Dev environment | `deploy/docker-compose.dev.yml` runs PostgreSQL 18. `deploy/db/bootstrap-roles.sql` and `create-database.sql` are the same scripts the tests use. `.env.example` contains no secrets. `.editorconfig`, `.gitignore` and `global.json` are included. |

## 2. FR IDs implemented

- **Complete in this step:** FR-AUTH-001 … FR-AUTH-010; FR-STAFF-001, -002, -003, -004, -005, -006, -009, -010, -011; FR-OPS-002, -003, -006, -009; FR-AUDIT-001, FR-AUDIT-004.
- **Foundation only** (later slices extend them):
  - **FR-OPS-001:** shell and contract.
  - **FR-OPS-004:** money transport and numeric columns.
  - **FR-OPS-005:** ETag done; the payment Idempotency-Key comes with Finance.
  - **FR-SEC-001, -005, -006, -009, -010, -011, -012:** the mechanisms exist for the routes built so far.
  - **FR-AUDIT-002, -003, -005, -006, -007, -008:** apply to the tables built so far.
  - **FR-STAFF-009:** the CRM redistribution is the CRM step.

## 3. AC IDs tested

All tests pass: **85 / 85**, of which 77 are integration tests against real PostgreSQL and 8 are architecture tests.

- **AUTH:** AC-AUTH-001, -002, -003, -004, -005, -006, -007, -008, -009, -010.
- **STAFF:** AC-STAFF-001, -002, -003, -004, -005, -006, -009, -010, -011, plus the role ⇔ staff-row invariant.
- **OPS:**
  - AC-OPS-001: RTL shell, no language switch, Arabic error messages, `/api/me` locale.
  - AC-OPS-002: no branch.
  - AC-OPS-003, AC-OPS-005 (ETag part), AC-OPS-006, AC-OPS-009.
  - AC-OPS-004: numeric columns, no float, two-decimal strings.
- **Security / audit:**
  - AC-SEC-001: 401 / 403 / 404, route metadata, keys mapped.
  - AC-SEC-009 (4 of the 6 anonymous routes exist; the public feedback pair arrives with Task 3).
  - AC-SEC-010 (Assistant routes), AC-SEC-012 (headers), AC-AUDIT-001, AC-AUDIT-004, AC-AUDIT-005 (no DELETE).
- **Negative:**
  - AC-NEG-001, -002, -003: no inventory, X-ray or branch tables / types.
  - AC-NEG-004: no Assistant account, role or audience.
  - AC-NEG-005: no patient audience routes.
  - AC-NEG-018: no reset route or column.
  - AC-NEG-029: no locale switch.

**Database gate** (all checked on a database created from zero):

- All 6 migrations apply.
- Only the 5 foundation schemas exist, and the exact 13 tables.
- Required CHECK, UNIQUE and FK constraints exist; cross-schema FKs match `CrossModuleReferences.cs` with NO ACTION.
- Every object is owned by `clinic_migrator`.
- `clinic_app` has no DELETE, no UPDATE on frozen columns and no UPDATE on the audit log.
- `clinic_worker` sees only its two views and settings.
- `clinic_backup` cannot read the Data Protection keys.
- No branch, inventory, X-ray, patient or payroll table exists.

**Real-process smoke test:**

- `dotnet ef database update` succeeded against an empty database.
- The CLI bootstrap ran, and a second run was refused with exit code 3.
- Over HTTPS: login, CSRF, `/api/me`, create a Doctor with Arabic data, settings and logout all worked.
- The Vite proxy worked, and the Worker started as `clinic_worker`.

## 4. Build / test commands

```
dotnet build Clinic.sln                                   # 0 warnings (warnings are errors)
dotnet test --solution Clinic.sln                         # needs CLINIC_TEST_PG_ADMIN (see .env.example)
cd src/web && npm ci && npm run typecheck && npm run build
```

## 5. Deviations and technical decisions (no business change)

1. **No Docker on this machine.** The integration harness uses a local PostgreSQL through `CLINIC_TEST_PG_ADMIN` instead of Testcontainers. Each run creates `clinic_test_*`, runs the real bootstrap scripts, migrates as `clinic_migrator`, runs as `clinic_app`, then drops the database. The run also creates the four `clinic_*` roles on the server if they are missing. CI should add the Testcontainers variant (DEPLOYMENT §2.2).
2. **Dev API uses HTTPS** (`https://localhost:5081`, ASP.NET dev certificate) because the `__Host-` cookies are Secure-only. Vite proxies `/api` to it.
3. **Technical additions:**
   - `GET /api/manager/users`, so the Manager UI can list accounts. It is Manager-only and returns no password data.
   - Technical error codes such as `validation_failed`, `forbidden`, `auth.invalid_credentials` and `csrf_invalid`. No business codes were invented.
   - `audit_log.actor_role` and `audit_log.reason` columns, for the role and the reason on A+ entries.
   - A `value_type` CHECK on settings.
4. **Account creation:** a Doctor or Sales account is always created together with its staff row. This happens through `POST /api/manager/users` with the matching `doctor` / `salesEmployee` link, or through the Staff routes. It keeps the role ⇔ staff invariant true at every commit.
5. **Production config:** the API refuses to start outside Development / Testing without a Data Protection certificate (`DataProtection:CertificatePath`).

## 6. Observations for review (not blockers; no behavior chosen)

- **Role change:** under the frozen rules (a staff row must fit the role, and staff rows are never deleted), no change between different roles can currently pass. The endpoint implements the rule literally: a mismatch → 422 and the same role → no-op.
- **Reactivation and commission (for Task 4):** reactivation clears `deactivated_at`. FR-PAYROLL-005 together with "no retroactive commission" means Sales commission must exclude collections from every inactive period, so Task 4 needs the inactivity history. The audit log has it today. This is a technical design point, not a business change.
- **Change own password:** the FRS does not say whether other sessions should end, so the security stamp is not rotated.

## 7. Remaining Task 2 work

- **Patients slice:** registration (Walk-In), identity / contact split, search by audience, Doctor and Sales views, chief complaint, reassignment, timelines.
- **Medical History intake slice:** tri-state intake, audit by field name.
- **CRM slice:** Leads with 8 statuses, Follow-Ups with 6 sources and 4 statuses, conversion, Manager assignment, and the **Sales-deactivation redistribution handler** (subscribes to `SalesEmployeeDeactivated`).
- **Cross-cutting:**
  - OpenAPI document per audience, generated TypeScript clients, OpenAPI property scans.
  - Log scrubbing (Serilog masking of phones and emails) before contact data exists.
  - Privacy canary harness for Doctor and Sales DTOs.
  - Contact-bearing permission seeds for SALES_RECEPTION / MANAGER (no DOCTOR).
- **Deferred to Task 4 by the matrix:** FR-STAFF-007 and -008 (Assistant salary and overtime-rate routes; the columns exist), the T6 Doctor % + Lab % check (needs the lab-percentage table).

## 8. Files changed

New only; no existing file was modified:

- **Root:** `Clinic.sln`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `dotnet-tools.json`, `.editorconfig`, `.gitignore`, `.env.example`
- **`src/Clinic.SharedKernel`:** errors, security metadata, audit contracts, ETag, money JSON, validation filter, domain events, clock.
- **`src/Modules/Clinic.Modules.{Identity,Audit,Configuration,Staff}`:** domain, model, contracts, features (endpoints), operator commands.
- **`src/Clinic.Persistence`:** DbContext, cross-module FKs, design-time factory, 6 migrations + snapshot, `Migrations/Sql/*.sql`.
- **`src/Clinic.Api`:** composition (security pipeline, problem+json, startup route check), `/api/me`, account creation, health, operator CLI, settings files.
- **`src/Clinic.Worker`:** host skeleton (no jobs).
- **`src/web`:** Vite / React / TS app and `package-lock.json`.
- **`tests/Clinic.IntegrationTests`, `tests/Clinic.ArchitectureTests`**
- **`deploy/`:** dev Compose and the database bootstrap scripts.
- **`docs/TASK2_STEP2_1_REPORT.md`** (this file).

## 9. Blockers

None.
