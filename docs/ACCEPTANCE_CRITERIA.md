# ACCEPTANCE_CRITERIA.md

**Design Baseline:** 1.1 — OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION
**Specification:** docs/FRS.md — COMPLETE
**Traceability:** docs/TRACEABILITY_MATRIX.md — COMPLETE
**Document status:** TASK 1 / STEP 1.3 — ACCEPTANCE CRITERIA COMPLETE

---

## 1. Purpose

This document defines executable acceptance expectations for every functional requirement of docs/FRS.md and every negative / forbidden / privacy requirement allocated in docs/TRACEABILITY_MATRIX.md, before implementation begins. Each criterion states **what must be true** for the requirement to be accepted; it adds no business behavior. Every AC ID was allocated in the Traceability Matrix: primary criteria map one-to-one (`FR-XXX-nnn` → `AC-XXX-nnn`), negative criteria are `AC-NEG-001` … `AC-NEG-043`. Implementation Tasks 2–4 must make the listed criteria pass; Task 5 runs them end-to-end (Section 5).

## 2. Acceptance Format and Conventions

Every criterion uses this compact structure:

- **Traces** — the FR (primary) or the FRS §30 item / §5 privacy rule (negative).
- **Type** — Positive / Validation / Permission / Privacy / Financial / Negative / Audit / Security / DB / Operations (one or more).
- **Task** — implementation Task(s) from the Traceability Matrix.
- **Given / When / Then** — usually one scenario; lettered sub-scenarios (a), (b), … only where needed to prove the requirement. A Then-line labelled (x) answers When-line (x).
- **Linked frozen tests** — frozen test numbers cited by the FR (SEC #, API A11 #, B9 #); Step 1.3 criteria include them and the implementation test suite must cover them.
- **Test level** — API / Integration / DB / UI / Security / Operations / E2E.

Conventions used throughout:

- Money values are EGP with two decimals; "1,000.00" = the API string `"1000.00"`.
- Stable error codes are quoted exactly as defined in the FRS; where the FRS gives only an HTTP status, only the status is required.
- "Audited" means an audit row with actor, role where required, timestamp, entity / action and reason where the FR requires one — never passwords, hashes, tokens or Medical History values.
- "403 / 404" follow the four-layer rule: missing permission → 403, out-of-scope resource → 404 (FR-SEC-001).
- Clinic-local dates use the configured clinic timezone (FR-OPS-003).
- Deferred items remain exactly **Q-ORT-3b** (V1 rejection verified by AC-ORT-010 / AC-NEG-021) and **OPS-1** (outside AC-OPS-007's scope until go-live).

## 3. Primary Acceptance Criteria

## 3.1 OPS

### AC-OPS-001 — Arabic-only, right-to-left interface

**Traces:** FR-OPS-001
**Type:** Positive / Negative
**Task:** 2, 5

**Given**
- An authenticated user of any role opens the SPA.

**When**
- Any screen, validation message, report, export or prescription print layout is rendered, and `GET /api/me` is called.

**Then**
- The root document has `dir="rtl"` and `lang="ar"` from the first screen.
- `GET /api/me` returns locale `ar` / `rtl`.
- API error bodies carry stable `code`s and no UI strings; the SPA maps each `code` to an Arabic message.
- Report, export, notification and prescription-print application text is Arabic.
- No language switch or bilingual mode exists (see AC-NEG-029).

**Linked frozen tests:** API A11 #37
**Test level:** UI / API

### AC-OPS-002 — Single clinic, single branch

**Traces:** FR-OPS-002
**Type:** Negative
**Task:** 2, 5

**Given**
- The migrated database and the generated OpenAPI documents.

**When**
- The schema and route table are inspected.

**Then**
- No table in any schema has a `branch_id` column (DB test (12)).
- No branch table, branch route, branch selector or cross-branch report exists (see AC-NEG-003).

**Test level:** DB / Security

### AC-OPS-003 — Clinic timezone and clinic-local dates

**Traces:** FR-OPS-003
**Type:** Positive / Validation / Permission
**Task:** 2

**Given**
- `configuration.settings.clinic_timezone` holds the default `Africa/Cairo`.

**When**
- (a) The Manager updates the timezone to a valid IANA value via `PUT /api/manager/settings`.
- (b) The Manager submits an invalid timezone value.
- (c) A Doctor or Sales user calls `PUT /api/manager/settings`.
- (d) A date-based rule (today, tomorrow, due date, payroll month, report range, attendance date, arrival day) is evaluated near UTC midnight.

**Then**
- (a) The value is stored, `GET /api/me` returns it, and the change is audited.
- (b) Rejected with a validation error; the stored value is unchanged.
- (c) 403.
- (d) The clinic-local calendar date is used; inclusive clinic-local date ranges are converted to half-open UTC ranges.
- With a missing / invalid stored timezone, jobs fail loudly and do not guess a timezone.

**Test level:** API / Integration

### AC-OPS-004 — Money representation

**Traces:** FR-OPS-004
**Type:** Validation / Financial
**Task:** 2, 4

**Given**
- Any API that accepts or returns money or a percentage.

**When**
- Amounts are submitted, stored, computed and returned.

**Then**
- Money is returned as a string with exactly two decimals; all money columns are `numeric(12,2)`, percentages `numeric(5,2)` within 0..100.
- An amount with more than two decimals is rejected; a non-positive amount is rejected where the operation requires a positive amount.
- Formula results round half-up to two decimals.
- No floating-point type is used for money in schema or domain code (architecture scan).
- Discount input accepts only a fixed EGP amount (see AC-NEG-020).

**Linked frozen tests:** API A11 #32
**Test level:** API / DB

### AC-OPS-005 — Optimistic concurrency and idempotency

**Traces:** FR-OPS-005
**Type:** Validation / Financial
**Task:** 2

**Given**
- An editable resource returned with an ETag, and the payment, credit-application and correction routes.

**When**
- (a) A PUT / PATCH / state-changing POST is sent without `If-Match`.
- (b) Two writes are sent with the same ETag.
- (c) A payment is sent without `Idempotency-Key`.
- (d) A payment is resent with the same key and same payload.
- (e) A payment is resent with the same key and a different payload.
- (f) A state transition is requested on a resource already in the target state.

**Then**
- (a) 428.
- (b) Exactly one succeeds; the other receives 412.
- (c) 400.
- (d) The original result is returned; exactly one `payments` row exists and no allocation / credit / distribution row is duplicated.
- (e) 409 `payment.idempotency_mismatch`; nothing written.
- (f) Succeeds idempotently with no duplicate side effect.

**Test level:** API / Integration

### AC-OPS-006 — Error responses with stable codes

**Traces:** FR-OPS-006
**Type:** Validation / Privacy
**Task:** 2

**Given**
- Requests that fail validation, authorization, concurrency or a business rule, and a request with an unknown member.

**When**
- Each request is sent.

**Then**
- Every error is RFC 7807 `application/problem+json` with `type`, `title`, `status`, stable `code` and `traceId`; validation errors list field names and codes.
- Status codes are drawn from 400/401/403/404/409/412/428/422/429/500.
- An unknown request member returns 400.
- No error body echoes input values, SQL, stack traces, personal or medical data (canary values injected in the request are absent from the response).

**Test level:** API / Security

### AC-OPS-007 — Encrypted backups and recovery-key custody

**Traces:** FR-OPS-007
**Type:** Positive / Security
**Task:** 5

**Given**
- A production-like deployment with two `age` recovery recipients configured.

**When**
- A scheduled backup runs and the result is restored in a test environment.

**Then**
- The off-site backup is encrypted client-side to both recipients, and either private key alone decrypts it.
- No recovery private key exists on the production VM (file-system check).
- The `clinic_backup` role cannot read `infra.data_protection_keys`; the dump excludes key-table data.
- Holding a recovery key corresponds to no application permission.
- RPO 5 min / RTO 4 h are demonstrated for the realistic failure scenarios.
- The exact physical storage locations of the private keys remain **OPS-1 — DEFERRED** to go-live; this criterion does not verify them.

**Test level:** Operations

### AC-OPS-008 — Post-restore account lockdown

**Traces:** FR-OPS-008
**Type:** Security
**Task:** 5

**Given**
- A database restored to an earlier point in time.

**When**
- The operator runs `post-restore-lockdown`, then `set-manager-password`.

**Then**
- Every security stamp is rotated and every non-Manager account is locked; no pre-restore session or password works for a non-Manager account.
- A locked employee can log in only after the Manager sets a new password through the normal Set Password action (lockout cleared).
- Operator commands are audited with a system marker and are not reachable over HTTP.
- No reset token or delivery channel is created.

**Test level:** Operations / Security

### AC-OPS-009 — Health endpoints and controlled migrations

**Traces:** FR-OPS-009
**Type:** Positive / Security
**Task:** 2, 5

**Given**
- A deployed stack with the migrator container and the API container.

**When**
- (a) `GET /health/live` and `GET /health/ready` are called with the schema up to date.
- (b) A required migration is not yet applied.
- (c) The API container starts.

**Then**
- (a) Both return healthy with no detail in the body.
- (b) `/health/ready` reports not-ready.
- (c) The application applies no migration itself; only role `clinic_migrator` can apply migrations; no destructive migration runs at startup.
- `/health/ready` is not routed publicly; health responses, alerts and pings contain no personal data.

**Test level:** Operations

## 3.2 AUTH

### AC-AUTH-001 — Exactly three roles; Assistant has no account

**Traces:** FR-AUTH-001
**Type:** Negative / Permission
**Task:** 2

**Given**
- A freshly migrated and seeded database.

**When**
- (a) `identity.roles` is read.
- (b) A second `user_roles` row is inserted for one user.
- (c) The Manager creates an Assistant staff record.
- (d) The permission seed is inspected.

**Then**
- (a) Exactly `MANAGER`, `DOCTOR`, `SALES_RECEPTION` exist; no `ASSISTANT` role.
- (b) Fails on the unique `user_id` constraint.
- (c) No `identity.users` or `identity.user_roles` row is created; `staff.assistants` has no `user_id` column.
- A DOCTOR account links to a `staff.doctors` row and no Sales row, SALES_RECEPTION the reverse, MANAGER neither; a role/staff mismatch is refused.
- (d) No contact-bearing permission key is mapped to DOCTOR (migration test and startup check).

**Linked frozen tests:** SEC #26, #42, #46
**Test level:** DB / Security

### AC-AUTH-002 — Manager creates user accounts

**Traces:** FR-AUTH-002
**Type:** Positive / Validation / Permission
**Task:** 2

**Given**
- A Manager holding `Identity.Users.Manage`.

**When**
- (a) The Manager creates a Doctor account with username, a 12+ character password, role `DOCTOR` and a Doctor staff link.
- (b) The username duplicates an existing normalized username.
- (c) The password has fewer than 12 characters or is on the deny-list.
- (d) The staff link does not match the role.
- (e) A Doctor or Sales user calls `POST /api/manager/users`.

**Then**
- (a) The account is active and can log in; only a one-way salted hash is stored; no response contains the password or hash; the creation is audited (actor, target, time) with no password material.
- (b), (c), (d) Rejected; nothing written.
- (e) 403.
- No email / mobile identity, invitation or activation link is requested or produced.

**Test level:** API / Security

### AC-AUTH-003 — Manager sets a NEW password

**Traces:** FR-AUTH-003
**Type:** Positive / Security / Permission
**Task:** 2

**Given**
- An employee with an active session and a locked-out account.

**When**
- The Manager calls `POST /api/manager/users/{id}/password` with a policy-compliant new password.

**Then**
- The new password works; the old password fails.
- The employee's security stamp is rotated: the existing session gets 401 on its next request.
- Lockout is cleared.
- No API or UI surface (including the Manager's) ever returns the old or new password or its hash.
- Audit records actor, target and time with no password material.
- A policy-violating password is rejected; a Doctor or Sales caller receives 403.

**Test level:** API / Security

### AC-AUTH-004 — No self-service password reset

**Traces:** FR-AUTH-004
**Type:** Negative
**Task:** 2

**Given**
- The route table, schema and OpenAPI documents.

**When**
- They are inspected and `/api/auth/password-reset/*` is called.

**Then**
- No recovery-initiation route is mapped (404); no reset-token column, reset flag or recovery state exists; no email / SMS delivery exists (see AC-NEG-018).

**Linked frozen tests:** SEC #13
**Test level:** Security / DB

### AC-AUTH-005 — Login

**Traces:** FR-AUTH-005
**Type:** Positive / Security
**Task:** 2

**Given**
- An active account, an inactive account and a non-existent username.

**When**
- (a) The active user calls `GET /api/auth/csrf` then `POST /api/auth/login` with correct credentials.
- (b) Login with a wrong password, an unknown username, or an inactive account.
- (c) 5 consecutive failures for one account.
- (d) More than 10 attempts per minute from one IP.

**Then**
- (a) A session cookie is issued; role and permission keys are returned for menu use only.
- (b) All three return the same generic "Invalid credentials" response; the unknown username passes through a dummy hash verification (no timing/enumeration difference beyond tolerance).
- (c) The account is locked for 15 minutes.
- (d) 429.
- Success and failure are audited with IP, user agent and username truncated to 64 characters; no password is stored.

**Test level:** API / Security

### AC-AUTH-006 — Session validity and logout

**Traces:** FR-AUTH-006
**Type:** Security
**Task:** 2

**Given**
- A user with an authenticated session.

**When**
- (a) The Manager deactivates the user, sets their password, changes their role or forces sign-out.
- (b) The session is idle beyond 20 minutes or older than 10 hours.
- (c) An unsafe request is sent without the CSRF token.
- (d) The user calls `POST /api/auth/logout`.

**Then**
- (a) The next request from that session returns 401.
- (b) 401.
- (c) Rejected.
- (d) The cookie is deleted and site data cleared; logout requires authentication.
- The cookie is `__Host-clinic.session`, HttpOnly, Secure, SameSite=Strict.

**Test level:** API / Security

### AC-AUTH-007 — Change own password

**Traces:** FR-AUTH-007
**Type:** Positive / Validation
**Task:** 2

**Given**
- An authenticated user who knows the current password.

**When**
- (a) The user submits the correct current password and a policy-compliant new password.
- (b) The current password is wrong.
- (c) An unauthenticated caller calls the route.

**Then**
- (a) The new password is effective; audited without password material.
- (b) Rejected; password unchanged.
- (c) 401.

**Test level:** API

### AC-AUTH-008 — Role change and forced sign-out

**Traces:** FR-AUTH-008
**Type:** Positive / Validation / Permission
**Task:** 2

**Given**
- A target user with an active session.

**When**
- (a) The Manager changes the role to one the target's staff row fits.
- (b) The Manager changes the role to one the staff row does not fit.
- (c) The Manager forces sign-out.
- (d) A Doctor or Sales user calls either route.

**Then**
- (a) The single role is replaced (still exactly one `user_roles` row); the stamp rotates; the target's next request returns 401; audited.
- (b) Refused; role unchanged.
- (c) The stamp rotates; the next request returns 401; audited.
- (d) 403.

**Test level:** API / Security

### AC-AUTH-009 — Initial Manager and break-glass password

**Traces:** FR-AUTH-009
**Type:** Security / Operations
**Task:** 2, 5

**Given**
- A new deployment with no accounts.

**When**
- (a) The deployment command creates the initial Manager with an operator-entered password.
- (b) The operator runs `set-manager-password` inside the `api` container.
- (c) An HTTP client searches for an equivalent endpoint.

**Then**
- No default account exists in any environment before (a).
- (a) and (b) restore Manager access and are audited with a system marker.
- (c) No HTTP route exists for either command.

**Test level:** Operations / Security

### AC-AUTH-010 — Current-user context

**Traces:** FR-AUTH-010
**Type:** Positive / Permission
**Task:** 2

**Given**
- An authenticated user of each role and an anonymous caller.

**When**
- `GET /api/me` is called.

**Then**
- Authenticated: display name, role, permission keys, clinic timezone and locale `ar` / `rtl` are returned.
- Removing a permission key from the client does not change backend authorization (backend still enforces).
- Anonymous: 401.

**Test level:** API

## 3.3 STAFF

### AC-STAFF-001 — Create Doctor staff record

**Traces:** FR-STAFF-001
**Type:** Positive / Validation / Permission / Privacy
**Task:** 2

**Given**
- A Manager holding `Staff.Manage` and an unlinked DOCTOR account.

**When**
- (a) The Manager creates a Doctor with full name, optional specialty and current percentage 40.00 linked to that account.
- (b) The percentage is 100.01 or −1.
- (c) A Doctor or Sales user calls `POST /api/manager/staff/doctors`.
- (d) Sales reads the Doctor lookup.

**Then**
- (a) An active Doctor linked to exactly one user account exists; audited.
- (b) Rejected; nothing written.
- (c) 403.
- (d) `SalesDoctorLookupItem` contains no percentage field.

**Test level:** API / Security

### AC-STAFF-002 — Doctor percentage applies to new work only

**Traces:** FR-STAFF-002
**Type:** Financial / Validation / Permission
**Task:** 2

**Given**
- A Doctor at 40 %, an existing work item snapshotting 40 %, a finalized charge and an existing entitlement on it; current General Lab % = 15.

**When**
- (a) The Manager changes the Doctor percentage to 50.
- (b) A new work item is then created for that Doctor.
- (c) The Manager submits 90 (90 + 15 > 100) or a value outside 0..100.
- (d) A Doctor or Sales user calls the route.

**Then**
- (a) `current_percentage` = 50; audited with old and new values.
- The existing work item, charge, entitlement and payroll rows still carry 40 %.
- (b) The new work item snapshots 50 %.
- (c) Rejected; percentage unchanged.
- (d) 403; the percentage never appears in any Sales response (see AC-NEG-039).

**Test level:** API / Integration

### AC-STAFF-003 — Create Sales employee with required commission rate

**Traces:** FR-STAFF-003
**Type:** Positive / Validation / Permission / Financial
**Task:** 2

**Given**
- A Manager holding `Staff.Manage` and `Staff.SalesCommission.Configure`, and an unlinked SALES_RECEPTION account.

**When**
- (a) The Manager creates a Sales employee with full name, base monthly salary and commission 5.00 %.
- (b) The commission percentage is omitted.
- (c) The commission percentage is outside 0..100.
- (d) A Doctor or Sales user calls the route.

**Then**
- (a) One transaction writes the employee and commission-rate revision 1 with `effective_from` = database time; `staff.sales_employees` has no rate column; audited.
- (b) 400 / 422; nothing written.
- (c) Rejected; nothing written.
- (d) 403.

**Test level:** API / DB

### AC-STAFF-004 — Commission rate change affects future collections only

**Traces:** FR-STAFF-004
**Type:** Financial / Permission / Privacy
**Task:** 2, 4

**Given**
- A Sales employee at 5 % and a Lead-sourced patient attributed to them.
- A first collection of 1,000.00 EGP allocated before the change.

**When**
- (a) The Manager saves a new rate of 7 %.
- (b) A second collection of 1,000.00 EGP is allocated after the save.
- (c) The Manager tries to back-date or future-date `effective_from`.
- (d) A Doctor or Sales user calls the change route; a Sales user calls the history route.

**Then**
- (a) A new revision is appended with `effective_from` = database time of the save; revision 1 is unchanged; audited with old and new rate.
- The first commission item remains 50.00 EGP; the second is 70.00 EGP; total 120.00 EGP.
- Draft and finalized payroll items created before the change are not recalculated.
- (c) No such input is accepted (unknown member → 400).
- (d) 403 in both cases (see AC-NEG-024, AC-NEG-039).

**Test level:** Integration / API

### AC-STAFF-005 — Change Sales base salary

**Traces:** FR-STAFF-005
**Type:** Positive / Permission / Financial
**Task:** 2, 4

**Given**
- A Sales employee with base salary 6,000.00 and an existing Sales payroll snapshotting 6,000.00.

**When**
- (a) The Manager changes the base salary to 7,000.00.
- (b) A later Sales payroll draft is created.
- (c) A Doctor or Sales user calls the route.

**Then**
- (a) `base_monthly_salary` = 7,000.00; audited with old / new.
- The existing payroll `base_salary_snapshot` remains 6,000.00.
- (b) The new draft snapshots 7,000.00.
- (c) 403.

**Test level:** API / Integration

### AC-STAFF-006 — Create and edit Assistant staff record

**Traces:** FR-STAFF-006
**Type:** Positive / Validation / Permission / Privacy
**Task:** 2, 4

**Given**
- A Manager holding `Staff.Manage`.

**When**
- (a) The Manager creates an Assistant with full name and current salary > 0.
- (b) The salary is 0 or negative.
- (c) The Manager edits the record with a stale `If-Match`.
- (d) A Doctor or Sales user calls any Assistant route.

**Then**
- (a) An active Assistant record exists; no `identity.users`, `user_roles` or credential is created; audited.
- (b) Rejected.
- (c) 412.
- (d) 403; no Doctor or Sales response anywhere contains Assistant data.
- No bonus, deduction, commission or incentive field or route exists for Assistants (see AC-NEG-004, AC-NEG-025, AC-NEG-040).

**Test level:** API / Security

### AC-STAFF-007 — Change Assistant salary

**Traces:** FR-STAFF-007
**Type:** Positive / Validation / Permission / Financial
**Task:** 4

**Given**
- An Assistant with salary 4,000.00 and an existing payroll snapshot of 4,000.00.

**When**
- (a) A Manager holding `Staff.AssistantSalary.Configure` sets 4,500.00 with a reason and valid `If-Match`.
- (b) The salary is ≤ 0.
- (c) The caller holds only `Staff.Manage`, or only `Payroll.Assistant.ManageAll`.
- (d) A Doctor or Sales user calls the route.

**Then**
- (a) `current_salary` = 4,500.00; audited with old / new; the existing payroll snapshot stays 4,000.00; later drafts use 4,500.00.
- (b) Rejected.
- (c), (d) 403.

**Test level:** API / Security

### AC-STAFF-008 — Configure Assistant overtime hourly rate

**Traces:** FR-STAFF-008
**Type:** Positive / Validation / Permission / Financial
**Task:** 4

**Given**
- An Assistant with overtime rate 50.00 and an existing overtime entry snapshotting 50.00.

**When**
- (a) The Manager sets the rate to 60.00 with a reason and valid `If-Match`.
- (b) The rate is negative.
- (c) A Doctor or Sales user calls the route.

**Then**
- (a) Rate = 60.00; audited with old / new; the existing entry and any payroll item still use 50.00; later entries snapshot 60.00.
- A rate of 0.00 is accepted.
- (b) Rejected.
- (c) 403.

**Test level:** API / Integration

### AC-STAFF-009 — Deactivate an employee

**Traces:** FR-STAFF-009
**Type:** Positive / Validation / Permission / Financial
**Task:** 2, 4

**Given**
- Active Doctor, Sales employee and Assistant records; the Sales employee owns open Leads / Follow-Ups and has an active session.

**When**
- (a) The Manager deactivates each.
- (b) The Manager tries to deactivate the last active Manager or their own account.
- (c) A Doctor or Sales user calls the route.

**Then**
- (a) In one transaction per call: staff `is_active = false`, `deactivated_at` set; for Doctor / Sales the user is deactivated, lockout set to maximum and the stamp rotated; nothing is deleted; audited.
- The deactivated Sales user's next request returns 401 and login fails.
- Sales: open Leads / Follow-Ups are redistributed per AC-CRM-010 / AC-CRM-011; no commission item is created for `allocated_at` ≥ `deactivated_at` (AC-PAYROLL-005).
- Doctor: no financial row changes (AC-FIN-030).
- Notifications thereafter fan out only to active users.
- (b) Refused.
- (c) 403.

**Test level:** API / Integration

### AC-STAFF-010 — Reactivate an employee

**Traces:** FR-STAFF-010
**Type:** Positive / Permission / Financial
**Task:** 2

**Given**
- An inactive Sales employee whose Leads were redistributed, with collections on their Lead-sourced patients during inactivity.

**When**
- (a) The Manager reactivates the employee.
- (b) The user tries to log in before the Manager sets a new password.
- (c) A Doctor or Sales user calls the route.

**Then**
- (a) Staff and user are active; the stamp rotates; audited.
- Redistributed Leads / Follow-Ups stay with their new owners.
- No commission item is created for collections made during inactivity.
- (b) Login fails until the Manager sets a new password.
- (c) 403.

**Test level:** API / Integration

### AC-STAFF-011 — Employees and Manager accounts are never deleted

**Traces:** FR-STAFF-011
**Type:** Negative
**Task:** 2

**Given**
- The route table and the `clinic_app` database role.

**When**
- A DELETE is attempted on any staff or user route, and a direct `DELETE` is attempted as `clinic_app` on `staff.*` and `identity.users`.

**Then**
- No delete route exists (404 / 405).
- `clinic_app` has no DELETE grant on these tables (permission denied).
- The last active Manager cannot be deactivated.

**Test level:** DB / Security

### AC-STAFF-012 — Manager records attendance

**Traces:** FR-STAFF-012
**Type:** Positive / Validation / Permission / Privacy
**Task:** 4

**Given**
- An Assistant A1 and a Sales employee S1; a Manager holding `Staff.Attendance.Manage`.

**When**
- (a) The Manager records A1 Present 09:00–17:00 and S1 Late on the same clinic-local date.
- (b) A second record for A1 on the same date.
- (c) A record naming both A1 and S1, or neither.
- (d) A1 Absent with check-in time; check-out ≤ check-in.
- (e) A record for a Doctor.
- (f) A Doctor or Sales user calls the route.

**Then**
- (a) Both records stored with status, times, notes and recorded-by; audited.
- (b) 409 `attendance.duplicate_day`.
- (c), (d) Rejected.
- (e) Rejected; no Doctor attendance exists.
- (f) 403.
- No self-check-in, clock-in route, Assistant / Sales write route, biometric or GPS source exists; attendance appears in no Doctor or Sales response (see AC-NEG-026).

**Test level:** API / DB

### AC-STAFF-013 — Edit attendance

**Traces:** FR-STAFF-013
**Type:** Positive / Validation / Audit
**Task:** 4

**Given**
- A Present record for A1.

**When**
- (a) The Manager changes it to Absent with times cleared, using a valid `If-Match`.
- (b) The edit uses a stale ETag, or sets Absent with times.
- (c) Doctor or Sales calls the route.

**Then**
- (a) Updated; audited with before / after.
- (b) 412; rejected.
- (c) 403.

**Test level:** API

### AC-STAFF-014 — Attendance alone changes no pay

**Traces:** FR-STAFF-014
**Type:** Financial / Negative
**Task:** 4

**Given**
- A1 (salary 6,000.00) with 5 Absent and 3 Late days in a month and check-out times 3 h after shift end; S1 with Absent days and Lead-sourced collections.

**When**
- Payroll drafts are created for the month.

**Then**
- A1 Base Pay = 6,000.00 and overtime = only explicitly approved entries (0.00 if none); no deduction, penalty or overtime derived from attendance.
- S1's base salary and commission are unaffected by attendance.
- No FK from any payroll table to `staff.attendance_records` exists (see AC-NEG-026).

**Test level:** Integration / DB

### AC-STAFF-015 — Record approved Assistant overtime

**Traces:** FR-STAFF-015
**Type:** Positive / Validation / Financial
**Task:** 4

**Given**
- A1 with overtime hourly rate 100.00 and an attendance record on 2026-10-10.

**When**
- (a) The Manager records 5 approved hours on 2026-10-10 linked to that attendance record.
- (b) The rate is then changed to 120.00.
- (c) Hours 0 or 24.5; A1 without a rate; a voided / missing Assistant; a second live entry on the same day; a link to another Assistant's or date's attendance.
- (d) Doctor or Sales calls the route.

**Then**
- (a) Entry with `hourly_rate_snapshot` = 100.00 and amount = 500.00; audited.
- (b) The existing entry stays 500.00.
- (c) Rejected; 422; 422; 409; rejected.
- (d) 403.
- Overtime is never inferred from check-in / check-out.

**Test level:** API / Integration

### AC-STAFF-016 — Void an overtime entry

**Traces:** FR-STAFF-016
**Type:** Positive / Validation / Audit
**Task:** 4

**Given**
- An unreferenced overtime entry E1 and an entry E2 attached to a payroll item.

**When**
- (a) The Manager voids E1 with reason, then records a corrected entry for the same day.
- (b) The Manager voids E1 again.
- (c) The Manager voids E2.
- (d) An edit or delete of any entry is attempted.

**Then**
- (a) `voided_at`, `voided_by_user_id`, `void_reason` set once; the new entry is accepted; audited.
- (b) Rejected.
- (c) Rejected (G2).
- (d) No edit / delete route; DB rejects.

**Test level:** API / DB

### AC-STAFF-017 — Sales has no overtime

**Traces:** FR-STAFF-017
**Type:** Negative
**Task:** 4

**Given**
- Schema and route table.

**When**
- An overtime entry is submitted for a Sales employee id; the schema is inspected.

**Then**
- Rejected; no overtime table, column or route accepts a Sales employee; Sales has attendance only (see AC-NEG-026).

**Test level:** API / DB

## 3.4 PAT

### AC-PAT-001 — Register a Walk-In / Direct patient

**Traces:** FR-PAT-001
**Type:** Positive / Validation / Permission / Financial
**Task:** 2

**Given**
- A Sales user holding both `Patients.Identity.Write` and `Patients.Contact.Write`.

**When**
- (a) Sales registers a patient with full name, DOB and contact fields.
- (b) Sales registers with only a recorded age of 35.
- (c) The request contains an Original Sales Employee field or any unknown member.
- (d) Neither DOB nor recorded age is supplied, or age is outside 0..120.
- (e) The caller holds only one of the two permissions.
- (f) The request contains any medical field.

**Then**
- (a) A patient with a `patient_code` and `registered_at` exists; the contact profile has `patient_source = WalkIn`, no Original Sales Employee, and the registering user only as `created_by_user_id`; audited with contact values restricted.
- (b) Stored as recorded age with recorded-on date; no DOB is inferred.
- (c) 400.
- (d) Rejected.
- (e) 403.
- (f) 400 (registration contains no medical fields).
- A later collection from this patient produces zero Sales commission and no pool entry (AC-PAYROLL-004, AC-NEG-015).

**Test level:** API

### AC-PAT-002 — Lead-sourced patient creation

**Traces:** FR-PAT-002
**Type:** Positive / Validation / Privacy
**Task:** 2

**Given**
- An open Lead owned by Sales employee S1.

**When**
- (a) The Lead is converted.
- (b) A direct insert sets `patient_source = Lead` with no Original Sales, or `WalkIn` with one.
- (c) An update tries to change `patient_source` or `original_sales_employee_id`.
- (d) A Doctor opens the patient.

**Then**
- (a) The patient has `patient_source = Lead` and Original Sales Employee = S1, copied once; `crm.leads.converted_patient_id` set; audited.
- (b) Rejected by CHECK.
- (c) Rejected; values unchanged.
- (d) No Original Sales Representative or patient source appears in the Doctor response.

**Test level:** API / DB

### AC-PAT-003 — Patient identity and age display

**Traces:** FR-PAT-003
**Type:** Positive / Validation / Privacy
**Task:** 2

**Given**
- Patient A with DOB; patient B with only recorded age 30 recorded on 2025-01-10; setting `doctor_dob_visible` at its default (on).

**When**
- Doctor DTO, Sales DTO, Manager view and prescription print model are produced on 2026-10-23; then `doctor_dob_visible` is turned off.

**Then**
- A: current age computed from DOB on every surface.
- B: "Age recorded at registration: 30 (recorded on 2025-01-10)" on every surface; never aged forward.
- With the setting off, the Doctor DTO omits DOB; Sales always receives DOB.
- A row with both age columns set inconsistently (one null) or with neither DOB nor recorded age is rejected.

**Test level:** API / DB

### AC-PAT-004 — Patient search by audience

**Traces:** FR-PAT-004
**Type:** Positive / Privacy / Validation
**Task:** 2

**Given**
- A patient with code, name and phone 01000000001.

**When**
- (a) Doctor searches by code, then by name, then types the phone into the name box.
- (b) Sales and Manager search by phone.
- (c) A search is attempted via GET with PII in the query string.
- (d) A page size above 50 is requested; a user exceeds 60 searches/min.

**Then**
- (a) Code and name find the patient; the phone finds nothing; Doctor list items contain only id, code, name and age display.
- (b) The patient is found.
- (c) Rejected.
- (d) Page size capped / rejected per contract; rate limit returns 429.
- Permission check: `Patients.Search.Clinical` for Doctor / Manager, `Patients.Search.Contact` for Sales / Manager; a Doctor calling the Sales search route receives 403 (see AC-NEG-034).

**Test level:** API / Security

### AC-PAT-005 — Edit patient identity and contact data

**Traces:** FR-PAT-005
**Type:** Positive / Validation / Permission / Audit
**Task:** 2

**Given**
- A patient and a Sales user holding the identity and contact write permissions.

**When**
- (a) Sales updates identity, then contact, each with a valid `If-Match`.
- (b) An update uses a stale ETag.
- (c) An update includes `patient_source` or Original Sales Employee.
- (d) A Doctor calls either route.

**Then**
- (a) Each is updated separately; audited; contact changes store previous / new values flagged `is_restricted`, readable only by the Manager.
- (b) 412.
- (c) 400; values unchanged.
- (d) 403.

**Test level:** API

### AC-PAT-006 — Sales patient view

**Traces:** FR-PAT-006
**Type:** Positive / Privacy
**Task:** 2

**Given**
- A Lead-sourced patient with a chief complaint, clinical notes and Patient Credit of 300.00; a Walk-In patient.

**When**
- Sales calls `GET /api/sales/patients/{id}` for each.

**Then**
- The response contains id, code, full name, DOB or recorded age, age display, contact fields, CRM notes, `patientSource`, active flag, registered at, current Doctor and credit balance 300.00.
- Original Sales Employee appears only for the Lead-sourced patient.
- No chief complaint and no Doctor-authored clinical field appears.
- An access event is written for the contact view.

**Test level:** API / Security

### AC-PAT-007 — Doctor patient view without contact data

**Traces:** FR-PAT-007
**Type:** Positive / Privacy / Audit
**Task:** 2

**Given**
- A patient with contact data and CRM notes, assigned to Doctor D1.

**When**
- Doctor D2 (not assigned) calls `GET /api/doctor/patients/{id}`.

**Then**
- 200 with id, code, full name, age display, chief complaint, active flag, registered at and current Doctor.
- The response contains no phone, WhatsApp, email, address, CRM notes, Original Sales Employee or patient source (canary values absent; see AC-NEG-034).
- The open is audited as record access.

**Test level:** API / Security

### AC-PAT-008 — Chief Complaint maintenance

**Traces:** FR-PAT-008
**Type:** Positive / Validation / Permission / Privacy
**Task:** 2

**Given**
- A Doctor with an editable Visit for the patient, and another Doctor with no editable Visit for that patient.

**When**
- (a) The first Doctor updates the chief complaint with a valid `If-Match`.
- (b) The second Doctor tries the same.
- (c) Sales calls the route; Sales reads the patient.

**Then**
- (a) Updated; audited.
- (b) Refused.
- (c) 403; the chief complaint never appears in any Sales response.
- The chief complaint is not part of the Medical History Intake.

**Test level:** API

### AC-PAT-009 — Manager reassigns a patient's Doctor

**Traces:** FR-PAT-009
**Type:** Positive / Permission / Financial
**Task:** 2, 4

**Given**
- A patient assigned to D1 with a finalized charge performed by D1 and a pending entitlement.

**When**
- (a) The Manager reassigns the patient to D2 with a reason and valid `If-Match`.
- (b) A later payment is allocated to the D1 charge.
- (c) A Doctor or Sales user calls the route.

**Then**
- (a) In one transaction the current assignment gets `ended_at` and a new row stores previous Doctor, new Doctor, time, Manager and reason; exactly one current Doctor; audited; history readable via the assignments route.
- (b) The entitlement and earning go to D1; no payment, allocation, entitlement or earning is delayed or altered.
- (c) 403.

**Test level:** Integration / API

### AC-PAT-010 — Patient timelines by audience

**Traces:** FR-PAT-010
**Type:** Positive / Privacy
**Task:** 2

**Given**
- A patient with visits, treatments, prescriptions, appointments, follow-ups and payments, including payments on another Doctor's work.

**When**
- Doctor, Sales and Manager each call their timeline route.

**Then**
- Doctor: visits, treatments, prescriptions; payment entries only for the Doctor's own work; follow-up entries show type, due date and status only; no contact data.
- Sales: appointments, follow-ups and payments; no prescription or visit content.
- Manager: full history.
- A Sales caller on the clinical timeline route and a Doctor on the operational route each receive 403.

**Test level:** API / Security

## 3.5 MED

### AC-MED-001 — Structured intake content

**Traces:** FR-MED-001
**Type:** Positive / Validation / Negative
**Task:** 2

**Given**
- A patient with no intake.

**When**
- (a) Sales submits an intake answering each of: Allergy; High Blood Pressure / Hypertension; Viral Hepatitis; Diabetes; Kidney Problems; Bleeding / Blood Clotting Problems; Heart Problems; Rheumatic Diseases; Smoker; Previous Surgical Operations; Currently Taking Medications; Last Dental Visit (free text); Pregnant; Breastfeeding.
- (b) `lastDentalVisit` has 201 characters.
- (c) The request contains an unknown member (e.g. allergy type, medication names, surgery details, any "other condition" or generic medical-history text).
- (d) A second intake is created for the same patient.

**Then**
- (a) Exactly one current intake exists, holding exactly these questions; `lastDentalVisit` is stored as free text and never coerced to a date.
- (b) Rejected.
- (c) 400; no such column or DTO property exists (see AC-NEG-032).
- (d) Not possible: 0..1 intake per patient (subsequent PUT replaces answers).

**Test level:** API / DB

### AC-MED-002 — Sales collects and edits the intake

**Traces:** FR-MED-002
**Type:** Positive / Validation / Permission / Audit
**Task:** 2

**Given**
- A Sales user holding `Patients.MedicalHistory.EditIntake`, and a patient without an intake and with no appointment.

**When**
- (a) `GET /api/sales/patients/{id}/medical-history`.
- (b) First PUT without `If-Match`.
- (c) Second PUT with a valid `If-Match`; then a PUT with a stale ETag.
- (d) A flag is sent as `"yes"` or `1`.

**Then**
- (a) 404.
- (b) Intake created regardless of appointment state.
- (c) Answers replaced; the stale PUT returns 412.
- (d) Rejected; flags accept only `true` / `false` / `null`.
- Each create / update is audited with actor, timestamp, patient id and changed field **names** only (see AC-NEG-037).

**Test level:** API

### AC-MED-003 — Doctor reads the intake read-only

**Traces:** FR-MED-003
**Type:** Positive / Permission / Privacy / Audit
**Task:** 2

**Given**
- A patient with an intake, opened by a Doctor from the arrival queue.

**When**
- (a) The Doctor calls `GET /api/doctor/patients/{id}/medical-history`.
- (b) The Doctor attempts any write (PUT / POST / PATCH) on the intake via any route.

**Then**
- (a) `DoctorPatientMedicalHistoryDto` returned with no contact field and no ETag; record access audited.
- (b) 403; no Doctor write route exists and DOCTOR holds no `EditIntake` permission.

**Test level:** API / Security

### AC-MED-004 — Unanswered is never "No"

**Traces:** FR-MED-004
**Type:** Validation / DB
**Task:** 2

**Given**
- An intake submitted with Diabetes = `true`, Smoker = `false` and all other flags omitted or `null`.

**When**
- The intake is stored and read back by Sales and Doctor.

**Then**
- Diabetes returns `true`, Smoker `false`, every other flag `null` (never `false`).
- Flag columns have no DEFAULT and no NOT NULL constraint.

**Linked frozen tests:** API A11 #38
**Test level:** API / DB

### AC-MED-005 — Pregnancy and breastfeeding questions

**Traces:** FR-MED-005
**Type:** Validation / Privacy
**Task:** 2

**Given**
- Two intakes: one where Sales answers Pregnant = `false`, Breastfeeding = `true`; one where the section is not applicable.

**When**
- Both are stored and read back.

**Then**
- The first returns `false` / `true`; the second returns `null` / `null`, never `false`.
- Neither answer is required for the intake to be saved.
- No patient sex field exists in any table or DTO.
- No notification text names a condition, including pregnancy.

**Test level:** API / DB

### AC-MED-006 — Intake privacy and audit

**Traces:** FR-MED-006
**Type:** Privacy / Audit
**Task:** 2

**Given**
- An intake updated with canary values (e.g. a unique Last Dental Visit string and changed flags).

**When**
- The audit log, application logs, notification payloads, reports, Manager financial reports and exports are scanned.

**Then**
- The audit row contains actor, timestamp, patient id and changed field names only.
- No canary medical value appears in any scanned surface.
- The Worker database role cannot read `patients.patient_medical_histories`.

**Linked frozen tests:** SEC #51
**Test level:** Security / Integration

### AC-MED-007 — Intake never blocks booking

**Traces:** FR-MED-007
**Type:** Positive
**Task:** 2

**Given**
- A newly registered patient with no intake.

**When**
- Sales books an appointment, records arrival, and the Doctor starts a Visit.

**Then**
- Every step succeeds; none checks for an intake.
- The Doctor's intake read returns "no intake" without error affecting the Visit.
- No Assistant access exists.

**Test level:** Integration

### AC-MED-008 — Intake access grants Sales no other clinical access

**Traces:** FR-MED-008
**Type:** Privacy / Permission
**Task:** 2

**Given**
- A Sales user holding the intake keys.

**When**
- Sales calls clinical-note, canal-measurement, prescription, Doctor-percentage, share and lab routes, and reads the intake DTO.

**Then**
- Each of those routes returns 403.
- The Sales intake DTO contains no clinical note, measurement, prescription, percentage, share or lab field (see AC-NEG-035).

**Linked frozen tests:** SEC #50
**Test level:** Security

## 3.6 CRM

### AC-CRM-001 — Create and manage Leads

**Traces:** FR-CRM-001
**Type:** Positive / Validation / Privacy
**Task:** 2

**Given**
- A Sales user S1 holding `Crm.Leads.Manage`.

**When**
- (a) S1 creates a Lead with name, phone, source, interested service, notes, next follow-up date and status `New`.
- (b) S1 updates the status successively to each of: New, Contacted, Interested, Call Later, No Answer, Not Interested, Booked, Converted.
- (c) A status outside these 8 is submitted.
- (d) An update is sent with a stale `If-Match`; an update tries to change `original_sales_employee_id`.
- (e) A Doctor calls any Lead route.

**Then**
- (a) The Lead has `original_sales_employee_id` = S1 and `responsible_sales_employee_id` = S1 and appears in S1's My Leads; audited.
- (b) Each of the 8 statuses is accepted.
- (c) Rejected.
- (d) 412; the attribution change is rejected.
- Open = no `converted_patient_id` and a status not classified closed in the seeded catalogue.
- (e) 403; Lead data is never visible to Doctors.

**Test level:** API / DB

### AC-CRM-002 — My Leads

**Traces:** FR-CRM-002
**Type:** Positive / Permission
**Task:** 2

**Given**
- Leads responsible to S1, to S2, and unassigned.

**When**
- S1 calls `GET /api/sales/leads/my` (including with an attempted employee-id parameter).

**Then**
- Only S1's Leads are returned; identity comes from the principal; the parameter has no effect / is rejected.
- Unassigned Leads never appear.

**Test level:** API / Security

### AC-CRM-003 — All Leads (Manager)

**Traces:** FR-CRM-003
**Type:** Positive / Permission
**Task:** 2

**Given**
- Assigned and unassigned Leads.

**When**
- (a) The Manager calls `GET /api/manager/leads`, then with `unassigned=true`.
- (b) Sales calls the route.

**Then**
- (a) Every Lead with its Responsible Sales Member; the filter returns only unassigned Leads.
- (b) 403.

**Test level:** API

### AC-CRM-004 — Convert a Lead to a Patient

**Traces:** FR-CRM-004
**Type:** Positive / Validation
**Task:** 2

**Given**
- An unconverted Lead with Original Sales Employee S1.

**When**
- (a) The Lead is converted.
- (b) Conversion is attempted again.

**Then**
- (a) Patient and contact profile created with `patient_source = Lead` and Original Sales = S1; `converted_patient_id` set; audited.
- (b) Refused (unique `converted_patient_id`).
- No other route can set an Original Sales Employee on a patient.

**Test level:** API / DB

### AC-CRM-005 — Create and manage Follow-Ups

**Traces:** FR-CRM-005
**Type:** Positive / Validation
**Task:** 2

**Given**
- A Sales user holding `Crm.FollowUps.Manage`.

**When**
- (a) Follow-ups are created with each type: Lead, Doctor Referral, Implant Appointment, Ortho Installment, Remaining Balance, General Treatment Follow-Up.
- (b) A seventh type is submitted.
- (c) Two source references are set, or neither patient nor lead is supplied.
- (d) An update uses a stale `If-Match`.
- (e) A charge becomes outstanding and an installment becomes due.

**Then**
- (a) Each is stored with type, patient or lead, assigned Sales employee, due date, status `New`, notes, result, next follow-up date and at most one source reference; audited.
- (b), (c) Rejected.
- (d) 412.
- (e) No follow-up is created automatically.
- A Remaining Balance follow-up yields the Outstanding Payment Follow-Up notification; every other type yields Pending Follow-Up (AC-NOTIF-005).

**Test level:** API

### AC-CRM-006 — Exact Follow-Up statuses and open predicate

**Traces:** FR-CRM-006
**Type:** Validation / DB
**Task:** 2

**Given**
- Follow-ups in each status.

**When**
- (a) A status outside `New`, `Contacted`, `NoResponse`, `FollowedUp` is written via API or directly to the DB.
- (b) Pending Follow-Ups, J3, J7, dashboard counts and redistribution evaluate "open".

**Then**
- (a) Rejected (API validation and CHECK).
- (b) Open = `New`, `Contacted`, `NoResponse`; closed = `FollowedUp`; every consumer returns identical sets for the same data.
- Referral progress status is a separate field and does not affect this predicate.
- Status changes are audited.

**Linked frozen tests:** API A11 #15; B9 #9
**Test level:** API / DB

### AC-CRM-007 — Follow-Up contact attempts

**Traces:** FR-CRM-007
**Type:** Positive / Audit
**Task:** 2

**Given**
- An open follow-up.

**When**
- Two contact attempts are recorded; then an update / delete of the first attempt is attempted.

**Then**
- Both attempts exist with time, user, outcome and notes.
- No update or delete route exists; the table is insert-only.

**Test level:** API / DB

### AC-CRM-008 — Sales sees only own Follow-Ups

**Traces:** FR-CRM-008
**Type:** Permission
**Task:** 2

**Given**
- Follow-ups assigned to S1, to S2 and unassigned.

**When**
- S1 lists follow-ups and requests S2's follow-up by id.

**Then**
- S1's list contains only S1's follow-ups; unassigned ones are absent.
- S2's follow-up returns 404.

**Test level:** API / Security

### AC-CRM-009 — Manager assigns / reassigns Leads and Follow-Ups

**Traces:** FR-CRM-009
**Type:** Positive / Permission / Audit
**Task:** 2

**Given**
- A Lead owned by S1 with Original Sales S1, and an unassigned follow-up.

**When**
- (a) The Manager reassigns the Lead to S2.
- (b) The Manager lists `GET /api/manager/follow-ups?unassigned=true` and assigns the follow-up to S2.
- (c) Sales calls either route; any `/api/unassigned/*` route is called.

**Then**
- (a) Responsible = S2; a `responsibility_history` row with reason `ManagerReassigned` is written in the same transaction; Original Sales stays S1.
- (b) The follow-up appears in the filter, then in S2's list; history reason `ManagerAssignedUnassigned`.
- (c) 403; the `/api/unassigned/*` route does not exist.

**Test level:** API

### AC-CRM-010 — Inactive Sales: even redistribution

**Traces:** FR-CRM-010
**Type:** Positive / Financial
**Task:** 2

**Given**
- S1 owns 7 open Leads/Follow-Ups (plus closed and converted items); active S2 owns 2 open items and S3 owns 0.

**When**
- The Manager deactivates S1.

**Then**
- In the deactivation transaction, every open Lead (not converted, not closed status) and every open Follow-Up (`status <> 'FollowedUp'`) of S1 is reassigned round-robin over the batch ordered by creation time, starting with the employee with fewest open items (S3).
- Final open-item counts of S2 and S3 differ by at most one.
- One `responsibility_history` row per item with reason `SalesDeactivated` and one shared batch id.
- Closed and converted items stay with S1.
- Original Sales attribution is unchanged; no commission is transferred to S2 or S3.
- New owners are notified; J7 reports zero open follow-ups owned by inactive Sales.

**Test level:** Integration

### AC-CRM-011 — No Active Sales left: items become unassigned

**Traces:** FR-CRM-011
**Type:** Positive
**Task:** 2

**Given**
- S1 is the last active Sales employee and owns open Leads / Follow-Ups.

**When**
- The Manager deactivates S1, then assigns one item via FR-CRM-009.

**Then**
- Deactivation succeeds (never refused for lack of colleagues).
- Open items get responsible = NULL; `responsibility_history.to_sales_employee_id` = NULL.
- They appear only in the Manager's Unassigned Leads / Unassigned Follow-Ups; no reminder fires for them; J7 counts them.
- After assignment, the item appears in the new owner's list and reminders resume.

**Test level:** Integration

### AC-CRM-012 — Original Sales attribution is permanent

**Traces:** FR-CRM-012
**Type:** Negative / Financial / Privacy
**Task:** 2

**Given**
- A Lead-sourced patient with Original Sales S1.

**When**
- Reassignment, redistribution, deactivation of S1, contact edits and a direct attempt to change `original_sales_employee_id` / `patient_source` are performed.

**Then**
- `crm.leads.original_sales_employee_id`, `patient_contacts.original_sales_employee_id` and `patient_source` remain unchanged; direct change attempts are rejected.
- Only responsible / assigned columns change.
- The Original Sales Employee never appears in any Doctor response.

**Test level:** Integration / DB

## 3.7 SCH

### AC-SCH-001 — Manager publishes weekly Doctor availability

**Traces:** FR-SCH-001
**Type:** Positive / Validation / Permission
**Task:** 3

**Given**
- A Doctor with no week record for week start 2026-11-01.

**When**
- (a) The Manager drafts ranges Sun 09:00–13:00 and Sun 15:00–19:00, then publishes.
- (b) The Manager adds an overlapping range Sun 12:00–16:00.
- (c) The Manager withdraws a range.
- (d) A second week record for the same Doctor and week start is created.
- (e) A Doctor or Sales user calls the publish route.

**Then**
- (a) The published week is visible to Sales and to that Doctor; audited.
- (b) 409 (availability exclusion constraint).
- (c) The range is marked withdrawn, not deleted.
- (d) Rejected (one week per Doctor per week start).
- (e) 403.
- The availability exclusion constraint never applies to appointments (AC-SCH-004).

**Test level:** API / DB

### AC-SCH-002 — View availability and schedules

**Traces:** FR-SCH-002
**Type:** Positive / Privacy
**Task:** 3

**Given**
- Doctor D1 with a published week and a draft (unpublished) week; an appointment 10:00–11:00 whose Visit ended at 10:20.

**When**
- (a) Sales calls `GET /api/sales/doctors` and `GET /api/sales/availability/slots`.
- (b) D1 calls `GET /api/doctor/schedule`.

**Then**
- (a) Doctor items contain id, name and specialty only (no percentage); slots come from published weeks only; 10:20–11:00 is shown as not booked.
- (b) D1 sees only own published weeks.

**Test level:** API

### AC-SCH-003 — Book an appointment with manual duration

**Traces:** FR-SCH-003
**Type:** Positive / Validation / Permission / Privacy
**Task:** 3

**Given**
- A Sales user holding `Scheduling.Appointments.Book`, and a patient with no Medical History intake.

**When**
- (a) Sales books with patient, Doctor, start, `durationMinutes` = 45, reason, Sales notes and kind `Standard`.
- (b) Duration is missing, 0, or 481.
- (c) Duration 1 and duration 480 are booked.
- (d) Sales-authored `reason` contains a phone number or email pattern.
- (e) A booking for kind `ImplantFollowUp` is made by the Manager.

**Then**
- (a) Appointment created with status `Scheduled` and duration 45 exactly as entered; audited.
- (b) 400.
- (c) Both accepted.
- (d) Rejected by the heuristic check.
- (e) Accepted.
- No clinic-wide default duration or procedure-duration mapping is applied.
- Booking succeeds without an intake.
- `reason` is visible to the Doctor; `sales_notes` is never returned to the Doctor.

**Test level:** API

### AC-SCH-004 — Overlapping appointments allowed

**Traces:** FR-SCH-004
**Type:** Positive / Negative
**Task:** 3

**Given**
- Doctor D1 with an appointment 10:00–11:00.

**When**
- (a) Sales books D1 for 10:30–11:30.
- (b) The Manager books D1 for exactly 10:00–11:00.
- (c) Two concurrent bookings for D1 at the same time are sent.

**Then**
- (a), (b), (c) All succeed; each is audited.
- No conflict response, warning-as-error, override permission or override workflow exists.
- No same-slot unique index or exclusion constraint exists on `scheduling.appointments` (schema check).

**Linked frozen tests:** API A11 #9
**Test level:** API / DB

### AC-SCH-005 — Exactly seven appointment statuses

**Traces:** FR-SCH-005
**Type:** Validation / Negative
**Task:** 3

**Given**
- The appointment status catalogue and CHECK constraint.

**When**
- (a) Each of Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled is written.
- (b) `Arrived` or `Waiting` is submitted via the API.
- (c) Any other value is written directly to the DB.

**Then**
- (a) Accepted; each change records time, user and reason.
- (b) 400.
- (c) Rejected by CHECK.
- In Treatment is set only by Visit start, Completed only by Visit finalization, Rescheduled only by reschedule (see AC-NEG-013, AC-NEG-014).

**Test level:** API / DB

### AC-SCH-006 — Confirm, Cancel, No Show

**Traces:** FR-SCH-006
**Type:** Positive / Validation / Permission
**Task:** 3

**Given**
- A Scheduled appointment.

**When**
- (a) Sales confirms it; the Manager confirms it again.
- (b) Sales cancels without a reason; then with a reason.
- (c) Sales marks another appointment No Show.
- (d) A Doctor calls the status route.
- (e) A status change is sent without `If-Match`.

**Then**
- (a) Confirmed; the repeat is idempotent.
- (b) 400; then Cancelled with `status_change_reason` stored.
- (c) No Show.
- Each change stores `status_changed_at`, `status_changed_by_user_id`, `status_change_reason`; audited.
- (d) 403.
- (e) 428.

**Test level:** API

### AC-SCH-007 — Reschedule preserves history

**Traces:** FR-SCH-007
**Type:** Positive / Validation / Audit
**Task:** 3

**Given**
- A Confirmed appointment A with a pending reminder.

**When**
- (a) Sales reschedules A to a new time with a reason.
- (b) A direct update of A's start time, duration, Doctor, patient or reason is attempted.
- (c) Two concurrent reschedules of the same appointment are sent.

**Then**
- (a) A new appointment B with `previous_appointment_id` = A exists; A is `Rescheduled` with reason; A is otherwise unchanged and not deleted; A's pending reminder is Cancelled; audited.
- (b) Rejected (these fields are frozen; a change is always a reschedule).
- (c) Exactly one succeeds; the other receives 409 (unique `previous_appointment_id`).

**Test level:** API / DB

### AC-SCH-008 — Doctor creates and reschedules the follow-up appointment

**Traces:** FR-SCH-008
**Type:** Positive / Validation / Permission
**Task:** 3

**Given**
- Doctor D1 with own Visit V1; Doctor D2 with Visit V2.

**When**
- (a) D1 calls `POST /api/doctor/appointments/follow-up` with `{ visitId: V1, startAt, durationMinutes: 30, reason }`.
- (b) `durationMinutes` is omitted, 0, or 481; separately 1 and 480.
- (c) D1 uses `visitId` V2.
- (d) D1 reschedules own follow-up; D1 reschedules a follow-up created by D2 or by Sales.
- (e) The follow-up overlaps another D1 appointment.

**Then**
- (a) An appointment with `doctor_id` = D1 (the authoring Doctor), `duration_minutes` = 30, `created_by_user_id` = D1's user, status `Scheduled` is created; it appears in the Sales schedule and generates its reminder; audited.
- (b) 400 for omitted / 0 / 481 (no default duration is applied); 1 and 480 accepted.
- (c) 404.
- (d) Own follow-up: rescheduled per AC-SCH-007; others: 404.
- (e) Accepted.
- No Follow-Up Request entity, route or workflow exists.

**Test level:** API / DB

### AC-SCH-009 — Record patient arrival as an event

**Traces:** FR-SCH-009
**Type:** Positive / Validation / Permission / Negative
**Task:** 3

**Given**
- A Confirmed appointment today (clinic-local) for Doctor D1, and a Scheduled appointment tomorrow.

**When**
- (a) Sales records arrival on today's appointment.
- (b) Sales records a second live arrival on it.
- (c) Sales records arrival on tomorrow's appointment, or on a Cancelled appointment.
- (d) A Doctor or the Manager calls the arrival route.

**Then**
- (a) An arrival row with `arrived_at` and the recording user exists; the appointment status remains `Confirmed` (never Arrived / Waiting); the patient appears in D1's queue; audited.
- (b) 409.
- (c) 422.
- (d) 403.
- Starting the Visit does not require an arrival (AC-SCH-012).

**Test level:** API

### AC-SCH-010 — Void a mistaken arrival

**Traces:** FR-SCH-010
**Type:** Positive / Validation / Permission
**Task:** 3

**Given**
- An appointment with a live arrival shown in the Doctor's queue.

**When**
- (a) Sales voids the arrival.
- (b) Sales voids again (no live arrival).
- (c) Sales records a new arrival.
- (d) A Doctor or the Manager calls the void route.

**Then**
- (a) `voided_at` / `voided_by_user_id` set once; the patient leaves the queue; the row is not deleted; audited.
- (b) Refused.
- (c) A new live arrival exists; the patient returns to the queue.
- (d) 403.

**Test level:** API

### AC-SCH-011 — Doctor waiting queue (derived)

**Traces:** FR-SCH-011
**Type:** Positive / Privacy / Permission
**Task:** 3

**Given**
- For Doctor D1 today: appointment A (Confirmed, arrival 09:10), B (Scheduled, arrival 09:05), C (Confirmed, arrival, Visit started), E (Cancelled after arrival), F (arrival voided); an arrived appointment for D2; and an arrival yesterday.

**When**
- (a) D1 calls `GET /api/doctor/queue`.
- (b) D1 passes a Doctor id parameter.
- (c) Sales calls the route.

**Then**
- (a) Exactly B then A (ordered by `arrived_at`); each item has appointment id, patient {id, code, name, age display}, reason, start, duration, arrived at.
- No contact data, `sales_notes`, financial data or Original Sales Employee appears (see AC-NEG-034).
- The queue is derived on read; no queue table or Waiting status exists (see AC-NEG-014).
- (b) Rejected / ignored; identity comes from the principal.
- (c) 403.

**Test level:** API / Security

### AC-SCH-012 — Visit start moves the appointment to In Treatment

**Traces:** FR-SCH-012
**Type:** Positive / Validation
**Task:** 3

**Given**
- A Confirmed appointment with a live arrival, a Scheduled appointment without arrival, and a Cancelled appointment.

**When**
- The Doctor starts a Visit linked to each; then a second Visit is started on the first appointment.

**Then**
- The Confirmed and Scheduled appointments become `In Treatment` and leave the queue (no arrival required); audited.
- The Cancelled appointment's status is not changed by Visit start.
- The second Visit on the same appointment is refused (unique `clinical.visits.appointment_id`).

**Test level:** Integration

### AC-SCH-013 — Finalization completes the appointment and releases unused time

**Traces:** FR-SCH-013
**Type:** Positive
**Task:** 3

**Given**
- An appointment 10:00 with duration 60, In Treatment.

**When**
- (a) The Visit is finalized at 10:25.
- (b) Another appointment's Visit is finalized at 11:15 (after its nominal end 11:00).

**Then**
- (a) In the finalize transaction the appointment becomes `Completed` and `visit_ended_at` = 10:25 (set once); the effective booked end = LEAST(start + duration, `visit_ended_at`) = 10:25; 10:25–11:00 is available again; the appointment row is otherwise unchanged and its history remains; audited.
- (b) Effective booked end stays 11:00 (never extended past nominal end).

**Test level:** Integration / DB

### AC-SCH-014 — Appointment views by audience

**Traces:** FR-SCH-014
**Type:** Privacy / Permission
**Task:** 3

**Given**
- Appointments with `sales_notes`, patient contact data, linked clinical case and a live arrival.

**When**
- Doctor, Sales and Manager list and open appointments.

**Then**
- Doctor: own appointments only, with patient id / code / name, start, duration, status, reason, kind, linked case and `arrivedAt`; no `sales_notes` and no contact data (canary scan; see AC-NEG-034).
- Sales: all appointments with `sales_notes`, patient contact, `arrivedAt`, `arrivalRecordedBy`; no clinical content.
- Arrived-today patients appear in the Sales list via `arrivedAt`, not as a status.
- A Doctor requesting another Doctor's appointment receives 404.

**Test level:** API / Security

## 3.8 CLN

### AC-CLN-001 — Start a Visit

**Traces:** FR-CLN-001
**Type:** Positive / Permission
**Task:** 3

**Given**
- Doctor D1 holding `Clinical.Visits.Write`, a patient, and a Confirmed appointment for D1.

**When**
- (a) D1 starts a Visit with `appointmentId`.
- (b) D1 starts a Visit without an appointment.
- (c) D1 supplies a different Doctor id in the body.
- (d) Sales calls `POST /api/doctor/visits`.

**Then**
- (a) Visit created with Doctor = D1 (from the principal), status editable (Draft / In Progress); the appointment becomes In Treatment; audited.
- (b) Visit created; no appointment is changed.
- (c) 400 (unknown member) — the Doctor is always the principal.
- (d) 403.
- Visit statuses are only Draft → In Progress → Finalized.

**Test level:** API

### AC-CLN-002 — Start or continue a treatment case

**Traces:** FR-CLN-002
**Type:** Positive / Permission
**Task:** 3

**Given**
- Doctor D1 with an editable Visit; an existing case started by Doctor D2.

**When**
- (a) D1 records a new procedure (type, teeth, area, details).
- (b) D1 records work on D2's existing case in D1's Visit.
- (c) D1 records a second treatment record for the same (Visit, case).
- (d) D1 writes to D2's Visit.
- (e) D1 changes a case status.

**Then**
- (a) A case and a treatment record are created with the lab model snapshot; audited.
- (b) A treatment record for D1 in this Visit is created on the existing case; `started_by_doctor_id` stays D2 and carries no revenue meaning.
- (c) Rejected (one record per Visit and case).
- (d) 404.
- (e) Allowed only among Planned, Active, Completed, Cancelled; audited A+.

**Test level:** API / DB

### AC-CLN-003 — Procedure catalog and option validation

**Traces:** FR-CLN-003
**Type:** Validation
**Task:** 3

**Given**
- The seeded procedure catalog.

**When**
- Treatment details are submitted for each invalid case: composite zone on GIC; missing zone on Composite; extraction type on Pediatric Extraction; Basic / High Quality on any type other than Laser Bleaching; Appliance without free-text type; canal 0 or 6; healing 0 or 7 months; a Sutures or Bone Removal field.
- Valid details are submitted for each catalog entry (Root Canal, Fillings, Extraction Simple / Surgical / Advanced Surgical, each Pediatric option including Other with free text, Implant, Fixed Prosthetics Porcelain/PFM / Zirconia / E-max, Removable Complete / Partial with Flexible / Standard Acrylic, Scaling/Cleaning, Deep Cleaning, Polishing, Laser Bleaching Basic / High Quality, Orthodontics Upper / Lower / Appliance, Other).

**Then**
- Each invalid submission returns 422 `catalog.option_invalid`.
- Each valid submission is accepted.
- Lab model snapshot is Fixed for Implant, Fixed Prosthetics and Removable Prosthetics, and General for every other type including Orthodontics.

**Test level:** API

### AC-CLN-004 — Root canal steps and canal measurements

**Traces:** FR-CLN-004
**Type:** Positive / Validation / Privacy
**Task:** 3

**Given**
- A Root Canal case on D1's editable Visit 1.

**When**
- (a) D1 completes Access Cavity in Visit 1, then attempts to complete it again.
- (b) D1 records canal 1 = 21.5 mm in Visit 1 and canal 1 = 21.0 mm in Visit 2.
- (c) D1 records canal 6.
- (d) Visit 1 is finalized and a direct update of its measurement is attempted.
- (e) Sales requests the case.

**Then**
- (a) Step moves Pending → Completed once with Visit and Doctor; the repeat does not complete it twice.
- Post & Core exists only if the Doctor adds it; steps persist across Visits.
- (b) Current canal 1 = 21.0 mm; the 21.5 mm row remains as history.
- (c) Rejected.
- (d) Rejected (insert-only after finalization).
- (e) No step or measurement appears (see AC-NEG-035).

**Test level:** API / DB

### AC-CLN-005 — Implant details

**Traces:** FR-CLN-005
**Type:** Positive / Validation / Negative
**Task:** 3

**Given**
- An Implant case on D1's editable Visit.

**When**
- (a) D1 stores bone graft, membrane, procedure date and healing 4 months.
- (b) Healing 0 or 7 months is submitted.
- (c) An imaging field is submitted.
- (d) An `ImplantFollowUp` appointment is booked for tomorrow.

**Then**
- (a) Stored; audited; no healing value is hardcoded.
- (b) Rejected.
- (c) 400 (no imaging field exists; see AC-NEG-002).
- (d) The Implant Follow-Up Tomorrow reminder is produced (AC-NOTIF-003).

**Test level:** API

### AC-CLN-006 — Clinical notes on the Visit

**Traces:** FR-CLN-006
**Type:** Positive / Validation / Privacy
**Task:** 3

**Given**
- D1's editable Visit.

**When**
- (a) D1 writes notes.
- (b) After finalization D1 writes notes via `PUT /api/doctor/visits/{id}`.
- (c) Sales reads any patient, appointment or summary surface.

**Then**
- (a) Saved; audited.
- (b) 422 `visit.finalized` (only the correction path applies; AC-CLN-014).
- (c) The notes never appear (canary scan; see AC-NEG-035).

**Test level:** API / Security

### AC-CLN-007 — Doctor enters the initial price of own work

**Traces:** FR-CLN-007
**Type:** Positive / Validation / Permission / Financial
**Task:** 3, 4

**Given**
- D1's treatment record in D1's editable Visit; D2's treatment record; Doctor percentage 40 %.

**When**
- (a) D1 sends `{ price: "3000.00" }` with `If-Match`.
- (b) The Manager calls the route.
- (c) D1 prices D2's work item.
- (d) D1 sends a price of 0, negative, or with 3 decimals; or extra body members.
- (e) A discount of 500.00 exists and D1 sets price 400.00.
- (f) After finalization D1 changes the price.

**Then**
- (a) D1's own work item has `entered_price` = 3,000.00 and percentage snapshot 40 %; audited A+.
- (b) 403 (see AC-NEG-023).
- (c) 404.
- (d) Rejected (body accepts `{ price }` only).
- (e) 422.
- (f) 422 `workitem.charge_exists`; changes are only via Manager correction (AC-FIN-017), never for an ortho Total.

**Test level:** API

### AC-CLN-008 — Per-Doctor priced work

**Traces:** FR-CLN-008
**Type:** Financial / Validation / Negative
**Task:** 3

**Given**
- A case: Dr. A prices 3,000.00 in Visit 1; Dr. B continues the case and prices 2,000.00 in Visit 2; Dr. C continues in Visit 3 without entering a price.

**When**
- (a) The three Visits are finalized.
- (b) A request contains `separatelyPricedWork`.
- (c) A second price is entered on an orthodontic case.

**Then**
- (a) Two Treatment charges exist: 3,000.00 owned by Dr. A and 2,000.00 owned by Dr. B; Visit 3 creates no work item and no charge; no automatic revenue sharing.
- Each priced treatment record has exactly one work item (unique `treatment_record_id`).
- (b) 400; the flag exists in no column or OpenAPI property (see AC-NEG-017).
- (c) 422 (single work item / charge per ortho case).

**Test level:** Integration / API

### AC-CLN-009 — Doctor percentage snapshot on work

**Traces:** FR-CLN-009
**Type:** Financial / Privacy
**Task:** 3, 4

**Given**
- D1 at 40 % creates a work item; the Manager then changes D1 to 50 %.

**When**
- The Visit is finalized, payments are allocated and payroll is drafted.

**Then**
- The work item and the charge carry `doctor_percentage_snapshot` = 40 %; every entitlement on it uses 40 %.
- Payroll does not look up `current_percentage`.
- The snapshot never appears in any Sales response.

**Test level:** Integration / DB

### AC-CLN-010 — Consultation Fee

**Traces:** FR-CLN-010
**Type:** Positive / Validation / Permission / Financial
**Task:** 3, 4

**Given**
- D1's own Visit before finalization.

**When**
- (a) D1 enters a consultation fee of 200.00.
- (b) D1 enters a negative fee.
- (c) The Manager enters a fee.
- (d) The Visit is finalized with a discount of 50.00 on the consultation line.

**Then**
- (a) Stored; audited.
- (b) Rejected.
- (c) 403 (see AC-NEG-023).
- (d) A separate Consultation charge of 150.00 is established; it creates no Doctor entitlement (AC-FIN-019) and is reported separately; later changes are only Manager charge adjustments.

**Test level:** API / Integration

### AC-CLN-011 — Treatment case lifecycle

**Traces:** FR-CLN-011
**Type:** Validation / Negative / Financial
**Task:** 3

**Given**
- A Planned case.

**When**
- (a) The case moves Planned → Active → Completed.
- (b) `OnHold` or `Abandoned` is submitted.
- (c) Sales reads the clinical summary.

**Then**
- (a) Each change is audited A+; `completed_at` is set on completion; completion establishes no charge.
- (b) Rejected; no such enum value exists (see AC-NEG-031).
- (c) The status is visible through the whitelist summary.

**Test level:** API / DB

### AC-CLN-012 — Finalize a Visit

**Traces:** FR-CLN-012
**Type:** Positive / Validation / Financial / Permission
**Task:** 3, 4

**Given**
- D1's Visit with two priced work items (3,000.00 with discount 500.00; 1,000.00), a consultation fee 200.00, a linked In Treatment appointment, and satisfied Fixed Lab / ortho preconditions.

**When**
- (a) D1 finalizes.
- (b) D1 finalizes again.
- (c) D2 finalizes D1's Visit.
- (d) A Fixed-model case lacks its initial Fixed Lab; separately an ortho Deposit exceeds the Effective Total.
- (e) A fault is injected in the feedback-request step.

**Then**
- (a) In one transaction: Visit Finalized with `finalized_at` = actual end; Treatment charges 2,500.00 and 1,000.00 and a Consultation charge 200.00 with discount snapshots; appointment Completed with `visit_ended_at`; feedback request created; audited for finalize and each charge.
- No Sales or Manager approval step exists.
- (b) Idempotent; no duplicate charge or request.
- (c) 404.
- (d) 422 `fixed_lab.initial_required`; 422 `ortho.deposit_exceeds_effective_total`.
- (e) Everything rolls back: no charge, status change or request remains.

**Test level:** Integration

### AC-CLN-013 — Finalized Visit protection and read breadth

**Traces:** FR-CLN-013
**Type:** Negative / Permission / Audit
**Task:** 3

**Given**
- D1's finalized Visit with treatment records, steps, measurements, prescription items and notes.

**When**
- (a) D2 reads the patient's visits, the Visit and the case.
- (b) D1 or the Manager writes to any Visit-owned record via normal routes; a direct DB update is attempted.
- (c) D2 writes to D1's record.
- (d) A reopen route is called.

**Then**
- (a) Full clinical history returned; each Doctor record open is audited.
- (b) 422 `visit.finalized`; the DB update is rejected by guard G1.
- (c) 404.
- (d) No reopen route exists (see AC-NEG-027).

**Test level:** API / DB

### AC-CLN-014 — Controlled correction of own finalized Visit

**Traces:** FR-CLN-014
**Type:** Positive / Permission / Audit
**Task:** 3

**Given**
- D1's finalized Visit with a treatment record and notes.

**When**
- (a) D1 submits a correction for VisitNotes with changed fields and a reason.
- (b) D2 submits a correction to D1's Visit.
- (c) The Manager or Sales submits a correction.
- (d) D1 includes price, discount or consultation fee fields.

**Then**
- (a) The change is applied; one `visit_corrections` row per corrected record and an audit entry with previous and new values are written in the same transaction; the Visit stays Finalized.
- (b) 404.
- (c) 403 (see AC-NEG-027).
- (d) Rejected.
- `visit_corrections` has a composite FK to the Visit's own Doctor.

**Test level:** API / DB

### AC-CLN-015 — Sales clinical summary whitelist

**Traces:** FR-CLN-015
**Type:** Privacy / Positive
**Task:** 3

**Given**
- A patient with a Root Canal case (notes, teeth, steps, measurements), a prescription, a referral and an ortho case; canary values in every clinical field.

**When**
- Sales calls `GET /api/sales/patients/{id}/clinical-summary`.

**Then**
- Per case: procedure name, treating Doctor, status, referral destination, referral reason, follow-up instructions, price, discount, total, paid, remaining and ortho installments.
- No notes, teeth / details, steps, measurements, medications, Doctor percentage, shares or lab values appear; canary scan of the response is clean (see AC-NEG-035).

**Test level:** API / Security

## 3.9 RX

### AC-RX-001 — Write a prescription

**Traces:** FR-RX-001
**Type:** Positive / Validation / Privacy
**Task:** 3

**Given**
- D1's editable Visit.

**When**
- (a) D1 writes a prescription with general notes and two items (name, dosage, frequency, duration, instructions, order).
- (b) D1 removes one item via update with `If-Match`.
- (c) D1 updates after the Visit is finalized.

**Then**
- (a) Prescription linked Patient → Visit → Doctor; audited.
- (b) The item gets `removed_at`; it is not deleted.
- (c) 404 (use the correction route).
- Medication details never appear in any Sales response.

**Test level:** API

### AC-RX-002 — View all prescriptions

**Traces:** FR-RX-002
**Type:** Positive / Permission / Privacy
**Task:** 3

**Given**
- A patient with prescriptions from D1 and D2.

**When**
- (a) D1 lists and opens them.
- (b) D1 writes to D2's prescription.
- (c) Sales calls either read route.

**Then**
- (a) All prescriptions are readable.
- (b) 404.
- (c) 403 (see AC-NEG-035).

**Test level:** API

### AC-RX-003 — Print prescription

**Traces:** FR-RX-003
**Type:** Positive / Privacy / Permission
**Task:** 3

**Given**
- A prescription for a patient with phone, WhatsApp, email and address canaries.

**When**
- (a) D1 calls `GET /api/doctor/prescriptions/{id}/print`.
- (b) Sales calls the route.

**Then**
- (a) A PDF is generated on demand (not stored) with clinic name, logo, clinic contact, patient name, age, Doctor name, specialty, date, medications (dose, frequency, duration, instructions) and signature / stamp area, in Arabic application text.
- The PDF text contains no patient contact canary; PDF metadata has no patient identifier; no QR / barcode with patient data; the file name has no patient name.
- Each print is audited.
- (b) 403.

**Test level:** API / Security

### AC-RX-004 — Controlled correction of own finalized prescription

**Traces:** FR-RX-004
**Type:** Positive / Permission / Audit
**Task:** 3

**Given**
- D1's prescription on a finalized Visit.

**When**
- (a) D1 corrects an item.
- (b) D2 corrects it.
- (c) Sales or the Manager corrects it.

**Then**
- (a) Applied with a `visit_corrections` row (Prescription / PrescriptionItem) and an audit entry flagged `after_finalization` holding the previous version; the Visit is not reopened; no approval step.
- (b) 404.
- (c) Denied (403; see AC-NEG-027).

**Test level:** API

## 3.10 REF

### AC-REF-001 — Doctor creates a referral

**Traces:** FR-REF-001
**Type:** Positive / Validation
**Task:** 3

**Given**
- D1's own Visit.

**When**
- (a) D1 creates a referral of type Implant with reason, notes and follow-up instructions.
- (b) A type outside Surgery, Implant, Orthodontics, Prosthetics, Other is submitted.

**Then**
- (a) Referral stored with patient, referring Doctor, type, reason, notes, instructions and status New; the New Doctor Referral notification is enqueued in the same transaction; audited.
- (b) Rejected.
- The referral is visible to Sales without `notes`.

**Test level:** API / Integration

### AC-REF-002 — Doctor / Manager referral view

**Traces:** FR-REF-002
**Type:** Positive / Privacy
**Task:** 3

**Given**
- A patient referral with Sales follow-up work attached.

**When**
- A Doctor calls `GET /api/doctor/patients/{id}/referrals`.

**Then**
- `DoctorReferralDto` with id, type, reason, notes, status and created at.
- No Sales follow-up work, Original Sales Employee, patient source or contact data.
- A Sales caller receives 403 (see AC-NEG-036).

**Test level:** API

### AC-REF-003 — Sales referral view: reason yes, notes never

**Traces:** FR-REF-003
**Type:** Privacy / Positive
**Task:** 3

**Given**
- A referral with reason "R-canary" and notes "N-canary".

**When**
- (a) Sales lists and opens referrals.
- (b) Sales sends a request containing `notes`.
- (c) The Sales OpenAPI document, Sales error bodies, exports, notifications and logs are scanned.

**Then**
- (a) `SalesReferralDto` with id, patient, type, reason "R-canary", status and follow-up instructions; no visibility setting can hide reason or type.
- (b) 400 (unknown member).
- (c) "N-canary" and a `notes` property appear nowhere (see AC-NEG-036).

**Linked frozen tests:** SEC #64
**Test level:** API / Security

### AC-REF-004 — Referral follow-up status

**Traces:** FR-REF-004
**Type:** Positive / Validation
**Task:** 3

**Given**
- A referral in status New and a Sales follow-up row with `referral_id`.

**When**
- (a) Sales sets status Contacted, Booked, Completed, Lost with `If-Match`.
- (b) A value outside these (e.g. `FollowedUp`) is submitted.

**Then**
- (a) Each accepted; audited.
- (b) Rejected.
- The follow-up row keeps its own four-status field per AC-CRM-006, independent of referral status.

**Test level:** API

## 3.11 FIN

### AC-FIN-001 — Charges are established at Visit finalization

**Traces:** FR-FIN-001
**Type:** Financial / Positive
**Task:** 3, 4

**Given**
- D1's Visit with a priced work item 3,000.00 (current discount 500.00 set by Sales), a consultation fee 200.00, and a second Visit with a priced item but no fee.

**When**
- (a) Before finalization, charges are queried.
- (b) D1 finalizes both Visits.

**Then**
- (a) No charge exists for either Visit.
- (b) First Visit: one Treatment charge (original 3,000.00, discount 500.00, final 2,500.00, discount setter / role / time copied) and one Consultation charge (200.00); `charged_at` = finalize time. Second Visit: one Treatment charge only (no Consultation charge).
- One charge per work item; one Consultation charge per Visit at most.
- No Sales or Manager approval step is required; charges are audited.
- Finance never calls Clinical (architecture test: no Finance → Clinical dependency).

**Test level:** Integration

### AC-FIN-002 — Charge content and immutability

**Traces:** FR-FIN-002
**Type:** Financial / DB
**Task:** 4

**Given**
- An established Treatment charge and a Consultation charge.

**When**
- (a) Their columns are read.
- (b) An UPDATE or DELETE on `finance.patient_charges` is attempted as `clinic_app`.
- (c) A Treatment charge without work item / performing Doctor / snapshot, a Consultation charge with them, or a charge with discount > price is inserted.
- (d) Sales reads the charge.

**Then**
- (a) Treatment: patient, kind, case, work item, Visit, performing Doctor, percentage snapshot, lab model, original price, discount, final price = original − discount, discount setter / role / time, charged at. Consultation: no work item, Doctor or snapshot.
- (b) Rejected (insert-only).
- (c) Rejected by CHECK.
- (d) Only price, discount and total are visible.
- Later changes exist only as adjustment rows (AC-FIN-017) or ortho terms edits (AC-ORT-008).

**Test level:** DB / API

### AC-FIN-003 — Derived Billed, Collected, Outstanding

**Traces:** FR-FIN-003
**Type:** Financial
**Task:** 4

**Given**
- A 10,000.00 charge.

**When**
- (a) A 3,000.00 payment is allocated to it.
- (b) A further 9,000.00 payment allocates 7,000.00 and leaves 2,000.00 as credit.

**Then**
- (a) Billed 10,000.00, Collected 3,000.00, Outstanding 7,000.00, Credit 0.00.
- (b) Collected 10,000.00, Outstanding 0.00, Credit 2,000.00; Cash received 12,000.00 = Collected 10,000.00 + Credit 2,000.00.
- No stored balance column exists in any schema; figures are derived (adjustments included in Billed; reversals / refunds netted in Collected).

**Test level:** Integration / DB

### AC-FIN-004 — Record a payment

**Traces:** FR-FIN-004
**Type:** Financial / Validation / Permission
**Task:** 4

**Given**
- A patient with charge C1 Outstanding 2,000.00 and charge C2 Outstanding 1,000.00; Sales holding `Finance.Payments.Record`.

**When**
- (a) Sales posts 2,500.00 Cash with allocations C1 2,000.00 and C2 500.00 and an `Idempotency-Key`.
- (b) The same request is replayed with the same key; then with a different amount and the same key.
- (c) No key is sent; the method is not one of Cash, Visa, InstaPay; the amount is 0.
- (d) A Doctor posts a payment.
- (e) A fault is injected in the distribution step.
- (f) A `paid_at` value is supplied.

**Then**
- (a) One transaction inserts the Receipt (`paid_at` = server time), both allocations and runs distribution; response `PaymentConfirmationDto` has payment id, amount, method, paid at, allocations, credit added 0.00, resulting outstanding (C1 0.00, C2 500.00) and credit balance; audited.
- (b) Same result returned, no duplicate rows; then 409 `payment.idempotency_mismatch`.
- (c) 400; rejected; rejected.
- (d) 403.
- (e) Everything rolls back.
- (f) Rejected (no backdating).
- No printed receipt is produced (see AC-NEG-009); no insurance field exists (see AC-NEG-007).

**Test level:** API / Integration

### AC-FIN-005 — Explicit payment allocations

**Traces:** FR-FIN-005
**Type:** Financial / Validation
**Task:** 4

**Given**
- Older charge C1 Outstanding 1,000.00 and newer charge C2 Outstanding 1,000.00.

**When**
- (a) A 1,000.00 payment explicitly allocates to C2 only.
- (b) An allocation of 1,000.01 to a charge with Outstanding 1,000.00.

**Then**
- (a) C2 is collected; C1 remains Outstanding 1,000.00 (no oldest-first); the allocation has `allocation_source = Payment`, `allocated_at` = server time.
- (b) 422 `payment.exceeds_outstanding`; nothing written.
- Allocation rows are insert-only.

**Test level:** API / DB

### AC-FIN-006 — Overpayment and advance payment become Patient Credit

**Traces:** FR-FIN-006
**Type:** Financial
**Task:** 4

**Given**
- A patient with Outstanding 3,000.00, and another patient with no charge.

**When**
- (a) 5,000.00 is paid with an allocation of 3,000.00.
- (b) The second patient pays 1,000.00 with no allocation.

**Then**
- (a) 3,000.00 allocated; a `FromPayment` (+) credit entry of 2,000.00; response `creditAdded` = 2,000.00.
- (b) A `FromPayment` credit entry of 1,000.00; no allocation, entitlement, distribution or commission row.
- Credit persists for later charges; audited.

**Test level:** Integration

### AC-FIN-007 — Apply Patient Credit to a charge

**Traces:** FR-FIN-007
**Type:** Financial / Validation / Permission
**Task:** 4

**Given**
- Credit entries: 1,000.00 from payment P1 (older) and 1,500.00 from payment P2; a General-lab Treatment charge 2,000.00 Outstanding, Doctor 40 %, Lab 15 %.

**When**
- (a) Sales applies 1,800.00 credit with an `Idempotency-Key`.
- (b) Sales applies more credit than the balance.
- (c) Sales applies an amount above the charge's Outstanding.
- (d) A Doctor calls the route.

**Then**
- (a) Two slices: 1,000.00 funded by P1 and 800.00 funded by P2; each has an allocation (`allocation_source = Credit`, `payment_id` = funding payment, `allocated_at` = now) and an `AppliedToCharge` (−) entry; distribution runs at application time (Doctor 720.00, Lab 270.00, Clinic 810.00); remaining credit 700.00; audited.
- (b) 422 `credit.insufficient`.
- (c) 422.
- (d) 403.

**Test level:** Integration / API

### AC-FIN-008 — Patient Credit is a liability, never revenue

**Traces:** FR-FIN-008
**Type:** Financial
**Task:** 4

**Given**
- A Lead-sourced patient with 2,000.00 unapplied credit.

**When**
- Revenue, Billed, entitlement, lab, clinic and Sales-commission figures are computed; then any operation would make credit negative.

**Then**
- The unapplied 2,000.00 adds nothing to Billed, Doctor entitlement / share, Lab, Clinic or Sales commission.
- It appears as a memo line (liability).
- An operation that would make credit negative is rejected (checked under the patient lock), including two concurrent applications exceeding the balance.
- Sales and Manager can read the credit balance and entries.

**Test level:** Integration

### AC-FIN-009 — Payment conservation

**Traces:** FR-FIN-009
**Type:** Financial / DB
**Task:** 4

**Given**
- The G8 deferred trigger.

**When**
- (a) A transaction commits a 1,000.00 payment with allocations 600.00 and credit 400.00.
- (b) A transaction commits a payment whose allocations + credit ≠ amount.
- (c) An allocation would push a charge's net allocated amount above Billed or below 0.

**Then**
- (a) Commits.
- (b), (c) The transaction is rejected at commit.

**Test level:** DB

### AC-FIN-010 — Patient balance, payment history, outstanding list

**Traces:** FR-FIN-010
**Type:** Positive / Privacy / Permission
**Task:** 4

**Given**
- A patient with charges, payments, credit entries and General / Fixed lab distributions.

**When**
- (a) Sales calls balance, payments and outstanding-balances routes.
- (b) A Doctor calls them.

**Then**
- (a) Charges (price, discount, total), Billed, Collected, Outstanding, credit balance and entries, and payment history; the outstanding list feeds the dashboard and follow-ups.
- No share, percentage, entitlement or lab value appears (canary scan; see AC-NEG-039).
- (b) 403.

**Test level:** API / Security

### AC-FIN-011 — No printed receipt or invoice

**Traces:** FR-FIN-011
**Type:** Negative
**Task:** 4

**Given**
- The route table, schema and UI.

**When**
- They are inspected after a payment is recorded.

**Then**
- Payment confirmation is data only; no print / PDF route, receipt or invoice table, tax-invoice object or UI print control exists (see AC-NEG-009).

**Test level:** Security / DB / UI

### AC-FIN-012 — Manager Refund

**Traces:** FR-FIN-012
**Type:** Financial / Permission / Audit
**Task:** 4

**Given**
- A payment P of 3,000.00: 2,000.00 allocated to a General-lab charge and 1,000.00 unapplied credit.

**When**
- (a) The Manager refunds 500.00 from the allocation and 1,000.00 from credit, with reason and `Idempotency-Key`.
- (b) The Manager refunds 1,600.00 from the allocation.
- (c) Doctor or Sales calls the route.

**Then**
- (a) A `payment_corrections` header (Refund) and a negative `payments` row (`kind = Refund`) with a −500.00 allocation and a −1,000.00 credit entry; the original rows are unchanged; Collected and Cash received drop; the credit refund has no distribution effect; the allocation refund propagates per AC-FIN-016; audited with reason.
- (b) 422 (exceeds the remaining refundable 1,500.00).
- (c) 403.

**Test level:** Integration / API

### AC-FIN-013 — Manager Payment Reversal

**Traces:** FR-FIN-013
**Type:** Financial / Permission
**Task:** 4

**Given**
- A payment P with allocations to a Treatment charge and an Ortho charge (applied to two installments) and remaining unapplied credit.

**When**
- (a) The Manager reverses P with reason and key.
- (b) The Manager reverses P again.
- (c) Doctor or Sales calls the route.

**Then**
- (a) Header (Reversal); negative `payments` row with `reverses_payment_id` = P; a negative allocation for every allocation of P; negative credit entries for its remaining credit; negative `installment_payments` rows for the ortho applications; P is neither edited nor deleted; propagation per AC-FIN-016; audited.
- (b) Rejected (nothing remains).
- (c) 403.

**Test level:** Integration

### AC-FIN-014 — Manager Reallocation

**Traces:** FR-FIN-014
**Type:** Financial / Permission
**Task:** 4

**Given**
- 1,000.00 of payment P allocated to D1's charge (40 %) instead of D2's charge (50 %).

**When**
- (a) The Manager reallocates it with reason and key.
- (b) The Manager moves 200.00 from an allocation to credit.
- (c) Doctor or Sales calls the route.

**Then**
- (a) A −1,000.00 allocation on D1's charge and a +1,000.00 allocation on D2's charge (same payment); Cash received unchanged; D1 entitlement −400.00, D2 entitlement +500.00; audited.
- (b) Negative allocation plus a `FromReallocation` credit entry of 200.00.
- (c) 403.

**Test level:** Integration

### AC-FIN-015 — Manager Payment Replacement

**Traces:** FR-FIN-015
**Type:** Financial / Permission
**Task:** 4

**Given**
- A payment recorded as 1,000.00 Cash that was actually 1,100.00 Visa.

**When**
- (a) The Manager replaces it with reason and key.
- (b) Doctor or Sales calls the route.

**Then**
- (a) One correction contains a full reversal of the original and a new 1,100.00 Visa Receipt; the original remains in history; net effect equals the corrected payment; audited.
- (b) 403.

**Test level:** Integration

### AC-FIN-016 — Correction propagation

**Traces:** FR-FIN-016
**Type:** Financial
**Task:** 4

**Given**
- A General-lab allocation of 2,000.00 recorded at Lab 15 % (Doctor 40 %) whose Doctor payroll month is finalized; Lab is now 20 %; a Fixed-lab case with collections; a Lead-sourced patient whose original commission item was at 5 % (rate now 7 %).

**When**
- The Manager reverses 1,000.00 of the General-lab allocation, and reverses a Fixed-lab allocation.

**Then**
- General lab: delta rows negate exactly at the recorded 15 %: Doctor −400.00, Lab −150.00, Clinic −450.00.
- Fixed lab: `Reconcile(case)` inserts only delta rows.
- Sales commission: a negative item at 5 % (−50.00), not 7 %.
- Negative Doctor earnings have `recognized_at` = now and reduce the current open payroll; the finalized payroll is unchanged.
- Patient Credit adjusts where the correction touches credit.
- Ledger identities (G8, G8b) hold after commit; audited.

**Test level:** Integration

### AC-FIN-017 — Manager price / discount correction of a non-ortho charge

**Traces:** FR-FIN-017
**Type:** Financial / Validation / Permission
**Task:** 4

**Given**
- A Treatment charge final 2,500.00 with Collected 1,000.00 and existing distributions; an Ortho charge.

**When**
- (a) The Manager posts a `DiscountCorrection` of −300.00 with reason and key.
- (b) The Manager posts a `PriceCorrection` making Billed 900.00 (below Collected 1,000.00).
- (c) The Manager posts any adjustment on the Ortho charge.
- (d) A client posts kind `OrthoTermsChange`.
- (e) Doctor or Sales calls the route.

**Then**
- (a) An insert-only `charge_adjustments` row with signed `amount_delta` −300.00 and reason; Billed 2,200.00; earlier distributions unchanged; audited.
- (b) 422.
- (c) 422 `ortho.use_terms_edit`.
- (d) Rejected.
- (e) 403.

**Test level:** API / Integration

### AC-FIN-018 — Doctor entitlement on every collection

**Traces:** FR-FIN-018
**Type:** Financial / Privacy
**Task:** 4

**Given**
- A Treatment charge with snapshot 40 % (Doctor's current percentage now 50 %) and Fixed Lab L = 3,000.00.

**When**
- An allocation of 333.33 is made.

**Then**
- One `doctor_entitlements` row = round_half_up(333.33 × 40 / 100, 2) = 133.33.
- The snapshot (40 %) is used, not 50 %; the lab is not removed before the percentage is applied.
- A later lab revision does not change this entitlement.
- Entitlements never appear in a Sales response.

**Test level:** Integration

### AC-FIN-019 — Consultation charges produce no Doctor commission

**Traces:** FR-FIN-019
**Type:** Financial
**Task:** 4

**Given**
- A Consultation charge 200.00 for a Lead-sourced patient whose Sales rate is 5 %.

**When**
- 200.00 is allocated to it.

**Then**
- No entitlement, earning or distribution row is created; the amount is Clinic revenue on its own report line.
- A Sales commission item of 10.00 is created (consultation counts in the commission base).

**Test level:** Integration

### AC-FIN-020 — Distribution identity and non-negative Clinic Share

**Traces:** FR-FIN-020
**Type:** Financial / DB
**Task:** 4

**Given**
- The G8b deferred trigger.

**When**
- (a) Any distribution event commits (general, fixed, correction, revision).
- (b) A transaction attempts lab + clinic + earnings ≠ basis, earnings > entitlements, negative earnings sum per allocation, or a negative cumulative Clinic Share for an allocation.

**Then**
- (a) Σ(lab + clinic) + Σ Doctor earnings = event basis; per allocation 0 ≤ Σ earnings ≤ Σ entitlements; Clinic Share is the half-up-rounded remainder.
- (b) Rejected at commit.

**Test level:** DB / Integration

### AC-FIN-021 — Fixed-EGP Discount before finalization

**Traces:** FR-FIN-021
**Type:** Financial / Validation / Permission
**Task:** 3, 4

**Given**
- D1's work-item line priced 3,000.00, a consultation line 200.00 and an ortho line (Gross 20,000.00), none finalized.

**When**
- (a) D1 sets 500.00 on own work line; Sales then sets 600.00; the Manager (via the Sales route) sets 700.00.
- (b) A percentage input (e.g. `discountPercent` or `"10%"`) is sent.
- (c) A discount of 3,000.01 is sent.
- (d) D2 sets a discount on D1's line.
- (e) The Visit is finalized, then a discount is sent.
- (f) Sales sets 50.00 on the consultation line and 2,000.00 on the ortho line.

**Then**
- (a) Three `pricing_discount_entries` rows (amount, actor, role `DOCTOR` / `SALES_RECEPTION` / `MANAGER`, time); current = highest `entry_seq` = 700.00; Total = 2,300.00; audited.
- (b) 400 (see AC-NEG-020).
- (c) 422 `discount.exceeds_price`.
- (d) 404.
- (e) 422.
- (f) Accepted; ortho Effective Total = 18,000.00 (AC-ORT-003).

**Test level:** API

### AC-FIN-022 — Sales pricing view without clinical data

**Traces:** FR-FIN-022
**Type:** Privacy / Positive
**Task:** 3, 4

**Given**
- A patient with a priced unfinalized work line, an unpriced treatment record, a finalized line, and clinical canaries.

**When**
- Sales calls `GET /api/sales/pricing-lines?patientId=`.

**Then**
- Only the priced, unfinalized line is returned with pricing-line id, kind, patient {id, code, name}, procedure name, treating Doctor {id, name}, price, discount, total, `entrySeq`, last set by {role, at}.
- No clinical notes, teeth / details, steps, measurements, prescriptions, lab, Doctor percentage, entitlement, shares, commission or Doctor compensation (canary scan; see AC-NEG-035, AC-NEG-039).

**Test level:** API / Security

### AC-FIN-023 — Discount concurrency

**Traces:** FR-FIN-023
**Type:** Validation
**Task:** 3, 4

**Given**
- A line with `entrySeq` = 2.

**When**
- Doctor and Sales concurrently set discounts with `expectedEntrySeq` = 2; separately, a set-discount races with finalization.

**Then**
- Exactly one discount write succeeds; the other returns 409 `discount.stale_entry`.
- Discount and finalization serialize on the Visit row lock; the charge snapshot equals the discount committed before finalization.

**Test level:** Integration

### AC-FIN-024 — Discount snapshot at finalization; Manager-only afterwards

**Traces:** FR-FIN-024
**Type:** Financial / Permission
**Task:** 4

**Given**
- A line with current discount 700.00 set by the Manager.

**When**
- (a) The Visit is finalized.
- (b) Doctor or Sales then attempts a discount change.
- (c) The Manager posts a `DiscountCorrection`.

**Then**
- (a) The charge holds discount 700.00 with setter, role `MANAGER` and time.
- (b) 403 / 422.
- (c) Accepted per AC-FIN-017 (refused for ortho charges).
- Commission and Doctor share follow collected money; no separate discount formula exists.

**Test level:** Integration

### AC-FIN-025 — Price may not drop below the existing discount

**Traces:** FR-FIN-025
**Type:** Validation
**Task:** 3, 4

**Given**
- A line priced 3,000.00 with discount 1,000.00.

**When**
- (a) The Doctor lowers the price to 900.00.
- (b) The discount ≤ price invariant is violated at finalization by any path.

**Then**
- (a) 422; price unchanged.
- (b) Finalization is refused (G6).

**Test level:** API / DB

### AC-FIN-026 — Ownership stays with the performing Doctor

**Traces:** FR-FIN-026
**Type:** Financial
**Task:** 4

**Given**
- A charge performed by D1 at 40 % on a patient now assigned to D2.

**When**
- Collections, corrections and reports are processed.

**Then**
- Every entitlement and earning is for `performing_doctor_id` = D1 at 40 %; none for D2.

**Test level:** Integration

### AC-FIN-027 — Reassignment never moves money

**Traces:** FR-FIN-027
**Type:** Financial / Negative
**Task:** 4

**Given**
- Patient reassigned from Dr. A to Dr. B after Dr. A's work was charged.

**When**
- A payment is allocated to Dr. A's charge.

**Then**
- Entitlement and earning go to Dr. A immediately; nothing is delayed, held or altered.
- The work stays in Dr. A's own-work view and does not appear in Dr. B's.
- No hold object exists (see AC-NEG-016).

**Test level:** Integration

### AC-FIN-028 — Earning belongs to the payroll month of the collection

**Traces:** FR-FIN-028
**Type:** Financial
**Task:** 4

**Given**
- General-lab work finalized in January; a Fixed-lab case with pending entitlement from February; January payroll finalized.

**When**
- (a) Payment on the January work is collected in March.
- (b) A March collection settles the February pending entitlement.

**Then**
- (a) `recognized_at` is in March; the earning is in March payroll.
- (b) The settled amount is recognized in March.
- No earning ever lands in a finalized month.

**Test level:** Integration

### AC-FIN-029 — Doctor own-work financial view

**Traces:** FR-FIN-029
**Type:** Privacy / Positive
**Task:** 4

**Given**
- D1 and D2 each have work on the same case; D1 has a Fixed-lab item.

**When**
- (a) D1 calls `GET /api/doctor/me/work-items` and opens an item.
- (b) D1 opens D2's item id; D1 passes a Doctor id parameter.
- (c) The Manager calls the own route.

**Then**
- (a) One item per D1 work item with case, Visit, procedure, original price, discount, final price, collected, remaining, Doctor percentage, entitled, recognized, pending entitlement, Clinic Share, lab; Fixed-lab item also "lab covered so far".
- No other Doctor's revenue or percentage, case / patient / clinic totals, profit, expenses, Sales payroll or management report data.
- (b) 404; parameter rejected / ignored.
- (c) 404 (see AC-NEG-038).

**Test level:** API / Security

### AC-FIN-030 — Inactive Doctor keeps entitlement on earlier work

**Traces:** FR-FIN-030
**Type:** Financial
**Task:** 4

**Given**
- D1 deactivated with an Outstanding charge from earlier work.

**When**
- A payment is allocated to it in April and the Manager creates April Doctor payroll drafts.

**Then**
- Entitlement and earning are created as usual for D1 in April.
- A payroll draft is created for D1 (payroll does not depend on `is_active`).

**Test level:** Integration

### AC-FIN-031 — No attribution holds

**Traces:** FR-FIN-031
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table and event-handler registry.

**When**
- They are inspected; a patient is reassigned and a Doctor is deactivated.

**Then**
- No hold / pending-attribution table, `PatientReassigned` / `DoctorDeactivated` finance handler, or reassignment-triggered finance route exists; Finance subscribes to no events.
- Neither action creates any finance row (see AC-NEG-016).

**Test level:** DB / Security

## 3.12 LAB

### AC-LAB-001 — General lab model applicability

**Traces:** FR-LAB-001
**Type:** Financial
**Task:** 4

**Given**
- Cases of each procedure type.

**When**
- Charges are established.

**Then**
- `lab_model` is Fixed for Implant, Fixed Prosthetics and Removable Prosthetics and General for every other type, including Orthodontics; copied procedure type → case snapshot → charge.

**Test level:** Integration / DB

### AC-LAB-002 — General lab distribution at the allocation-time rate

**Traces:** FR-LAB-002
**Type:** Financial
**Task:** 4

**Given**
- Case A: General-lab charge 10,000.00, Doctor 40 %, Lab 15 %; a second charge with Doctor 40 %, Lab 20 %.

**When**
- (a) 2,000.00 is collected on Case A.
- (b) 100.00 is collected on the second charge; Lab changes to 15 %; another 100.00 is collected.
- (c) Credit is applied after a further lab % change.

**Then**
- (a) In one event: Doctor earning 800.00 (recognized immediately), Lab 300.00, Clinic 900.00.
- (b) First: 40.00 + 20.00 + 40.00; second: 40.00 + 15.00 + 45.00; the first distribution is unchanged.
- (c) The lab % in force at credit-application time is used.
- Each distribution row records the lab % applied; rows are insert-only; lab data never reaches Sales.

**Test level:** Integration

### AC-LAB-003 — Configure the General Lab percentage

**Traces:** FR-LAB-003
**Type:** Validation / Permission
**Task:** 4

**Given**
- A Doctor at 85 % and a charge snapshotting 80 % with Outstanding > 0.

**When**
- (a) The Manager sets Lab 15 %.
- (b) The Manager sets Lab 16 % (85 + 16 > 100).
- (c) Doctor or Sales calls the route.

**Then**
- (a) A new history row effective now; previous rows unchanged; current = latest.
- (b) 422.
- (c) 403.

**Test level:** API

### AC-LAB-004 — General-lab corrections use the recorded rate

**Traces:** FR-LAB-004
**Type:** Financial
**Task:** 4

**Given**
- A 2,000.00 allocation distributed at Lab 15 % (Doctor 800.00, Lab 300.00, Clinic 900.00); Lab now 25 %.

**When**
- 2,000.00 is refunded.

**Then**
- Delta rows: Doctor −800.00, Lab −300.00, Clinic −900.00 (recorded 15 %, never 25 %); identity restored; audited.

**Test level:** Integration

### AC-LAB-005 — One Fixed Lab obligation per case

**Traces:** FR-LAB-005
**Type:** Validation / Privacy
**Task:** 4

**Given**
- A Fixed-model case with Fixed Lab revision 1 by D1; D2 also performs work on the case; a General-model case.

**When**
- (a) D2 creates a Fixed Lab for the same case.
- (b) A Fixed Lab is created for the General-model case.
- (c) Sales reads any surface.

**Then**
- (a) 409 `fixed_lab.already_exists`.
- (b) Rejected (G7(a)).
- One obligation L per case, attached to no charge, payment or Doctor.
- (c) No lab data appears (see AC-NEG-039).

**Test level:** API / DB

### AC-LAB-006 — Doctor enters the initial Fixed Lab

**Traces:** FR-LAB-006
**Type:** Positive / Validation / Permission
**Task:** 3, 4

**Given**
- A Fixed-model case on which D1 performs priced work, with no Fixed Lab.

**When**
- (a) D1 posts 3,000.00.
- (b) The Manager posts the initial amount.
- (c) D1 posts again; D1 posts a negative amount.
- (d) Another case: D1 posts 0.00.

**Then**
- (a) Revision 1 (amount, D1, role `DOCTOR`, time); no distribution event; audited.
- (b) 403; DB revision-1 role CHECK rejects `MANAGER` (see AC-NEG-023).
- (c) 409; rejected.
- (d) Accepted as explicit "no lab cost".

**Test level:** API / DB

### AC-LAB-007 — Unknown Fixed Lab is never zero

**Traces:** FR-LAB-007
**Type:** Validation / Financial
**Task:** 3, 4

**Given**
- A Visit containing the case's first Fixed-lab priced work and no revision 1.

**When**
- The Doctor finalizes.

**Then**
- 422 `fixed_lab.initial_required`; no charge, entitlement or distribution exists; L is not defaulted to 0.
- The waterfall refuses to run without revision 1 (G7(b)).

**Test level:** Integration / DB

### AC-LAB-008 — Lab-First waterfall (Owner example)

**Traces:** FR-LAB-008
**Type:** Financial
**Task:** 4

**Given**
- Fixed Lab L = 3,000.00; Doctor percentage 40 %; treatment charge 10,000.00.

**When**
- Collections of 2,000.00, 3,000.00 and 5,000.00 are allocated in that order.

**Then**
- P1 (2,000.00): Lab 2,000.00; entitlement 800.00 accrues and is pending; Doctor recognized 0.00; Clinic 0.00.
- P2 (3,000.00): Lab 1,000.00 (L funded); new entitlement 1,200.00; pending 800.00 + 1,200.00 = 2,000.00 recognized; Clinic 0.00.
- P3 (5,000.00): Lab 0.00; entitlement 2,000.00 recognized; Clinic 3,000.00.
- Cumulative: Lab 3,000.00, Doctor 4,000.00, Clinic 3,000.00 (= 10,000.00).
- The queue is paid FIFO (oldest entitlement, then Doctor id); Clinic Share is never negative at any step.

**Test level:** Integration

### AC-LAB-009 — Pending Doctor entitlement

**Traces:** FR-LAB-009
**Type:** Financial / Privacy
**Task:** 4

**Given**
- (a) Case 1 in the state after P1 of AC-LAB-008: Fixed Lab L = 3,000.00, Doctor 40 %, collected so far 2,000.00 → Lab 2,000.00, Doctor entitlement 800.00, Doctor recognized 0.00, Clinic 0.00.
- (b) Case 2, a separate Fixed-lab case: L = 7,000.00, Doctor 40 %, Billed 10,000.00 (L > (1 − p) × Billed), no collection yet.

**When**
- (a) For Case 1, the Manager calls `GET /api/manager/finance/pending-entitlements` and D1 reads own work.
- (b) Case 2 is fully collected (10,000.00).

**Then**
- Pending = entitlement − recognized earnings in every case.
- (a) Case 1 shows pending 800.00 (800.00 − 0.00) to both the Manager and D1. It is a memo figure: not included in any payroll and not labelled Clinic profit.
- (b) Case 2 after full collection: Lab 7,000.00; Doctor total entitlement 4,000.00; Doctor recognized 3,000.00; pending 1,000.00, carried indefinitely; Clinic 0.00. The pending 1,000.00 never forces Clinic Share negative.

**Test level:** Integration / API

### AC-LAB-010 — Manager revises the Fixed Lab

**Traces:** FR-LAB-010
**Type:** Financial / Permission
**Task:** 4

**Given**
- State after P1 of AC-LAB-008 (L 3,000.00, Lab paid 2,000.00, pending 800.00); the month of P1 is finalized.

**When**
- (a) The Manager revises L to 1,000.00 with reason.
- (b) Reconcile is re-run.
- (c) The Manager raises L on another case with recognized earnings.
- (d) A Doctor calls the revise route; the Manager revises a case without revision 1.

**Then**
- (a) Revision 2 (role `MANAGER`, reason); a `FixedLabRevision` event inserts only deltas: Lab −1,000.00, Doctor +800.00 recognized now, Clinic +200.00; entitlements and the 40 % are unchanged; deltas land in the current open payroll; the finalized month is untouched; audited.
- (b) Idempotent: no further rows.
- (c) Negative earnings now, in the current open month.
- (d) 403; 409 `fixed_lab.initial_required`.

**Test level:** Integration / API

## 3.13 ORT

Reference case used below (**Case O**): Gross 20,000.00, pre-finalization Discount 2,000.00, Deposit 5,000.00, Months 5, Doctor percentage 40 %, charge date 2026-01-15.

### AC-ORT-001 — Create an Orthodontic case

**Traces:** FR-ORT-001
**Type:** Positive / Validation / Permission
**Task:** 3, 4

**Given**
- D1 holding `Orthodontics.Cases.Create` and `Orthodontics.Terms.SetInitial`, with an own editable Visit.

**When**
- (a) D1 creates Case O with area Upper Arch.
- (b) D1 creates a case with area Appliance and no appliance type.
- (c) The Manager or Sales calls the route.
- (d) A later Visit tries to add a second price to Case O.

**Then**
- (a) Case, its single work item (price = Gross 20,000.00, percentage snapshot 40 % fixed at case start) and terms revision 1 are created; **no charge and no installment exist**; audited.
- (b) 422 `catalog.option_invalid`.
- (c) 403.
- (d) 422.

**Test level:** API / DB

### AC-ORT-002 — Doctor enters initial terms (stored only)

**Traces:** FR-ORT-002
**Type:** Validation / Permission / Financial
**Task:** 3, 4

**Given**
- D1 creating an ortho case.

**When**
- (a) Terms Gross 20,000.00, Deposit 5,000.00, Months 5 are submitted.
- (b) Gross < 0, Deposit < 0, Deposit > Gross, or Months 0 is submitted.
- (c) D1 submits initial terms a second time.
- (d) The Manager or Sales submits initial terms.

**Then**
- (a) Revision 1 stored with role `DOCTOR`; nothing else (no charge, no installment) is created before finalization; audited.
- (b) Rejected.
- (c) Rejected (initial terms are entered once).
- (d) 403; revision-1 role CHECK rejects a non-Doctor role (see AC-NEG-023).

**Test level:** API / DB

### AC-ORT-003 — Effective Ortho Total

**Traces:** FR-ORT-003
**Type:** Financial
**Task:** 4

**Given**
- Case O with Discount 2,000.00 set before finalization.

**When**
- The Visit is finalized and the ortho read model and patient balance are read.

**Then**
- `grossTotal` 20,000.00, `discount` 2,000.00, `effectiveOrthoTotal` 18,000.00 = Ortho Charge Billed (charge original 20,000.00, discount 2,000.00, final 18,000.00).
- Remaining = 18,000.00 − 5,000.00 = 13,000.00.
- Outstanding never includes an extra 2,000.00 balance; the schedule balances against 18,000.00, never 20,000.00.

**Test level:** Integration

### AC-ORT-004 — Charge and initial installments at finalization

**Traces:** FR-ORT-004
**Type:** Financial / Validation
**Task:** 3, 4

**Given**
- Case O before finalization; a second case with Gross 10,000.00, Discount 0.00, Deposit 0.00, Months 3; a third case with Gross 10,000.00, Discount 2,000.00, Deposit 9,000.00.

**When**
- (a) Charges and installments are queried before finalization.
- (b) The Case O Visit is finalized on 2026-01-15; then finalize is invoked again.
- (c) The second case's Visit is finalized.
- (d) The third case's Visit is finalized.

**Then**
- (a) No charge and no installment exist.
- (b) Charge original 20,000.00 / discount 2,000.00 / final 18,000.00; exactly 5 `Generated` installments of 2,600.00; first due 2026-02-15, then monthly; 5,000.00 + 13,000.00 = 18,000.00; Deposit unchanged; the repeat finalize generates nothing (G10).
- (c) Installments 3,333.33, 3,333.33, 3,333.34 (last absorbs rounding; Σ = 10,000.00).
- (d) 422 `ortho.deposit_exceeds_effective_total` (9,000.00 > 8,000.00); nothing is written; the Visit stays unfinalized.

**Test level:** Integration / DB

### AC-ORT-005 — Ortho read model

**Traces:** FR-ORT-005
**Type:** Positive / Privacy
**Task:** 4

**Given**
- Case O finalized with one retired installment, a PriceChange installment and some applications.

**When**
- D1, D2, Sales and the Manager read the case.

**Then**
- Per installment: number, origin (Generated / ManagerAdded / PriceChange), current due date, current amount, `amountAlreadyPaid`, remaining, status, `isActive`.
- Per case: charge Collected / Outstanding, deposit paid, unapplied ortho collections, `grossTotal`, `discount`, `effectiveOrthoTotal`, `scheduleDifference`, `monthPlanDifference`, `requiresScheduleAdjustment`.
- The retired installment appears only in history and is not payable.
- Percentages / shares appear only for D1 (own work); not for D2 or Sales.
- No `applicationState: "Pending"` field exists.

**Test level:** API

### AC-ORT-006 — Manual installment payment application

**Traces:** FR-ORT-006
**Type:** Financial / Validation / Permission
**Task:** 4

**Given**
- Case O finalized; installments #1–#5 of 2,600.00 (#1 due soonest); a 9,000.00 payment allocated to the ortho charge (allocation A).

**When**
- (a) Sales applies A: 1,000.00 → #3 (future, early), 2,600.00 → #4, leaving #1 untouched.
- (b) Sales applies a further 2,000.00 of A to #3 (exceeds #3's remaining 1,600.00) or more than A's unapplied balance.
- (c) Sales applies to a retired installment.
- (d) A Doctor calls the route.

**Then**
- (a) Application rows exist (one→many, partial, early/future); #1 remains unpaid (no automatic oldest-first); #3 Partially Paid, #4 Paid; distribution already ran at allocation time and does not run again; deposit paid = min(5,000.00, 9,000.00 − 3,600.00) = 5,000.00; audited.
- (b) 422 `ortho.application_exceeds_allocation` (capped by allocation and installment current amount, G5).
- (c) Rejected.
- (d) 403.

**Test level:** Integration / API

### AC-ORT-007 — Derived installment status

**Traces:** FR-ORT-007
**Type:** Financial
**Task:** 4

**Given**
- Installments: due in the future with no payment; due today; partially paid; fully paid; past due with remaining > 0.

**When**
- The ortho read model is read on the clinic-local date.

**Then**
- Statuses are Upcoming, Due, Partially Paid, Paid, Overdue respectively; remaining 0 ⇒ Paid.
- Status changes with clinic-local date and applications without any stored status column.

**Test level:** Integration / DB

### AC-ORT-008 — Manager edits Ortho terms (the only route)

**Traces:** FR-ORT-008
**Type:** Permission / Validation / Audit
**Task:** 4

**Given**
- Case O finalized; another ortho case not yet finalized.

**When**
- (a) The Manager edits Months with reason and `If-Match`.
- (b) The Manager edits the unfinalized case.
- (c) D1 (after initial entry) or Sales calls the terms route.

**Then**
- (a) A terms revision (role `MANAGER`, reason) is inserted and the matching rule applies; audit holds actor, role, time, previous and new Gross / Deposit / Months and reason.
- (b) 422 `ortho.charge_not_established`.
- (c) 403.
- No other route changes Gross, Deposit or Months (AC-ORT-019).

**Test level:** API

### AC-ORT-009 — Total increase adds one PriceChange installment

**Traces:** FR-ORT-009
**Type:** Financial
**Task:** 4

**Given**
- Case O finalized: Billed 18,000.00; installments #1–#5 of 2,600.00, latest active due 2026-06-15; some applications exist.

**When**
- The Manager raises Gross from 20,000.00 to 24,000.00 (Δ = 4,000.00).

**Then**
- An audited terms revision; a `charge_adjustments` row `OrthoTermsChange` +4,000.00; Billed 22,000.00.
- Exactly one `PriceChange` installment of 4,000.00 due 2026-07-15.
- Installments #1–#5, applications and allocations are unchanged; nothing redistributed.
- The 2,000.00 discount is not re-applied to Δ (not 3,600.00).
- 5,000.00 + 13,000.00 + 4,000.00 = 22,000.00; `monthPlanDifference` stays 0 (PriceChange is not counted toward Months).

**Test level:** Integration

### AC-ORT-010 — Total decrease rejected in V1

**Traces:** FR-ORT-010
**Type:** Negative / Validation
**Task:** 4

**Given**
- Case O finalized with Gross 20,000.00.

**When**
- The Manager submits Gross 19,000.00; separately a direct write bypassing the handler is attempted.

**Then**
- 422 `ortho.total_decrease_not_supported`; nothing is written (no revision, adjustment, installment change or negative installment); backstop G9(b) rejects the direct write.
- This V1 behavior is linked to deferred item **Q-ORT-3b**; no decrease handling exists (see AC-NEG-021).

**Test level:** API / DB

### AC-ORT-011 — Deposit change after installments exist

**Traces:** FR-ORT-011
**Type:** Financial / Validation
**Task:** 4

**Given**
- Case O finalized (Effective 18,000.00, Deposit 5,000.00, installments Σ 13,000.00).

**When**
- (a) The Manager changes Deposit to 7,000.00.
- (b) The Manager changes Deposit to 18,000.01 or −1.

**Then**
- (a) Only a terms revision is written; installments are not regenerated, added, removed or redistributed; required Σ = 11,000.00; read model returns `requiresScheduleAdjustment: true` and `scheduleDifference` −2,000.00 until a valid batch is saved.
- (b) 422 `ortho.deposit_exceeds_effective_total`.

**Test level:** Integration

### AC-ORT-012 — Number of Months change after installments exist

**Traces:** FR-ORT-012
**Type:** Financial / Validation
**Task:** 4

**Given**
- An ortho case with 10 active regular installments (Generated / ManagerAdded) plus one PriceChange installment; 3 regular installments have payment history.

**When**
- (a) The Manager changes Months 10 → 12.
- (b) The Manager changes Months 10 → 8.
- (c) The Manager changes Months to 2 (below the 3 paid regular installments) or 0.

**Then**
- (a) Only a terms revision is written; `monthPlanDifference` = +2; `requiresScheduleAdjustment: true`.
- (b) `monthPlanDifference` = −2; nothing generated or removed.
- The PriceChange installment is excluded from the count.
- (c) 422 `ortho.months_conflict_with_paid_history` (for 2); rejected (for 0).

**Test level:** Integration

### AC-ORT-013 — Derived schedule validity flag

**Traces:** FR-ORT-013
**Type:** Financial
**Task:** 4

**Given**
- Case O finalized and valid; then Deposit changed to 7,000.00.

**When**
- The read model is read before and after the change, and after payments are applied.

**Then**
- Before: `scheduleDifference` 0.00, `monthPlanDifference` 0, `requiresScheduleAdjustment` false.
- After: `scheduleDifference` = 18,000.00 − 7,000.00 − 13,000.00 = −2,000.00; flag true.
- Payments never change either difference.
- No lifecycle status column exists for schedule validity.

**Test level:** Integration

### AC-ORT-014 — Batch: revise installment amount

**Traces:** FR-ORT-014
**Type:** Financial / Validation / Permission
**Task:** 4

**Given**
- Installment #2 of 2,600.00 with 1,000.00 applied; #5 unpaid.

**When**
- (a) The Manager revises #2 to 2,000.00 and #5 to 0.00 in a valid batch with reason and `If-Match`.
- (b) The Manager revises #2 to 999.99.
- (c) A negative amount is submitted.
- (d) Doctor or Sales calls the route.

**Then**
- (a) New `ManagerEdit` revisions; previous amounts kept in history; audited with actor, role, time, previous / new and reason.
- (b) 422 `ortho.installment_below_paid_amount`; nothing written.
- (c) 422.
- (d) 403.

**Test level:** API

### AC-ORT-015 — Batch: revise due date of an unpaid installment

**Traces:** FR-ORT-015
**Type:** Validation / Permission
**Task:** 4

**Given**
- Unpaid installment #4 and partially paid installment #2.

**When**
- (a) The Manager re-dates #4.
- (b) The Manager re-dates #2.

**Then**
- (a) New `ManagerEdit` revision; previous due date kept; audited.
- (b) 422 `ortho.installment_has_payment_history`.
- Doctor / Sales receive 403.

**Test level:** API

### AC-ORT-016 — Batch: add an installment manually

**Traces:** FR-ORT-016
**Type:** Validation / Permission
**Task:** 4

**Given**
- Months raised 5 → 6 on Case O.

**When**
- (a) The Manager adds a `ManagerAdded` installment due today or later with amount ≥ 0 and rebalances in a valid batch.
- (b) The due date is before clinic-local today.
- (c) Any other route attempts to create an installment.

**Then**
- (a) New installment with next number and revision 1 (due date, amount, Manager, time, reason); no payments; counts as active regular; audited.
- (b) 422.
- (c) No such route exists.

**Test level:** API

### AC-ORT-017 — Batch: retire an unpaid installment

**Traces:** FR-ORT-017
**Type:** Validation / Permission / Negative
**Task:** 4

**Given**
- Unpaid installment #5; installment #2 with any payment history (including a reversed application).

**When**
- (a) The Manager retires #5 in a valid batch.
- (b) The Manager retires #2.
- (c) A re-activation of #5 or an application to it is attempted; a DELETE is attempted.

**Then**
- (a) `ManagerRetire` revision (`is_active = false`, amount 0); history only; audited.
- (b) 422 `ortho.installment_has_payment_history`.
- (c) Rejected; no DELETE route or grant exists (see AC-NEG-022).

**Test level:** API / DB

### AC-ORT-018 — Batch is atomic and must end valid

**Traces:** FR-ORT-018
**Type:** Financial / Validation
**Task:** 4

**Given**
- Case O after Deposit changed to 7,000.00 (required Σ 11,000.00) and Months unchanged at 5; #1 paid 2,600.00.

**When**
- (a) Batch `{ reason, changes: [#5 → 200.00] }` (Σ = 10,600.00).
- (b) Batch retiring #5 without adding another (active regular 4 ≠ Months 5).
- (c) Batch containing one valid change and one change re-dating paid #1.
- (d) Batch revising #5 → 600.00 (Σ = 2,600 × 4 + 600 = 11,000.00).

**Then**
- (a) 422 `ortho.schedule_unbalanced`; nothing written.
- (b) 422 `ortho.month_plan_mismatch`; nothing written.
- (c) 422 `ortho.installment_has_payment_history`; the valid change is not written either.
- (d) Accepted in one transaction; Deposit 7,000.00 + Σ active 11,000.00 = Effective 18,000.00; active regular = 5; `requiresScheduleAdjustment: false`; one audited revision per change.

**Test level:** Integration

### AC-ORT-019 — Paid history protection; no generic adjustments on ortho

**Traces:** FR-ORT-019
**Type:** Negative / Financial
**Task:** 4

**Given**
- Case O with applications on #1 and #2.

**When**
- (a) The Manager posts `PriceCorrection` or `DiscountCorrection` on the ortho charge.
- (b) Terms edits and batches are run; `payment_allocations` and `installment_payments` are compared before / after.
- (c) A DELETE on installment tables or an application move between installments is attempted.

**Then**
- (a) 422 `ortho.use_terms_edit`; G9 rejects any non-terms change of an ortho total.
- (b) Allocations and applications are byte-identical; no installment is below its paid amount; no paid installment is deleted, retired or re-dated; no negative installment exists.
- (c) No DELETE grant; no route moves a payment between installments (see AC-NEG-022).

**Test level:** DB / Integration

### AC-ORT-020 — Refund / reversal / correction of ortho payments

**Traces:** FR-ORT-020
**Type:** Financial / Permission
**Task:** 4

**Given**
- An ortho allocation of 2,600.00 applied to #1.

**When**
- (a) The Manager reverses the payment.
- (b) Sales then applies a new ortho allocation to #1.

**Then**
- (a) Normal correction flow plus a negative `installment_payments` row for #1; Gross, Deposit, Months and the schedule are unchanged; #1's status derives back to unpaid / due; audited.
- (b) Accepted.
- Doctor / Sales cannot invoke the correction (403).

**Test level:** Integration

### AC-ORT-021 — Doctor and Sales cannot edit terms or schedule

**Traces:** FR-ORT-021
**Type:** Permission
**Task:** 4

**Given**
- Case O finalized.

**When**
- D1 and Sales call `PUT …/terms` and `PUT …/installments` (amounts, due dates, additions, retirements).

**Then**
- Every call returns 403; nothing written.
- Sales retains only the payment-application capability (AC-ORT-006).

**Linked frozen tests:** SEC #34, #71, #72
**Test level:** Security

## 3.14 EXP

### AC-EXP-001 — Expense categories (11-category catalogue)

**Traces:** FR-EXP-001
**Type:** Positive / Validation / Permission / Negative
**Task:** 4

**Given**
- A freshly seeded database and a Manager holding `ExpenseCategories.Manage`.

**When**
- (a) The Manager lists categories.
- (b) The Manager creates a category with a duplicate name.
- (c) The Manager deactivates and re-activates a category.
- (d) Sales or a Doctor calls any `/api/manager/expense-categories` route.

**Then**
- (a) Exactly 11 categories: (1) Water; (2) Electricity; (3) Rent; (4) Internet; (5) Maintenance; (6) Cleaning; (7) Office Supplies; (8) Materials / Medical Supplies; (9) Marketing; (10) Transportation; (11) Petty / Miscellaneous.
- `sales_can_record = true` only for Materials / Medical Supplies and Petty / Miscellaneous; false for the other nine.
- (b) Rejected.
- (c) Accepted; audited.
- (d) 403.
- Materials / Medical Supplies has no stock, quantity, warehouse, reorder or valuation attribute (see AC-NEG-001).

**Test level:** API / DB

### AC-EXP-002 — Sales records Materials / Petty expenses (create only)

**Traces:** FR-EXP-002
**Type:** Positive / Validation / Permission / Privacy
**Task:** 4

**Given**
- A Sales user holding `Expenses.CreateOperational`.

**When**
- (a) Sales posts `{ categoryId: <Materials / Medical Supplies>, amount: "850.00", specification: "Purchase of gloves and masks for the clinic" }`.
- (b) Sales posts the same to Petty / Miscellaneous.
- (c) Sales posts to Rent (or any of the other nine categories, or a deactivated allowed category).
- (d) `specification` missing or empty; `amount` 0, negative or "850.005".
- (e) The body adds any other member (e.g. `date`, `vendor`, `createdBy`, `attachment`).
- (f) Sales calls list, read, edit, delete, totals, category management or Manager expense report routes.

**Then**
- (a), (b) Created; `created_by_user_id` = Sales user, `created_by_role = SALES_RECEPTION`, `created_at` set by the system; response `ExpenseCreatedDto` contains only id, category, amount, specification, created at; audited naming the Sales user.
- (c) 422 / 403; nothing written.
- (d) 400 for specification; rejected for amount.
- (e) 400.
- (f) 403 / no route (see AC-NEG-042).
- No stock / quantity / item behavior is triggered.

**Test level:** API / Security

### AC-EXP-003 — Sales allowed-categories lookup

**Traces:** FR-EXP-003
**Type:** Positive / Privacy / Permission
**Task:** 4

**Given**
- The 11 seeded categories, with Petty / Miscellaneous deactivated in one scenario.

**When**
- (a) Sales calls `GET /api/sales/expenses/allowed-categories`.
- (b) A Doctor calls it.

**Then**
- (a) Returns `[{ id, name }]` for active categories with `sales_can_record = true` only (both allowed categories, or only Materials / Medical Supplies when Petty is inactive); no flag, totals or expenses.
- (b) 403.

**Test level:** API

### AC-EXP-004 — Manager expense management

**Traces:** FR-EXP-004
**Type:** Positive / Validation / Permission
**Task:** 4

**Given**
- Expenses created by Sales and by the Manager.

**When**
- (a) The Manager creates an expense in Rent.
- (b) The Manager lists and reads expenses.
- (c) The Manager creates with amount ≤ 0.

**Then**
- (a) Created in any category; audited.
- (b) All expenses with creator and creator role.
- (c) Rejected.
- Sales / Doctor calling these routes receive 403.

**Test level:** API

### AC-EXP-005 — Manager edits an expense, audited

**Traces:** FR-EXP-005
**Type:** Positive / Validation / Audit / Permission
**Task:** 4

**Given**
- A Sales-created expense.

**When**
- (a) The Manager changes amount and specification with a valid `If-Match`.
- (b) The edit uses a stale ETag; the edit tries to change `created_by_user_id`, `created_by_role` or `created_at`.
- (c) Sales calls the route.

**Then**
- (a) Updated in place; `updated_by_user_id` / `updated_at` set; creator fields unchanged; audited with who, when and before / after of changed fields.
- (b) 412; creator fields are not overwritable (rejected).
- (c) 403.

**Test level:** API

### AC-EXP-006 — No Finalized state, no attachments, no deletion

**Traces:** FR-EXP-006
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table and UI.

**When**
- They are inspected; finalize, adjustment, attachment-upload and DELETE requests are attempted on an expense.

**Then**
- No `finalized_at` column, Finalized state, finalize route or expense adjustment rows (see AC-NEG-019).
- No attachment table, file column, upload route or UI control (see AC-NEG-010).
- No delete route; `clinic_app` has no DELETE grant on expenses.

**Test level:** DB / Security / UI

### AC-EXP-007 — Expenses reduce only the Clinic result

**Traces:** FR-EXP-007
**Type:** Financial
**Task:** 4

**Given**
- A month with Doctor earnings 4,000.00 and expenses 1,500.00.

**When**
- The financial report and Doctor payroll are produced.

**Then**
- Expenses are subtracted only in the Estimated Clinic Operating Result.
- Doctor earnings / payroll remain 4,000.00.

**Test level:** Integration

## 3.15 PAYROLL

### AC-PAYROLL-001 — Commission base = collected money of Lead-sourced patients

**Traces:** FR-PAYROLL-001
**Type:** Financial
**Task:** 4

**Given**
- Sales S1 at 5 %; Lead-sourced patient P (Original Sales S1) with a Treatment charge original 3,000.00, discount 500.00, final 2,500.00, and a Consultation charge 200.00; Walk-In patient W with collections.

**When**
- In October: 1,000.00 is allocated to P's Treatment charge and 200.00 to P's Consultation charge; 1,500.00 remains uncollected. S1's October Sales payroll draft is created.

**Then**
- Two commission items: 50.00 and 10.00 (base = actual collected money after discount, all charge kinds); no item for the uncollected 1,500.00, undiscounted price or Clinic Share; none for W.
- Each allocation yields at most one commission item ever (re-running drafts adds none).
- Attendance does not affect the items.
- G2 rejects an item whose employee ≠ Original Sales, wrong month, after deactivation, non-effective rate revision or non-Draft payroll.

**Test level:** Integration / DB

### AC-PAYROLL-002 — Rate at collection time, snapshotted per item

**Traces:** FR-PAYROLL-002
**Type:** Financial
**Task:** 4

**Given**
- S1 rate revision 1 = 5 % (effective 2026-01-01) and revision 2 = 7 % (effective 2026-10-15 10:00).

**When**
- Allocations at 2026-10-15 09:59 and 10:00 are processed into the October draft.

**Then**
- The first item stores revision 1 and `commission_percentage_applied` 5 %; the second stores revision 2 and 7 % (greatest `effective_from ≤ allocated_at`).
- The payroll header has no single percentage; the month shows both rates.
- An item referencing a non-effective revision is rejected (G2).

**Test level:** Integration / DB

### AC-PAYROLL-003 — New rate applies to future collections only

**Traces:** FR-PAYROLL-003
**Type:** Financial / Negative
**Task:** 4

**Given**
- First collection 1,000.00 at 5 % → item 50.00 in a finalized September payroll; the Manager then changes the rate to 7 %.

**When**
- A second collection of 1,000.00 is allocated in October; payroll drafts are regenerated.

**Then**
- Second item 70.00 at 7 %; the first item still shows 5 % / 50.00; total commission across both = 120.00.
- The finalized September payroll is byte-identical before and after.
- Draft items created before the change are not recalculated.
- No recalculation route or job exists (see AC-NEG-024).

**Test level:** Integration

### AC-PAYROLL-004 — Walk-In / Direct patients generate zero commission

**Traces:** FR-PAYROLL-004
**Type:** Financial / Negative
**Task:** 4

**Given**
- Walk-In patient W registered by Sales S1 (Original Sales = NULL), with a 2,000.00 charge.

**When**
- 2,000.00 is collected and all Sales payroll drafts for the month are created.

**Then**
- No commission item is created for any Sales employee (including S1).
- No pool table, pool payroll line, equal split, pool route or job exists; no reassignment of commission occurs.
- Reports list W's collection with commission 0.00 (see AC-NEG-015).

**Test level:** Integration / DB

### AC-PAYROLL-005 — Inactive Sales gets no new commission

**Traces:** FR-PAYROLL-005
**Type:** Financial
**Task:** 4

**Given**
- Lead-sourced patient P (Original Sales S1); S1 deactivated at 2026-10-20 12:00; active S2 received S1's open Leads.

**When**
- Allocations on P at 2026-10-20 11:59 and 12:00 are processed.

**Then**
- 11:59: a commission item for S1.
- 12:00 (`allocated_at ≥ deactivated_at`): no item for S1 and none for S2 or anyone else; G2 rejects any attempt.
- P's Original Sales remains S1.
- The Manager report lists the 12:00 collection as "not accrued — inactive Sales".

**Test level:** Integration

### AC-PAYROLL-006 — Reversal commission at the original rate

**Traces:** FR-PAYROLL-006
**Type:** Financial
**Task:** 4

**Given**
- First collection 1,000.00 at 5 % (item 50.00, September, finalized); rate changed to 7 %; second collection 1,000.00 (item 70.00).

**When**
- The Manager reverses the first collection in November.

**Then**
- A negative item of −50.00 at 5 % (not −70.00) in the November payroll.
- The finalized September payroll is unchanged.
- Items are insert-only.

**Test level:** Integration

### AC-PAYROLL-007 — Unapplied Patient Credit is not commission base

**Traces:** FR-PAYROLL-007
**Type:** Financial
**Task:** 4

**Given**
- Lead-sourced patient P (S1 at 5 %) pays 1,000.00 in October with no charge (credit).

**When**
- October payroll is drafted; in November 1,000.00 credit is applied to a charge and November payroll is drafted.

**Then**
- October: no commission item.
- November: one item of 50.00 (collection moment = application time).

**Test level:** Integration

### AC-PAYROLL-008 — Monthly payroll lifecycle

**Traces:** FR-PAYROLL-008
**Type:** Positive / Validation / Financial
**Task:** 4

**Given**
- October Doctor, Sales and Assistant draft payrolls, created by the Manager.

**When**
- (a) Finalize is attempted on 2026-10-31 (clinic-local, month not ended).
- (b) Finalize on 2026-11-01, then mark paid.
- (c) After finalization: update a sealed money column; add an item or adjustment.
- (d) A second payroll for the same person-month; a non-calendar-month period.
- (e) Doctor or Sales calls any Manager payroll route.

**Then**
- (a) 422.
- (b) Draft → Finalized → Paid; audited on create, finalize and mark paid.
- (c) Rejected by G2; status / finalized-at / paid-at transitions remain possible.
- (d) 409 (e.g. `payroll.assistant_period_exists`); rejected.
- (e) 403.
- Historical payroll is never recalculated.

**Test level:** API / DB

### AC-PAYROLL-009 — Doctor payroll

**Traces:** FR-PAYROLL-009
**Type:** Financial
**Task:** 4

**Given**
- D1 (inactive) with October earnings 800.00 and 2,000.00 and a pending fixed-lab entitlement 500.00; one September earning already paid; negative earnings −3,500.00 in October from a correction for D2.

**When**
- October Doctor drafts are created.

**Then**
- D1: items 800.00 and 2,000.00; `doctor_share_total` 2,800.00; informational revenue / lab / clinic totals; `final_amount` = 2,800.00 + adjustments; the pending 500.00 is excluded; the September earning is not included again.
- D2: negative total shown as is; no automatic carry-forward.
- Unaffected by reassignment / deactivation.
- G2 rejects an earning of another Doctor or month.

**Test level:** Integration

### AC-PAYROLL-010 — Doctor payroll adjustments

**Traces:** FR-PAYROLL-010
**Type:** Financial / Validation
**Task:** 4

**Given**
- A Draft Doctor payroll with `doctor_share_total` 2,800.00 and a Finalized one.

**When**
- (a) The Manager adds an adjustment +200.00 with reason to the Draft.
- (b) The Manager adds an adjustment to the Finalized payroll.

**Then**
- (a) `adjustments_total` 200.00; `final_amount` 3,000.00; audited.
- (b) Rejected (G2).

**Test level:** API / DB

### AC-PAYROLL-011 — Doctor reads own payroll

**Traces:** FR-PAYROLL-011
**Type:** Privacy / Permission
**Task:** 4

**Given**
- Payrolls for D1 and D2.

**When**
- D1 calls `GET /api/doctor/me/payroll` (also with a Doctor id parameter); the Manager calls the same route.

**Then**
- D1 receives own rows only; the parameter has no effect / is rejected.
- Manager → 404 (see AC-NEG-038).

**Test level:** API / Security

### AC-PAYROLL-012 — Sales payroll

**Traces:** FR-PAYROLL-012
**Type:** Financial
**Task:** 4

**Given**
- S1 base salary 6,000.00, active all October, with commission items 50.00 + 70.00 and Walk-In collections; S2 deactivated on 2026-10-10.

**When**
- October Sales drafts are created; the Manager adds Bonus 100.00 to S1 and a Deduction to S2 for the partial month; payrolls are finalized.

**Then**
- S1: base 6,000.00 + commission 120.00 + Bonus 100.00 − Deductions 0.00 = net 6,220.00; Walk-In contributes 0.00.
- S2 has a payroll (active at some time in the month); base salary is not auto-prorated.
- Attendance has no effect.
- At finalization `commission_total` = Σ items is verified.

**Test level:** Integration

### AC-PAYROLL-013 — Sales reads own payroll only

**Traces:** FR-PAYROLL-013
**Type:** Privacy / Permission
**Task:** 4

**Given**
- Payrolls for S1 and S2.

**When**
- S1 calls `GET /api/sales/me/payroll`, requests S2's payroll id, and calls the commission-history route.

**Then**
- Own rows and items only; S2's payroll → 404; commission history → 403.
- No other employee's compensation appears in any Sales response (see AC-NEG-039).

**Test level:** API / Security

### AC-PAYROLL-014 — Assistant payroll draft

**Traces:** FR-PAYROLL-014
**Type:** Financial / Validation
**Task:** 4

**Given**
- A1 salary 6,000.00; October overtime entries 5 h × 100.00 = 500.00 (live) and one voided; a September entry already paid.

**When**
- (a) The Manager creates A1's October draft.
- (b) A1's salary then changes to 6,500.00.
- (c) A second October draft for A1 is created.

**Then**
- (a) Salary snapshot 6,000.00; Base Pay 6,000.00 (source `SalarySnapshot`); one overtime item (5 h, 100.00, 500.00); Base Pay, Overtime and Final shown separately; voided and September entries excluded; audited.
- (b) The snapshot stays 6,000.00.
- (c) 409 `payroll.assistant_period_exists`.
- An overtime entry belongs to at most one payroll, only of its own Assistant and month.

**Test level:** Integration

### AC-PAYROLL-015 — Assistant partial-month manual Base Pay

**Traces:** FR-PAYROLL-015
**Type:** Financial / Validation / Negative
**Task:** 4

**Given**
- A1 joined on 2026-10-12 with salary 6,000.00; Draft October payroll with overtime 400.00.

**When**
- (a) The Manager sets Base Pay 3,500.00 with reason `JoinedMidMonth`.
- (b) Base Pay is set without a reason, or on a Finalized payroll.
- (c) The draft is inspected before (a).
- (d) Overtime is edited through this route.

**Then**
- (a) `base_pay_source = ManagerManual`; Final = 3,500.00 + 400.00 = 3,900.00; audited with old, new and reason code.
- (b) Rejected.
- (c) Base Pay = 6,000.00 (no automatic proration by 30, calendar days or working days).
- (d) Rejected (see AC-NEG-025).

**Test level:** API / Integration

### AC-PAYROLL-016 — Assistant Final = Base Pay + Overtime

**Traces:** FR-PAYROLL-016
**Type:** Financial / Negative
**Task:** 4

**Given**
- A1 Draft: salary 6,000.00, Base Pay 6,000.00, overtime 5 h × 100.00 = 500.00; the month has ended.

**When**
- (a) The Manager finalizes and marks paid.
- (b) A bonus, deduction, commission, incentive or `final-amount` value is submitted.
- (c) Doctor or Sales requests Assistant payroll.

**Then**
- (a) Finalization verifies `overtime_amount` = Σ items = 500.00 and `final_amount` = 6,000.00 + 500.00 = 6,500.00; components stored and reported separately; finalized components immutable; marked Paid.
- (b) 400 / no route (see AC-NEG-025).
- (c) 403.

**Test level:** Integration / API

## 3.16 FDB

### AC-FDB-001 — Feedback request created at Visit finalization

**Traces:** FR-FDB-001
**Type:** Positive / Validation
**Task:** 3

**Given**
- D1's Visit ready for finalization.

**When**
- (a) D1 finalizes the Visit.
- (b) A second request for the same Visit is inserted.
- (c) The finalize transaction is rolled back by an injected fault.

**Then**
- (a) Exactly one request (patient, Visit, Doctor) exists, derived state Pending; the Feedback Request Pending notification is enqueued in the same transaction.
- The request table has no stored status, result or `expires_at` column.
- (b) Rejected (unique).
- (c) No request and no notification remain.

**Test level:** Integration / DB

### AC-FDB-002 — Sales feedback queue (derived state)

**Traces:** FR-FDB-002
**Type:** Positive / Privacy
**Task:** 3

**Given**
- Request R1 with no link, R2 marked sent, R3 with a PatientWeb entry, R4 with a SalesEntered entry.

**When**
- Sales calls `GET /api/sales/feedback-requests?status=` for each status.

**Then**
- R1 Pending, R2 Sent, R3 Submitted, R4 Submitted.
- No rating or comment appears in any queue item (see AC-NEG-043).

**Test level:** API

### AC-FDB-003 — Prepare the unique feedback link

**Traces:** FR-FDB-003
**Type:** Positive / Security / Validation
**Task:** 3

**Given**
- A Pending request and a Submitted request.

**When**
- (a) Sales prepares the link.
- (b) Sales prepares it again.
- (c) Sales prepares a link for the Submitted request.

**Then**
- (a) A 256-bit random token is issued; only its hash is stored; the website URL is returned once; the token contains / derives from no patient or Visit identifier.
- (b) The hash is replaced; the earlier link now returns the generic invalid-token response.
- (c) 409 `feedback.already_submitted`.
- The token never appears in logs or audit (log scan).

**Test level:** API / Security

### AC-FDB-004 — Mark the link as sent

**Traces:** FR-FDB-004
**Type:** Positive / Validation
**Task:** 3

**Given**
- A Pending request with a prepared link; a Submitted request.

**When**
- (a) Sales marks it sent with channel.
- (b) Sales marks the Submitted request sent.

**Then**
- (a) `link_sent_at`, `sent_by_user_id`, `delivery_channel` stored; derived state Sent.
- (b) 409.
- No automated WhatsApp / SMS / email delivery occurs (see AC-NEG-030).

**Test level:** API

### AC-FDB-005 — Patient opens the feedback form

**Traces:** FR-FDB-005
**Type:** Positive / Security / Privacy
**Task:** 3

**Given**
- A valid token, a malformed token, an unknown well-formed token, and a token whose request is Submitted.

**When**
- `GET /api/public/feedback/{token}` is called anonymously for each, including from a non-clinic origin and above the rate limit.

**Then**
- Valid: only the Arabic form definition and the Doctor's display name; no patient identifier; no login required.
- Malformed and unknown: identical generic responses.
- Submitted: neutral `feedback.already_submitted` state with nothing from the entry.
- CORS allows only the clinic website origin; rate limits per IP and per token hash return 429 (see AC-NEG-041).

**Test level:** API / Security

### AC-FDB-006 — Patient submits website feedback

**Traces:** FR-FDB-006
**Type:** Positive / Validation / Security
**Task:** 3

**Given**
- A valid token for a Pending request.

**When**
- (a) The patient posts `{ rating: 4, comment: "<b>x</b>" }`.
- (b) The patient posts again; or after a Sales entry for the same request.
- (c) Rating 0 or 6; any identifying field is added.

**Then**
- (a) One `PatientWeb` entry linked to the request; comment stored as plain text; request derived Submitted; neutral acknowledgement; no login or CSRF token required (token binding).
- (b) Neutral `feedback.already_submitted`; no second entry.
- (c) Rejected (rating 1..5; unknown members 400).

**Test level:** API

### AC-FDB-007 — Feedback links never expire

**Traces:** FR-FDB-007
**Type:** Negative
**Task:** 3

**Given**
- A token prepared and left unused, with the clock advanced by 400 days.

**When**
- The patient opens and submits.

**Then**
- Both succeed.
- No Expired status, `expires_at` column, timeout or J6 expiry job exists (see AC-NEG-012).

**Test level:** Integration / DB

### AC-FDB-008 — Sales-entered feedback about a Visit

**Traces:** FR-FDB-008
**Type:** Positive / Validation / Privacy
**Task:** 3

**Given**
- A finalized Visit V (patient P, Doctor D1) with a Sent request and a prepared token.

**When**
- (a) Sales posts `{ patientId: P, doctorId: D1, visitId: V, rating: 5, comment }`.
- (b) The patient then opens / submits via the token.
- (c) Sales posts again for V; posts with a Visit not belonging to P / D1; rating outside 1..5.

**Then**
- (a) A `SalesEntered` entry with `recorded_by_user_id`, linked to V's existing request; the request becomes Submitted; response `FeedbackEntryCreatedDto` (id, recorded at); audited; no notification; no clinical table touched.
- (b) Neutral already-submitted.
- (c) 409 `feedback.already_submitted`; rejected; rejected.
- Sales cannot read the result afterwards (AC-FDB-011).

**Test level:** API / Integration

### AC-FDB-009 — Sales-entered general feedback (no Visit)

**Traces:** FR-FDB-009
**Type:** Positive / Validation
**Task:** 3

**Given**
- Patient P and Doctor D1, no Visit reference.

**When**
- (a) Sales posts without `visitId`.
- (b) A row with Visit set but request NULL (or vice versa) is inserted directly.

**Then**
- (a) A `SalesEntered` entry with Visit and request both NULL; audited.
- (b) Rejected by CHECK `(visit_id IS NULL) = (feedback_request_id IS NULL)`.

**Test level:** API / DB

### AC-FDB-010 — At most one accepted result per request

**Traces:** FR-FDB-010
**Type:** Validation / DB
**Task:** 3

**Given**
- A Pending request with a valid token.

**When**
- A website submission and a Sales entry for the same request are sent concurrently, then in each sequential order.

**Then**
- Exactly one entry exists each time (unique `feedback_request_id`); the loser receives 409 / neutral `feedback.already_submitted`.

**Linked frozen tests:** API A11 #50
**Test level:** Integration

### AC-FDB-011 — Only the Manager reads feedback results

**Traces:** FR-FDB-011
**Type:** Permission / Privacy
**Task:** 3

**Given**
- PatientWeb and SalesEntered entries for D1 and D2 across dates.

**When**
- (a) The Manager calls `GET /api/manager/feedback` with `doctorId`, `from`, `to` and `source` filters.
- (b) A Doctor or Sales user calls it.

**Then**
- (a) Ratings and comments of both sources with `source` shown, filtered correctly.
- (b) 403; no Doctor or Sales surface exposes ratings / comments (see AC-NEG-028, AC-NEG-043).

**Test level:** API / Security

## 3.17 NOTIF

### AC-NOTIF-001 — Seven notification types

**Traces:** FR-NOTIF-001
**Type:** Positive / Negative / Privacy
**Task:** 3

**Given**
- The notification type catalogue and channel registry.

**When**
- Each source triggers its notification.

**Then**
- Exactly: Appointment Tomorrow; Implant Follow-Up Tomorrow; Orthodontic Installment Due Tomorrow; New Doctor Referral; Outstanding Payment Follow-Up; Pending Follow-Up; Feedback Request Pending.
- Each row has type, recipient user and role, related patient, entity type and id, scheduled / sent / read time, status, retry count and dedupe key; one row per recipient; no patient name or phone stored.
- Only the in-app channel is registered (see AC-NEG-030).

**Test level:** Integration / DB

### AC-NOTIF-002 — Recipients

**Traces:** FR-NOTIF-002
**Type:** Permission / Privacy
**Task:** 3

**Given**
- Active Sales S1, S2; inactive S3 with a Pending notification; Doctors; Assistants; the Manager; an unassigned follow-up and a follow-up assigned to S1.

**When**
- All seven notification types are produced; attendance, overtime and Sales-entered feedback are recorded.

**Then**
- Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral and Feedback Request Pending → S1 and S2 only.
- Pending Follow-Up / Outstanding Payment Follow-Up → S1 only; the unassigned follow-up notifies nobody.
- No row targets a Doctor, Assistant or the Manager; S3 receives nothing and the Pending row is Cancelled.
- Attendance, overtime and Sales-entered feedback create no notification.
- Recipients are resolved only via `v_active_user_roles` / `v_sales_employee_activity` (see AC-NEG-004, AC-NEG-040).

**Test level:** Integration

### AC-NOTIF-003 — J1 appointment and implant reminders

**Traces:** FR-NOTIF-003
**Type:** Positive
**Task:** 3

**Given**
- Clinic-local today 2026-10-23; appointments tomorrow: Standard (Scheduled), ImplantFollowUp (Confirmed), Cancelled, Rescheduled; one booked today for today.

**When**
- J1 runs (every 15 minutes); then the Standard appointment is rescheduled to another time tomorrow and J1 runs again.

**Then**
- Appointment Tomorrow for the Standard and Implant Follow-Up Tomorrow for the ImplantFollowUp; none for Cancelled, Rescheduled or same-day items.
- After rescheduling: the new appointment id gets a new reminder; the old pending reminder is Cancelled.

**Test level:** Integration

### AC-NOTIF-004 — J2 orthodontic installment reminders

**Traces:** FR-NOTIF-004
**Type:** Positive
**Task:** 3, 4

**Given**
- Active installments due tomorrow on an ortho charge with Outstanding > 0; a retired installment due tomorrow; an installment due tomorrow on a fully paid charge.

**When**
- J2 runs.

**Then**
- One reminder for the first, showing the charge-level balance; none for the retired installment or the fully paid charge.

**Test level:** Integration

### AC-NOTIF-005 — J3 follow-up due notifications

**Traces:** FR-NOTIF-005
**Type:** Positive / Privacy
**Task:** 2, 3

**Given**
- Open follow-ups due today or earlier: one `RemainingBalance` and one `Lead` assigned to S1, one unassigned; a `FollowedUp` follow-up due yesterday.

**When**
- J3 runs; then the Lead follow-up's due date is changed and J3 runs again.

**Then**
- S1 receives Outstanding Payment Follow-Up and Pending Follow-Up; the unassigned one is skipped (counted by J7); the closed one produces nothing.
- The due-date change produces a new notification.
- The Worker reads follow-ups without notes.

**Test level:** Integration

### AC-NOTIF-006 — Event-driven notifications

**Traces:** FR-NOTIF-006
**Type:** Positive
**Task:** 3

**Given**
- A referral creation and a Visit finalization; one of each is forced to roll back.

**When**
- The transactions commit / roll back and J4 runs.

**Then**
- Committed sources: New Doctor Referral / Feedback Request Pending enqueued in the same transaction and dispatched within a minute.
- Rolled-back sources leave no notification.

**Test level:** Integration

### AC-NOTIF-007 — Notification inbox

**Traces:** FR-NOTIF-007
**Type:** Positive / Permission / Privacy
**Task:** 3

**Given**
- Sent notifications for S1 and S2 with patient contact and clinical canaries in source data.

**When**
- (a) S1 lists the inbox, reads the unread count, marks one read, marks all read.
- (b) S1 marks S2's notification read.
- (c) A Doctor calls any inbox route.

**Then**
- (a) Paged own Sent rows rendered at display time under Sales whitelist permissions; counts update; no contact or clinical canary in rendered DTOs.
- (b) 404.
- (c) 403 (see AC-NEG-028).

**Test level:** API / Security

### AC-NOTIF-008 — Idempotent, timezone-aware, retry-safe processing

**Traces:** FR-NOTIF-008
**Type:** Positive / Security
**Task:** 3

**Given**
- Planner jobs and the J4 / J5 dispatcher; one poison notification whose delivery throws.

**When**
- (a) J1 runs twice for the same target date.
- (b) J4 processes a batch including the poison item.
- (c) J5 runs repeatedly.
- (d) The Worker is down for an entire reminder day and restarts.

**Then**
- (a) One row per dedupe key `{type}:{entity_id}:{clinic-local target date}:{recipient}`.
- (b) Each notification is processed in its own transaction (`SKIP LOCKED`); others are Sent; the poison item is `Failed` with retry count incremented; source and recipient are re-validated before delivery.
- (c) Failed → Pending retry only while retry count < 5.
- (d) No late reminder is invented for the missed day.
- Job arguments are ids only; `last_error` is scrubbed, ≤ 500 characters, without names / contact data.

**Test level:** Integration

### AC-NOTIF-009 — J7 operations health

**Traces:** FR-NOTIF-009
**Type:** Operations / Privacy
**Task:** 3, 5

**Given**
- A running Worker with one inactive Sales owning no open follow-ups and two unassigned open items.

**When**
- J7 runs (every 5 minutes) and the recurring-job registry is inspected.

**Then**
- J7 reports job freshness, dispatcher lag, exhausted retries, failed jobs, open follow-ups owned by inactive Sales (0) and unassigned open Leads / Follow-Ups (2) as counts only, with no names, ids or text.
- Recurring jobs are exactly J1–J5 and J7; no J6 (see AC-NEG-012).

**Test level:** Operations

## 3.18 REPORT

### AC-REPORT-001 — Manager live daily dashboard

**Traces:** FR-REPORT-001
**Type:** Positive / Permission / Negative
**Task:** 4

**Given**
- Activity on 2026-10-23 and two unassigned open items.

**When**
- (a) The Manager calls `GET /api/manager/dashboard` with no date, then with `date=2026-10-22`.
- (b) A payment is recorded and the dashboard is re-read.
- (c) Doctor or Sales calls the route.

**Then**
- (a) Live figures for the clinic-local date, including Unassigned Leads / Follow-Ups counters (2).
- (b) The figure changes immediately (computed on read).
- (c) 403.
- No Close Day route, sealed daily state or report-generation job exists (see AC-NEG-011).

**Test level:** API / Integration

### AC-REPORT-002 — Financial report

**Traces:** FR-REPORT-002
**Type:** Financial / Permission
**Task:** 4

**Given**
- A month with charges, discounts, collections (Walk-In and Lead-sourced), consultation fees, General / Fixed lab distributions, a Finalized Sales payroll, a Paid Assistant payroll, a Draft Sales payroll, and Materials / Petty / Rent expenses.

**When**
- The Manager requests Daily, Weekly, Monthly and Custom reports.

**Then**
- REVENUE: Billed, Discounts, Collected, Consultation Revenue, General Lab, Fixed Lab funding, Clinic Treatment Revenue Share, Clinic Gross Revenue.
- COMPENSATION / EXPENSES: Sales Salaries, Sales Commissions (Walk-In at 0), Assistant Base Salaries, Assistant Overtime, Assistant Payroll Expense, Clinic Operating Expenses with Materials and Petty as separate lines.
- RESULT and MEMO sections present.
- Only Finalized / Paid payrolls are counted; the Draft is listed as not yet finalized.
- Reporting owns no business table (schema check); Doctor / Sales → 403.

**Test level:** Integration / API

### AC-REPORT-003 — Estimated Clinic Operating Result

**Traces:** FR-REPORT-003
**Type:** Financial
**Task:** 4

**Given**
- For a month: Σ clinic share 20,000.00; consultation fees collected 2,000.00; Sales compensation (salary 6,000.00 + commission 500.00 + bonus 100.00 − deductions 0.00) = 6,600.00; Assistant Payroll Expense 6,500.00; operating expenses 3,000.00 (incl. Materials / Petty); Doctor payroll 8,000.00.

**When**
- The financial report and finance summary are produced.

**Then**
- Clinic Treatment Revenue Share 20,000.00; Clinic Gross Revenue 22,000.00; Result = 22,000.00 − 6,600.00 − 6,500.00 − 3,000.00 = 5,900.00.
- Doctor payroll 8,000.00 is not subtracted again; expenses do not reduce any Doctor share.
- The result is labelled an estimated operational metric.

**Test level:** Integration

### AC-REPORT-004 — Memo lines

**Traces:** FR-REPORT-004
**Type:** Financial
**Task:** 4

**Given**
- Pending fixed-lab entitlement 800.00, Patient Credit 2,000.00, refunds, and a collection "not accrued — inactive Sales".

**When**
- The report is produced.

**Then**
- MEMO lists Doctor recognized share, Pending Fixed-Lab Doctor entitlement (800.00), Patient Credit (2,000.00), Cash received by method, Outstanding, refund / correction effects, "not accrued — inactive Sales" and Lab total.
- Pending entitlement and Patient Credit are not deducted and not counted as revenue or Clinic profit.

**Test level:** Integration

### AC-REPORT-005 — Assistant / Sales payroll only in full months

**Traces:** FR-REPORT-005
**Type:** Financial / Negative
**Task:** 4

**Given**
- Finalized October Assistant payroll (Base Pay 6,000.00, Overtime 500.00) and Sales payroll.

**When**
- Reports run for: Monthly October; Custom 2026-10-01..2026-10-31; Custom 2026-10-01..2026-10-15; Daily 2026-10-10; Weekly; Custom 2026-09-15..2026-11-15.

**Then**
- Monthly and full-month Custom ranges include Assistant Base Pay 6,000.00 and Overtime 500.00 as separate lines plus Sales payroll.
- The partial Custom, Daily and Weekly ranges include 0.00 (no fraction, no division by 30 / calendar / working days, no PENDING / INCOMPLETE label).
- The Sept 15–Nov 15 range includes October in full.

**Test level:** Integration

### AC-REPORT-006 — Finance summary and lists

**Traces:** FR-REPORT-006
**Type:** Financial / Permission
**Task:** 4

**Given**
- A populated finance ledger.

**When**
- The Manager calls summary, pending-entitlements, charges, payments and credit routes.

**Then**
- The summary contains Billed, Collected, Outstanding, cash by method, discounts by setter role, consultation fees, Doctor recognized share, pending entitlement, Lab, Clinic Treatment Revenue Share, Clinic Gross Revenue, Patient Credit, refunds / corrections and estimated result — each equal to the derivation in AC-FIN-003.
- Doctor / Sales → 403.

**Test level:** API / Integration

### AC-REPORT-007 — Performance, receivables, expenses, attendance reports

**Traces:** FR-REPORT-007
**Type:** Positive / Privacy / Permission
**Task:** 4

**Given**
- Data including Walk-In and Lead-sourced collections, an inactive Sales employee, ortho receivables, expenses, attendance, approved overtime and Medical History canaries.

**When**
- The Manager runs each report; Doctor and Sales call each route.

**Then**
- Sales performance shows commission on collected money after discount, Walk-In vs Lead-sourced collections, Walk-In at 0 commission and "not accrued — inactive Sales".
- Attendance report shows Assistants and Sales attendance and approved overtime by month, without proration.
- No Medical History value appears in any report (canary scan; see AC-NEG-037).
- Doctor / Sales → 403.

**Test level:** API / Security

### AC-REPORT-008 — Report exports

**Traces:** FR-REPORT-008
**Type:** Security / Privacy / Permission
**Task:** 4

**Given**
- Report data containing a patient name starting with `=` and a comment starting with `@`.

**When**
- (a) The Manager exports CSV and PDF.
- (b) Doctor or Sales calls any export route.

**Then**
- (a) Arabic output built from an explicit column whitelist; streamed with `Content-Disposition: attachment`, not stored; file names contain no patient name or phone; cells beginning with `=`, `+`, `-`, `@` are neutralized; no Medical History values; each export audited with user, type, filter and row count.
- (b) 403; no Doctor or Sales export route exists.

**Test level:** API / Security

### AC-REPORT-009 — Sales dashboard (8 items)

**Traces:** FR-REPORT-009
**Type:** Positive / Privacy / Negative / Permission
**Task:** 4

**Given**
- Today: appointments (one with a live arrival), new Leads, open and closed follow-ups, new referrals; tomorrow: appointments and ortho installments; outstanding balances; Pending feedback requests.

**When**
- (a) Sales calls `GET /api/sales/dashboard`.
- (b) A Doctor calls it; any Doctor dashboard route is called.

**Then**
- (a) Exactly eight items: (1) Today's Appointments; (2) New Leads; (3) Pending Follow-Ups (open predicate `status <> 'FollowedUp'`); (4) New Referrals; (5) Tomorrow's Appointments; (6) Tomorrow's Orthodontic Installments; (7) Outstanding Balances; (8) Feedback Requests to Send.
- The arrived patient appears inside Today's Appointments with `arrivedAt`; no Arrived / Waiting status and no "Waiting Patients" widget.
- No attendance, overtime, Doctor finance or Manager report data.
- (b) 403; no Doctor dashboard route exists (see AC-NEG-014, AC-NEG-028).

**Test level:** API / UI

## 3.19 SEC

### AC-SEC-001 — Four-layer backend authorization

**Traces:** FR-SEC-001
**Type:** Permission / Security
**Task:** 2, 5

**Given**
- The full route table, `identity.role_permissions` and the SEC §4 matrix.

**When**
- The generated role-matrix test calls every route as anonymous, Manager, Doctor and Sales, with in-scope and out-of-scope resources.

**Then**
- Unauthenticated → 401; missing permission key → 403; out-of-scope resource → 404; responses are audience DTO projections.
- Every mapped route has a permission key assigned to ≥ 1 role, or `[AuthenticatedOnly]`, or is one of the six anonymous endpoints; a route without metadata is denied by the fallback policy.
- The Manager is denied every capability not in the matrix (not a wildcard); no in-app role editor exists.
- Bypassing the React guards (direct API call) does not grant access.

**Test level:** Security

### AC-SEC-002 — Doctors never receive patient contact data

**Traces:** FR-SEC-002
**Type:** Privacy
**Task:** 2, 3, 5

**Given**
- A patient with unique canaries in phone, WhatsApp, email, address, CRM / contact notes, appointment `sales_notes`, Original Sales Representative and patient source.

**When**
- Every `/api/doctor/*` route, the Doctor OpenAPI document, Doctor search, prescription PDF, notifications, logs, error bodies and audit views reachable by a Doctor are exercised and scanned.

**Then**
- No canary value and no contact property name appears.
- Contact endpoints return 403 to a Doctor; no contact-bearing permission key is mapped to DOCTOR (see AC-NEG-034).

**Test level:** Security

### AC-SEC-003 — Sales never receives clinical detail or Doctor finance

**Traces:** FR-SEC-003
**Type:** Privacy
**Task:** 2, 3, 4, 5

**Given**
- Canaries in clinical notes, root canal measurements, procedure details, medication details, referral notes, Doctor percentage, Doctor Share, entitlement, Clinic Share, General / Fixed lab data, other employees' compensation and Assistant data.

**When**
- Every `/api/sales/*` route, the Sales OpenAPI document, notifications, errors and logs are exercised and scanned; Sales calls clinical / Doctor-finance routes.

**Then**
- No canary value or forbidden property name appears; those routes return 403.
- Architecture test: Sales code reads clinical data only through `ISalesClinicalSummary` and `ISalesPricingLines` (see AC-NEG-035, AC-NEG-039).

**Test level:** Security

### AC-SEC-004 — Sales whitelist and deliberate exceptions

**Traces:** FR-SEC-004
**Type:** Positive / Privacy
**Task:** 2, 3, 4, 5

**Given**
- A Lead-sourced patient with all data categories populated.

**When**
- Sales reads patient, summary, referral, appointment, balance, ortho and intake views.

**Then**
- Sales receives exactly the whitelist: contact data; DOB; Original Sales Representative (Lead-sourced); procedure name, treating Doctor, treatment status; Referral Destination / Type, Referral Reason, follow-up instructions; appointment and arrival; price, discount, total, paid, remaining, Patient Credit; ortho installment information; the structured intake.
- Sales can set a discount before finalization and collect / edit the intake.
- Nothing outside the whitelist is returned.

**Test level:** API / Security

### AC-SEC-005 — Resource scoping from the principal

**Traces:** FR-SEC-005
**Type:** Permission / Privacy
**Task:** 2, 3, 4, 5

**Given**
- Doctors D1, D2 and Sales S1, S2 with own work, payroll, notifications, queue, Leads and Follow-Ups.

**When**
- D1 / S1 call each own route, adding `doctorId` / `salesEmployeeId` parameters, and request the other user's scoped resources.

**Then**
- Own routes return only the caller's data; no own route accepts an identity parameter (rejected / no effect).
- Another user's scoped resource → 404.
- D1 writes only own editable Visit / work and reschedules only own-created follow-ups (see AC-NEG-038).

**Test level:** Security

### AC-SEC-006 — Audience DTOs and strict request binding

**Traces:** FR-SEC-006
**Type:** Security / Privacy
**Task:** 2, 5

**Given**
- The OpenAPI documents per audience and all command endpoints.

**When**
- (a) Requests with unknown members (e.g. `id`, `createdBy`, entity navigation properties) are sent.
- (b) The Doctor and Sales OpenAPI documents are scanned.
- (c) The Manager calls a shared route.

**Then**
- (a) 400; no over-posting succeeds.
- (b) One document per audience; the Doctor document has no contact property name; the Sales document has no clinical property name.
- (c) The Manager receives that audience's DTO.
- Architecture test: no EF entity is serialized or model-bound.

**Test level:** Security

### AC-SEC-007 — No personal data in URLs, logs, alerts, job arguments

**Traces:** FR-SEC-007
**Type:** Privacy / Security
**Task:** 2, 5

**Given**
- Requests carrying canary names, Egyptian mobiles, `+20` numbers, emails and Medical History values.

**When**
- Searches, patient / contact / finance / payroll writes, job scheduling, notifications and error tracking run; logs, Hangfire storage, alerts and error-tracker events are scanned.

**Then**
- Searches are POST bodies; URLs contain only opaque ids and patient codes.
- No request / response bodies are logged on patient, contact, finance or payroll routes; mobiles / `+20` / emails are masked.
- Hangfire arguments are ids only; notification payloads have no patient name or phone; error tracking is scrubbed.
- No canary appears in any scanned surface (see AC-NEG-037).

**Test level:** Security

### AC-SEC-008 — Sales-authored Doctor-visible text check

**Traces:** FR-SEC-008
**Type:** Validation / Privacy
**Task:** 3, 5

**Given**
- Sales booking an appointment.

**When**
- `reason` contains "01012345678", "+201012345678" or "name@example.com"; then plain text "Crown check".

**Then**
- The first three return 400 with a code pointing to `sales_notes`; the last is accepted.
- The same values in `sales_notes` are accepted.

**Test level:** API

### AC-SEC-009 — Exactly six anonymous endpoints

**Traces:** FR-SEC-009
**Type:** Security / Negative
**Task:** 2, 3, 5

**Given**
- The application route table at startup.

**When**
- The startup test enumerates anonymous routes; anonymous calls are made to `GET /api/me`, logout and change-password.

**Then**
- Anonymous routes are exactly `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /health/live`, `GET /health/ready`, `GET /api/public/feedback/{token}`, `POST /api/public/feedback/{token}`; startup fails if the list differs.
- The other three return 401.
- No patient login, portal or mobile route exists (see AC-NEG-005, AC-NEG-006, AC-NEG-041).

**Test level:** Security

### AC-SEC-010 — Assistant, attendance and payroll data are Manager-only

**Traces:** FR-SEC-010
**Type:** Permission / Privacy
**Task:** 4, 5

**Given**
- Assistant, attendance, overtime and payroll records with canaries.

**When**
- Doctor and Sales call every `/api/manager/staff/*` and `/api/manager/payroll/*` route; all Doctor / Sales responses are scanned; Worker DB permissions are checked.

**Then**
- 403 on every route; no canary in any Doctor or Sales response.
- The Worker role has no access to these tables (see AC-NEG-004, AC-NEG-040).

**Test level:** Security / DB

### AC-SEC-011 — Denied-by-default capabilities

**Traces:** FR-SEC-011
**Type:** Permission / Negative
**Task:** 2, 3, 4, 5

**Given**
- The generated role-matrix test.

**When**
- The listed unconfirmed capabilities are invoked.

**Then**
- 403 (or no route) for: Doctor dashboard, Doctor notifications, Doctor feedback results, Doctor payment recording, any Sales view of Doctor Share / Clinic Share / lab / percentages, Sales expense lists / reports / edits, Sales category management, Sales feedback results, Sales prescription print, Manager arrival recording (see AC-NEG-028, AC-NEG-042).

**Test level:** Security

### AC-SEC-012 — Web security controls

**Traces:** FR-SEC-012
**Type:** Security
**Task:** 2, 5

**Given**
- A deployed environment.

**When**
- Responses, headers, SPA behavior and limits are inspected.

**Then**
- HTTPS with HSTS and security headers; CSP without inline scripts; unsafe methods without CSRF token are rejected; authenticated responses carry `Cache-Control: no-store`.
- Free text (e.g. `<script>`, Markdown) renders as plain text.
- SQL is parameterized (static analysis); sort / filter fields outside per-endpoint whitelists are rejected; page size > 100 is rejected / capped.
- No data or token in browser storage; frontend cache cleared on logout / 401.
- More than 300 requests/min/user → 429.

**Test level:** Security / UI

## 3.20 AUDIT

### AC-AUDIT-001 — Audit written atomically and insert-only

**Traces:** FR-AUDIT-001
**Type:** Audit / DB
**Task:** 2, 5

**Given**
- An audited action (e.g. Visit finalize) and the `clinic_app` role.

**When**
- (a) The action succeeds.
- (b) The audit insert is forced to fail.
- (c) `clinic_app` attempts UPDATE / DELETE on `audit.audit_log`.

**Then**
- (a) The audit row commits in the same transaction.
- (b) The business action fails and rolls back (fail closed).
- (c) Permission denied (INSERT and SELECT only).

**Test level:** DB / Integration

### AC-AUDIT-002 — Audited events

**Traces:** FR-AUDIT-002
**Type:** Audit
**Task:** 2, 3, 4, 5

**Given**
- A scripted session performing every event in FR-AUDIT-002.

**When**
- The audit log is read after each event.

**Then**
- An entry exists with actor, role where applicable, timestamp, entity / action and reason where the FR requires one, for each of: login success / failure; account creation, password set, role change, deactivation / reactivation, forced sign-out; Doctor record opens, Sales contact views, Manager views; contact edits (restricted); Medical History create / update (field names only); reassignment; appointment / arrival / status changes; Visit finalize; controlled corrections; price entries; discount entries; charge adjustments; payments, refunds, reversals, reallocations, replacements; fixed-lab entries / revisions; Ortho terms and schedule revisions; installment applications; percentage, commission-rate, lab-rate, salary and overtime-rate changes; attendance create / edit; overtime record / void; payroll create / Base Pay / finalize / paid; expense create and edits (before / after); Sales-entered feedback; exports and prescription prints; timezone change; audit reads.

**Test level:** Integration

### AC-AUDIT-003 — Never-stored audit content

**Traces:** FR-AUDIT-003
**Type:** Privacy / Audit
**Task:** 2, 5

**Given**
- Actions involving passwords, security stamps, feedback tokens and Medical History canary values.

**When**
- The audit log is scanned.

**Then**
- No password, password hash, security stamp, reset token, feedback token or Medical History value appears.
- A password change cannot be reconstructed from audit; the Data Protection key table is never audited (see AC-NEG-037).

**Test level:** Security

### AC-AUDIT-004 — Manager-only audit read

**Traces:** FR-AUDIT-004
**Type:** Permission / Audit
**Task:** 2, 5

**Given**
- Audit entries.

**When**
- (a) The Manager pages the list (cursor) and opens a detail.
- (b) Doctor or Sales calls the routes.

**Then**
- (a) `AuditEntryDto` results; each read is itself audited.
- (b) 403.

**Test level:** API

### AC-AUDIT-005 — No physical deletion of business history

**Traces:** FR-AUDIT-005
**Type:** Negative / DB
**Task:** 2, 3, 4, 5

**Given**
- The `clinic_app` role and the route table.

**When**
- DELETE is attempted on treatments, payments, payroll, prescriptions, expenses, lab expenses, Doctor assignments, appointments, arrivals, installments and employees, via API and SQL.

**Then**
- No delete route exists; `clinic_app` has no DELETE grant on any business schema.
- Removal is expressed only by soft markers (`voided_at`, `removed_at`, `withdrawn_at`, `is_active`).

**Test level:** DB / Security

### AC-AUDIT-006 — Insert-only ledgers

**Traces:** FR-AUDIT-006
**Type:** DB / Financial
**Task:** 2, 3, 4, 5

**Given**
- Each insert-only table listed in FR-AUDIT-006.

**When**
- UPDATE and DELETE are attempted as `clinic_app` on each; an expense is edited.

**Then**
- UPDATE / DELETE are denied on charges, adjustments, payments, allocations, credit entries, corrections, distribution events, entitlements, earnings, distributions, fixed-lab expenses / revisions, lab-percentage history, commission-rate history, ortho term / installment revisions, installment applications, discount entries, Visit corrections, feedback entries, responsibility history, contact attempts, payroll items / adjustments.
- The expense edit succeeds in place with audit (the only exception).

**Test level:** DB

### AC-AUDIT-007 — Snapshots never recalculated

**Traces:** FR-AUDIT-007
**Type:** Financial / DB
**Task:** 4, 5

**Given**
- Existing work items, charges, distributions, commission items, overtime entries / items, Assistant and Sales payroll snapshots and sealed payroll totals.

**When**
- Doctor percentage, lab %, commission rate, overtime rate, Assistant salary and Sales base salary are all changed.

**Then**
- Every existing snapshot value and sealed total is byte-identical before and after.

**Test level:** Integration / DB

### AC-AUDIT-008 — Historical relationships preserved

**Traces:** FR-AUDIT-008
**Type:** DB / Audit
**Task:** 2, 3, 5

**Given**
- A patient with reassignments, a reschedule chain, a deactivated Sales owner with redistributed items, and Lead attribution.

**When**
- The timelines and history tables are read.

**Then**
- Past work still references the performing Doctor; the appointment chain shows the old appointment Rescheduled and the new one linked via `previous_appointment_id`; Doctor assignment history, responsibility history and Original Sales attribution are complete.

**Test level:** Integration

## 4. Negative / Forbidden / Privacy Acceptance Criteria

AC-NEG-001 … AC-NEG-033 verify the 33 forbidden V1 behaviors of FRS §30 (TRACEABILITY_MATRIX.md §7, same numbering). AC-NEG-034 … AC-NEG-043 verify the 10 privacy boundaries of TRACEABILITY_MATRIX.md §5. "Absence" checks are made by schema inspection, route-table / OpenAPI inspection, permission-seed inspection, recurring-job registry inspection, UI inspection or architecture scan — whichever proves absence directly; runtime calls are used where a rejection behavior is specified.

Common canary method (AC-NEG-034 … AC-NEG-043): seed unique canary strings into every protected field, exercise every route of the audience under test, and scan JSON responses, the audience OpenAPI document, PDFs, CSV exports, rendered notifications, application logs, error bodies and audit views reachable by that audience. A pass means zero canary hits and zero forbidden property names.

### AC-NEG-001 — No Inventory

**Traces:** FRS §30 #1; FR-EXP-001, FR-EXP-002
**Type:** Negative
**Task:** 4

**Given**
- The migrated schema, route table, permission seed, enum catalogue and SPA.

**When**
- They are inspected; Sales records a Materials / Medical Supplies expense.

**Then**
- No inventory table and no column named or meaning stock, quantity, warehouse, reorder level or valuation exists.
- No inventory route, permission, enum value or UI control exists.
- "Materials / Medical Supplies" exists only as an `expenses.expense_categories` row; the expense creates no stock / item record.

**Test level:** DB / Security / UI

### AC-NEG-002 — No X-ray / imaging

**Traces:** FRS §30 #2; FR-CLN-005
**Type:** Negative
**Task:** 3

**Given**
- Schema, route table, recurring jobs, SPA.

**When**
- They are inspected; an imaging field is posted to the implant route.

**Then**
- No imaging table or column (none on `clinical.implant_details`), route, job or UI control exists; the request returns 400.

**Test level:** DB / API / UI

### AC-NEG-003 — No multi-branch

**Traces:** FRS §30 #3; FR-OPS-002
**Type:** Negative
**Task:** 2

**Given**
- Every schema, the route table and SPA.

**When**
- A catalogue query lists columns named `branch_id` and tables named like branch.

**Then**
- Zero `branch_id` columns, no branch table, no branch route, no branch selector, no cross-branch report.

**Test level:** DB / UI

### AC-NEG-004 — No Assistant login / account / role / audience / self-check-in / notification

**Traces:** FRS §30 #4; FR-AUTH-001, FR-STAFF-006, FR-SEC-010, FR-NOTIF-002
**Type:** Negative
**Task:** 2, 4

**Given**
- `identity.roles`, `staff.assistants`, OpenAPI documents, route table, notification recipient views.

**When**
- They are inspected; an Assistant is created; a login is attempted with the Assistant's name.

**Then**
- No ASSISTANT role; no `user_id` column on `staff.assistants`; no `identity.users` row is created.
- No Assistant OpenAPI document, route, dashboard or self-check-in route.
- No notification recipient resolves to an Assistant.
- Login fails with the generic invalid-credentials response.

**Test level:** DB / Security

### AC-NEG-005 — No patient portal / patient accounts

**Traces:** FRS §30 #5; FR-SEC-009
**Type:** Negative
**Task:** 2, 3

**Given**
- Schema and route table.

**When**
- They are inspected.

**Then**
- No patient account / credential table and no patient auth route; the anonymous route list equals exactly the six endpoints of AC-SEC-009.

**Test level:** DB / Security

### AC-NEG-006 — No patient mobile app

**Traces:** FRS §30 #6; FR-SEC-009
**Type:** Negative
**Task:** 2, 3

**Given**
- OpenAPI documents and route table.

**When**
- They are inspected.

**Then**
- No mobile API audience exists; the only patient-reachable routes are `GET` / `POST /api/public/feedback/{token}`.

**Test level:** Security

### AC-NEG-007 — No insurance integration

**Traces:** FRS §30 #7; FR-FIN-004
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table and payment-method seed.

**When**
- They are inspected; a payment with an insurance member or an `Insurance` method is posted.

**Then**
- No insurance table, column or route; payment methods are seeded exactly Cash, Visa, InstaPay.
- The request returns 400 (unknown member) / rejection (inactive or unknown method).

**Test level:** DB / API

### AC-NEG-008 — No digital consent / e-signature / consent storage

**Traces:** FRS §30 #8 (no FR)
**Type:** Negative
**Task:** 3

**Given**
- Schema, route table and SPA.

**When**
- They are inspected.

**Then**
- No consent table, consent / signature route, consent print endpoint or consent UI control exists.

**Test level:** DB / Security / UI

### AC-NEG-009 — No printed invoice / receipt / tax invoice

**Traces:** FRS §30 #9; FR-FIN-011, FR-FIN-004
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table and SPA, after a payment is recorded.

**When**
- They are inspected.

**Then**
- No receipt / invoice table, no print or PDF route for payments, no tax-invoice object, no print control; `PaymentConfirmationDto` is data only.

**Test level:** DB / Security / UI

### AC-NEG-010 — No expense attachments

**Traces:** FRS §30 #10; FR-EXP-006
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table, request limits and SPA.

**When**
- They are inspected; a multipart upload is posted to an expense route.

**Then**
- No attachment table, no file / blob column, no upload route or control; request size limit 1 MB with no upload handling; the upload is rejected.

**Test level:** DB / API / UI

### AC-NEG-011 — No Daily Closing / Close Day

**Traces:** FRS §30 #11; FR-REPORT-001
**Type:** Negative
**Task:** 4

**Given**
- Schema, route table, recurring-job registry, SPA.

**When**
- They are inspected.

**Then**
- No Close Day route, no sealed daily state column, no closing / report-generation background job, no UI control.

**Test level:** DB / Security / UI

### AC-NEG-012 — No feedback expiry

**Traces:** FRS §30 #12; FR-FDB-007, FR-NOTIF-009
**Type:** Negative
**Task:** 3

**Given**
- `feedback.*` schema, enums and the recurring-job registry.

**When**
- They are inspected.

**Then**
- No `expires_at` column, no Expired status / state, no J6 job; recurring jobs are exactly J1–J5 and J7.

**Test level:** DB / Operations

### AC-NEG-013 — No Arrived appointment status

**Traces:** FRS §30 #13; FR-SCH-005, FR-SCH-009
**Type:** Negative
**Task:** 3

**Given**
- Status enum, CHECK constraint, API and SPA.

**When**
- `Arrived` is written via API and directly in the DB; arrival is recorded; the SPA is inspected.

**Then**
- API → 400; DB CHECK rejects; recording arrival leaves the status unchanged; no "Arrived" status label exists in the UI.

**Test level:** API / DB / UI

### AC-NEG-014 — No Waiting / Checked-In status or CheckIn permission

**Traces:** FRS §30 #14; FR-SCH-005, FR-SCH-011, FR-REPORT-009
**Type:** Negative
**Task:** 3, 4

**Given**
- Status enum, permission seed, route table, Sales dashboard.

**When**
- They are inspected; `Waiting` / `CheckedIn` is submitted.

**Then**
- No such enum value (400 / CHECK); no `Scheduling.Appointments.CheckIn` permission; no check-in route; no Waiting Patients widget or queue table.

**Test level:** API / DB / UI

### AC-NEG-015 — No Walk-In commission pool

**Traces:** FRS §30 #15; FR-PAYROLL-004, FR-PAT-001
**Type:** Negative / Financial
**Task:** 4

**Given**
- Walk-In patient W (Original Sales = NULL) with a 1,000.00 collection; schema, routes, jobs.

**When**
- Sales payroll drafts are created; artefacts are inspected.

**Then**
- No commission item for any employee from W's collection; no pool table, pool payroll line, pool rate default, route or job exists.

**Test level:** Integration / DB

### AC-NEG-016 — No Doctor attribution holds

**Traces:** FRS §30 #16; FR-FIN-031, FR-FIN-027
**Type:** Negative
**Task:** 4

**Given**
- Schema, event-handler registry, route table.

**When**
- They are inspected; a patient is reassigned and a Doctor deactivated.

**Then**
- No hold / pending-attribution table; no `PatientReassigned` / `DoctorDeactivated` finance handler; no reassignment-triggered finance route; no finance row is created by either action.

**Test level:** DB / Security / Integration

### AC-NEG-017 — No `separatelyPricedWork` flag

**Traces:** FRS §30 #17; FR-CLN-008
**Type:** Negative
**Task:** 3

**Given**
- Schema, OpenAPI documents, pricing / treatment routes.

**When**
- `separatelyPricedWork` is sent; artefacts are inspected.

**Then**
- 400; no such column or OpenAPI property exists.

**Test level:** API / DB

### AC-NEG-018 — No self-service password reset

**Traces:** FRS §30 #18; FR-AUTH-004
**Type:** Negative
**Task:** 2

**Given**
- Route table, `identity.users` schema, message templates.

**When**
- `/api/auth/password-reset/*` is called; artefacts are inspected.

**Then**
- No route (404); no reset-token column or flag; no reset email / SMS message template.

**Test level:** Security / DB

### AC-NEG-019 — No Expense Finalized state

**Traces:** FRS §30 #19; FR-EXP-006
**Type:** Negative
**Task:** 4

**Given**
- `expenses.*` schema and route table.

**When**
- They are inspected; a finalize call is attempted.

**Then**
- No `finalized_at` column, no Finalized value, no finalize or expense-adjustment route.

**Test level:** DB / Security

### AC-NEG-020 — No percentage discounts

**Traces:** FRS §30 #20; FR-FIN-021, FR-OPS-004
**Type:** Negative
**Task:** 3, 4

**Given**
- `clinical.pricing_discount_entries` and the discount routes.

**When**
- A discount is sent as a percentage member or percentage string; the schema is inspected.

**Then**
- 400; no percentage column on discount entries; only fixed EGP amounts are stored.

**Test level:** API / DB

### AC-NEG-021 — No Ortho Total decrease handling

**Traces:** FRS §30 #21; FR-ORT-010
**Type:** Negative
**Task:** 4

**Given**
- A finalized ortho case and the route table.

**When**
- A lower Gross is submitted through the terms edit; a direct DB write lowering the total is attempted; routes are inspected.

**Then**
- 422 `ortho.total_decrease_not_supported`; nothing written; G9(b) rejects the direct write; no decrease route exists.
- Linked to deferred **Q-ORT-3b**; no decrease behavior is implemented.

**Test level:** API / DB

### AC-NEG-022 — No automatic installment regeneration / oldest-first / negative installment / deletion

**Traces:** FRS §30 #22; FR-ORT-006, FR-ORT-011, FR-ORT-012, FR-ORT-017, FR-ORT-019
**Type:** Negative
**Task:** 4

**Given**
- Ortho schema, grants, route table, recurring jobs and code.

**When**
- Deposit and Months are edited; a payment is allocated to an ortho charge; artefacts are inspected; a negative amount and a DELETE are attempted.

**Then**
- No job or route regenerates or redistributes installments; allocation applies nothing to installments automatically (no oldest-first code path — architecture scan).
- CHECK amount ≥ 0 rejects a negative installment.
- No DELETE route or grant on installment tables.

**Test level:** DB / Security / Integration

### AC-NEG-023 — No Manager initial work price / consultation fee / Fixed Lab / Ortho terms

**Traces:** FRS §30 #23; FR-CLN-007, FR-CLN-010, FR-LAB-006, FR-ORT-002
**Type:** Negative / Permission
**Task:** 3, 4

**Given**
- The permission seed and revision-1 CHECKs.

**When**
- The Manager calls the work-price, consultation-fee, initial Fixed Lab and ortho-create routes; a revision 1 with role `MANAGER` is inserted directly.

**Then**
- `Clinical.WorkItems.EnterPrice`, `Clinical.Visits.EnterConsultationFee`, `Finance.FixedLab.SetInitial` and `Orthodontics.Terms.SetInitial` are not mapped to MANAGER; each call → 403.
- Revision-1 role CHECKs reject the direct insert.

**Test level:** Security / DB

### AC-NEG-024 — No recalculation of old Sales commission

**Traces:** FRS §30 #24; FR-PAYROLL-003, FR-STAFF-004
**Type:** Negative / Financial
**Task:** 4

**Given**
- Commission items at 5 %; route table and job registry.

**When**
- The rate changes to 7 %; drafts are regenerated; artefacts are inspected; a second item for the same allocation is inserted.

**Then**
- Existing items unchanged; no recalculation route or job; the insert is rejected (items unique per allocation).

**Test level:** DB / Integration

### AC-NEG-025 — No Assistant bonus / deduction / commission / incentive / proration / `final-amount` route

**Traces:** FRS §30 #25; FR-PAYROLL-015, FR-PAYROLL-016, FR-STAFF-006
**Type:** Negative
**Task:** 4

**Given**
- `payroll.assistant_*` and `staff.assistants` schema, route table, code.

**When**
- They are inspected; these members are posted to Assistant routes; a mid-month draft is created.

**Then**
- No such columns or routes (incl. no `final-amount` route); members → 400; Base Pay defaults to the full salary snapshot (no proration code path).

**Test level:** DB / API

### AC-NEG-026 — No attendance-based deductions / inferred overtime / Sales overtime / Doctor attendance

**Traces:** FRS §30 #26; FR-STAFF-012, FR-STAFF-014, FR-STAFF-015, FR-STAFF-017
**Type:** Negative
**Task:** 4

**Given**
- Payroll, staff and attendance schema; route table.

**When**
- They are inspected; an attendance record for a Doctor and an overtime entry for a Sales employee are submitted.

**Then**
- No FK from any payroll table to attendance; no Sales overtime table, column or route; no Doctor attendance column; both submissions rejected.

**Test level:** DB / API

### AC-NEG-027 — No reopen-Visit route; no Manager approval / clinical correction

**Traces:** FRS §30 #27; FR-CLN-013, FR-CLN-014, FR-RX-004
**Type:** Negative / Permission
**Task:** 3

**Given**
- Route table, Visit status enum, permission seed.

**When**
- They are inspected; the Manager calls the Visit and prescription correction routes.

**Then**
- No reopen route; no approval state; `Clinical.Visits.CorrectFinalized` and `Prescriptions.CorrectFinalized` are not mapped to MANAGER (403).

**Test level:** Security

### AC-NEG-028 — No Doctor notifications / dashboard / feedback access

**Traces:** FRS §30 #28; FR-NOTIF-007, FR-REPORT-009, FR-FDB-011, FR-SEC-011
**Type:** Negative / Permission
**Task:** 3, 4

**Given**
- Permission seed and route table.

**When**
- A Doctor calls the inbox, unread count, Sales dashboard and Manager feedback routes; Doctor routes are listed.

**Then**
- No notification, dashboard or feedback permission is mapped to DOCTOR (403 on each); no Doctor dashboard route exists.

**Test level:** Security

### AC-NEG-029 — No language switch / non-Arabic UI

**Traces:** FRS §30 #29; FR-OPS-001
**Type:** Negative / UI
**Task:** 2, 5

**Given**
- The SPA build.

**When**
- Every screen is rendered and the UI is searched for a locale control.

**Then**
- No locale selector or language-switch control; SPA root `dir="rtl" lang="ar"`; no non-Arabic user-facing strings in production screens (string-resource scan).

**Test level:** UI

### AC-NEG-030 — No automated WhatsApp / SMS / email delivery

**Traces:** FRS §30 #30; FR-NOTIF-001, FR-FDB-004
**Type:** Negative
**Task:** 3

**Given**
- The notification channel registry and job registry.

**When**
- They are inspected; notifications are dispatched; a feedback link is marked sent.

**Then**
- Only `InAppChannel` is registered; no external channel job, SMTP / SMS / WhatsApp client or outbound call occurs.

**Test level:** Security / Integration

### AC-NEG-031 — No unconfirmed proposal objects

**Traces:** FRS §30 #31; FR-CLN-011, FR-OPS-002
**Type:** Negative
**Task:** 2, 3, 4

**Given**
- Source code, schema, enums, routes and jobs.

**When**
- An architecture scan searches for the named proposals.

**Then**
- No type, table, enum value, route or job named / implementing `ILabAllocationStrategy`, `CaseDoctorAllocation`, TreatmentPlan / FinancialAgreement workflow, OnHold / Abandoned case states, addendum workflow, `PatientContact` aggregate, Manager approval workflows, price lists, deviation reports or Doctor-share clawback rules.

**Test level:** Security (architecture scan) / DB

### AC-NEG-032 — No generic Medical History / extra medical fields

**Traces:** FRS §30 #32; FR-MED-001
**Type:** Negative
**Task:** 2

**Given**
- `patients.patients`, `patients.patient_medical_histories`, DTOs and OpenAPI.

**When**
- They are inspected; `medicalHistory`, `chronicDiseases`, `previousSurgeries`, allergy type, medication names, surgery details or "other condition" members are posted.

**Then**
- No such column on `patients.patients` or the intake table; no such DTO / OpenAPI property; requests → 400.

**Test level:** DB / API

### AC-NEG-033 — No rendered diagram artifacts in the design package

**Traces:** FRS §30 #33 (no FR); OWN Change 11
**Type:** Negative / Repository hygiene
**Task:** 1 (final audit), 5

**Given**
- The repository working tree and git index.

**When**
- A file-system / `git ls-files` check searches for `*.png`, `*.jpg`, `*.svg` and any rendered-output folder (e.g. `docs/rendered-diagrams/`) in the design package.

**Then**
- Expected state: no rendered diagram artifact (PNG / JPG / SVG) and no rendered-diagrams folder is tracked in the design package; diagrams exist only as Mermaid source.

**Current status:** REQUIRES VERIFICATION / CLEANUP during the Task 1 final audit. TRACEABILITY_MATRIX.md records that the repository currently appears to track `docs/rendered-diagrams/` (PNG / SVG) and `docs/README (1).md`. This is a known repository / package-hygiene item, **not** a new business requirement and **not** a business blocker; no file is changed by this step.

**Test level:** Operations (repository check)

### AC-NEG-034 — Privacy canary: Doctor contact-data denial

**Traces:** Privacy rule "Doctor contact-data denial"; FR-SEC-002, FR-PAT-004, FR-PAT-007, FR-SCH-011, FR-SCH-014, FR-MED-003, FR-RX-003
**Type:** Privacy / Negative
**Task:** 2, 3, 5

**Given**
- Canaries in patient phone, WhatsApp, email, address, CRM / contact notes, appointment `sales_notes`, Original Sales Representative and patient source.

**When**
- The canary method runs over the Doctor audience: every `/api/doctor/*` JSON response (search, patient view, queue, appointments, intake, timeline, referrals, own work, payroll), the Doctor OpenAPI document, the printed prescription PDF (text and metadata), any CSV reachable by a Doctor, notifications, logs and error responses.

**Then**
- Zero canary hits; the Doctor OpenAPI document contains no contact property name (`phone`, `whatsapp`, `email`, `address`, `crmNotes`, `salesNotes`, `originalSalesEmployee`, `patientSource`).
- Contact-bearing endpoints return 403 to a Doctor; a phone typed into Doctor search finds nothing.

**Test level:** Security

### AC-NEG-035 — Privacy canary: Sales clinical-note denial

**Traces:** Privacy rule "Sales clinical-note denial"; FR-SEC-003, FR-CLN-015, FR-FIN-022, FR-MED-008, FR-RX-002
**Type:** Privacy / Negative
**Task:** 2, 3, 5

**Given**
- Canaries in Visit clinical notes, root canal measurements, procedure details / teeth / steps, and prescription medication details.

**When**
- The canary method runs over the Sales audience: every `/api/sales/*` response (patient, clinical summary, pricing lines, intake, appointments, referrals, ortho, balances, dashboard), the Sales OpenAPI document, notifications, logs and error responses; Sales calls clinical, measurement and prescription routes.

**Then**
- Zero canary hits; no clinical property name in the Sales OpenAPI document; those routes → 403; Sales cannot print prescriptions (403).

**Test level:** Security

### AC-NEG-036 — Privacy canary: Sales referral-notes denial

**Traces:** Privacy rule "Sales referral-notes denial"; FR-REF-003, FR-REF-002
**Type:** Privacy / Negative
**Task:** 3, 5

**Given**
- A referral with reason "R-canary" and Doctor notes "N-canary".

**When**
- The canary method runs over the Sales audience; Sales calls the Doctor referral route; Sales posts a body containing `notes`.

**Then**
- "R-canary" appears in Sales referral DTOs (allowed); "N-canary" and a `notes` property appear nowhere in Sales responses, OpenAPI, exports, notifications, logs or errors.
- Doctor referral route → 403 for Sales; `notes` member → 400.

**Test level:** Security

### AC-NEG-037 — Privacy canary: Medical History value protection

**Traces:** Privacy rule "Medical History value protection"; FR-MED-006, FR-MED-002, FR-MED-003, FR-AUDIT-003, FR-SEC-007, FR-REPORT-007
**Type:** Privacy / Negative
**Task:** 2, 4, 5

**Given**
- An intake with canary values (unique Last Dental Visit text and changed flags).

**When**
- Audit rows, application logs, notification payloads, Manager reports, CSV / PDF exports and error-tracking events are scanned; Worker and Assistant access is checked.

**Then**
- Intake values appear only in the Sales intake DTO and the Doctor read-only intake DTO.
- Audit rows hold changed field names only; no canary in logs, notifications, reports, exports or errors.
- The Worker DB role cannot read the intake table; no Assistant principal exists.

**Test level:** Security

### AC-NEG-038 — Doctor own-finance restriction

**Traces:** Privacy rule "Doctor own-finance restriction"; FR-FIN-029, FR-PAYROLL-011, FR-SEC-005
**Type:** Privacy / Negative
**Task:** 4, 5

**Given**
- D1 and D2 with work on the same case and payrolls; clinic-level totals.

**When**
- D1 calls own-work and own-payroll routes with and without identity parameters, and requests D2's item / payroll ids.

**Then**
- Only D1's items and payroll; D2's resources → 404; no case / patient / clinic totals, other Doctors' revenue or percentages, clinic profit, expenses, Sales payroll or management reports appear; the Manager calling the own routes → 404.

**Test level:** Security

### AC-NEG-039 — Sales protected-finance restriction

**Traces:** Privacy rule "Sales protected-finance restriction"; FR-SEC-003, FR-FIN-010, FR-FIN-022, FR-LAB-005, FR-PAYROLL-013, FR-STAFF-004
**Type:** Privacy / Negative
**Task:** 2, 4, 5

**Given**
- Canaries in Doctor percentage, Doctor Share, entitlement, Clinic Share, General / Fixed lab amounts and percentages, other employees' salaries / commission, and commission-rate history.

**When**
- The canary method runs over the Sales audience; Sales calls the commission-history route and another employee's payroll.

**Then**
- Zero canary hits; no such property names in Sales OpenAPI; commission-history → 403; another employee's payroll → 404.

**Test level:** Security

### AC-NEG-040 — Assistant no-access rule

**Traces:** Privacy rule "Assistant no-access rule"; FR-AUTH-001, FR-STAFF-006, FR-SEC-010, FR-NOTIF-002
**Type:** Privacy / Negative
**Task:** 2, 4, 5

**Given**
- Assistant records with canaries in name, salary, attendance and overtime.

**When**
- Identity tables, notification recipients and every Doctor / Sales response are inspected; Doctor / Sales call Assistant routes.

**Then**
- No Assistant principal (no user, role or session possible); no notification to an Assistant.
- No Assistant canary in any Doctor or Sales response; Assistant routes → 403 for Doctor and Sales.

**Test level:** Security

### AC-NEG-041 — Patient no-login rule

**Traces:** Privacy rule "Patient no-login rule"; FR-SEC-009, FR-FDB-005, FR-FDB-006
**Type:** Privacy / Negative
**Task:** 2, 3, 5

**Given**
- Route table, schema and the public feedback endpoints.

**When**
- They are inspected; the public endpoints are called.

**Then**
- No patient login, account, portal or app route or table; the public feedback endpoints require only the token, return no patient identifier and accept no identifying input.

**Test level:** Security

### AC-NEG-042 — Sales expense / Manager analytics denial

**Traces:** Privacy rule "Sales expense / Manager analytics denial"; FR-EXP-002, FR-EXP-003, FR-SEC-011
**Type:** Privacy / Negative / Permission
**Task:** 4, 5

**Given**
- Expenses created by Sales and the Manager.

**When**
- Sales calls expense list, read, edit, delete, totals, category-management, Manager report, dashboard and finance-summary routes.

**Then**
- 403 / no route for each; Sales can only create an expense (`ExpenseCreatedDto` echo) and read the allowed-categories lookup `[{ id, name }]`.

**Test level:** Security

### AC-NEG-043 — Feedback results Manager-only

**Traces:** Privacy rule "Feedback results Manager-only"; FR-FDB-011, FR-FDB-002
**Type:** Privacy / Negative
**Task:** 3, 5

**Given**
- Feedback entries with canary comments and ratings from both sources.

**When**
- The canary method runs over the Doctor and Sales audiences (including the Sales feedback queue); Doctor and Sales call the Manager feedback route.

**Then**
- Zero canary hits and no rating / comment field in any Doctor or Sales response; the Manager feedback route → 403 for both.

**Test level:** Security

## 5. End-to-End Flow Acceptance Map

Map only; no new AC IDs. Primary IDs are the FLOW rows of TRACEABILITY_MATRIX.md §4. Each FLOW is executed end-to-end in Task 5 against real PostgreSQL with guards active (FRS §32 item 2); a FLOW passes when every listed primary and negative criterion passes within the run and the final observable result holds.

| FLOW | Name | Primary Acceptance IDs | Critical negative IDs | Expected final observable result |
|---|---|---|---|---|
| FLOW-01 | Lead → Patient | AC-CRM-001, AC-CRM-002, AC-CRM-005, AC-CRM-006, AC-CRM-007, AC-NOTIF-005, AC-CRM-004, AC-PAT-002, AC-PAYROLL-001, AC-PAYROLL-002, AC-PAYROLL-003, AC-PAYROLL-004, AC-PAYROLL-005 | AC-NEG-024, AC-NEG-034 | Lead converted to a Lead-sourced patient with permanent Original Sales; later collections produce commission items at the rate in force at `allocated_at`; Lead data never reaches Doctors. |
| FLOW-02 | Walk-In → Patient | AC-PAT-001, AC-MED-002, AC-MED-007, AC-SCH-003, AC-PAYROLL-004 | AC-NEG-015, AC-NEG-037 | Walk-In patient (Original Sales NULL) registered and booked with or without intake; collections create zero commission and no pool. |
| FLOW-03 | Appointment → Arrival → Doctor Queue → Visit | AC-SCH-001, AC-SCH-002, AC-SCH-003, AC-SCH-004, AC-NOTIF-003, AC-SCH-006, AC-SCH-007, AC-SCH-009, AC-SCH-011, AC-MED-003, AC-PAT-007, AC-CLN-001, AC-SCH-012 | AC-NEG-013, AC-NEG-014, AC-NEG-034 | Overlapping booking accepted; arrival recorded as an event (status unchanged); patient appears in own Doctor's derived queue without contact data; Visit start sets In Treatment and removes the queue item. |
| FLOW-04 | Visit → Priced Work → Finalize → Charge | AC-CLN-002, AC-CLN-003, AC-CLN-004, AC-CLN-005, AC-CLN-006, AC-CLN-007, AC-CLN-008, AC-CLN-009, AC-CLN-010, AC-LAB-006, AC-FIN-021, AC-FIN-022, AC-FIN-023, AC-RX-001, AC-REF-001, AC-SCH-008, AC-CLN-012, AC-FIN-001, AC-FIN-024, AC-SCH-013, AC-FDB-001, AC-CLN-014, AC-RX-004 | AC-NEG-017, AC-NEG-020, AC-NEG-023, AC-NEG-027, AC-NEG-035 | One finalize transaction: Visit Finalized, one Treatment charge per priced work item + Consultation charge with discount snapshot, appointment Completed with unused time released, feedback request + notification; follow-up appointment owned by the authoring Doctor with manual duration; later corrections only through the Doctor's controlled path. |
| FLOW-05 | Payment → Allocation → Distribution | AC-FIN-004, AC-FIN-005, AC-FIN-006, AC-FIN-018, AC-LAB-002, AC-FIN-028, AC-LAB-008, AC-FIN-019, AC-FIN-009, AC-FIN-020 | AC-NEG-007, AC-NEG-009, AC-NEG-039 | Payment = Σ allocations + credit; entitlement = allocated × snapshot %; General lab at allocation-time rate; Fixed lab Lab-First; Consultation no entitlement; G8 / G8b hold; Clinic Share ≥ 0. |
| FLOW-06 | Patient Credit | AC-FIN-006, AC-FIN-008, AC-FIN-007, AC-FIN-012 | AC-NEG-039 | Overpayment / advance becomes credit (liability, no revenue / entitlement / commission); later application allocates and distributes at application time; credit never negative; refund of credit has no distribution effect. |
| FLOW-07 | Fixed Lab | AC-CLN-007, AC-LAB-005, AC-LAB-006, AC-LAB-007, AC-LAB-008, AC-LAB-009, AC-LAB-010 | AC-NEG-023, AC-NEG-039 | Owner case 2,000 / 3,000 / 5,000 with L 3,000 and 40 % ends Lab 3,000, Doctor 4,000, Clinic 3,000; pending entitlement recognized FIFO; revision deltas land in the current open payroll. |
| FLOW-08 | Orthodontics | AC-ORT-001, AC-ORT-002, AC-FIN-021, AC-ORT-003, AC-ORT-004, AC-FIN-004, AC-ORT-006, AC-ORT-007, AC-NOTIF-004, AC-ORT-008, AC-ORT-009, AC-ORT-010, AC-ORT-011, AC-ORT-012, AC-ORT-013, AC-ORT-014, AC-ORT-015, AC-ORT-016, AC-ORT-017, AC-ORT-018, AC-ORT-019, AC-ORT-020 | AC-NEG-021, AC-NEG-022, AC-NEG-023 | 20,000 − 2,000 = 18,000 billed; Deposit 5,000 + 5 × 2,600 generated once at finalization; manual application; increase adds one PriceChange installment; decrease rejected `ortho.total_decrease_not_supported`; Manager batch ends balanced with paid history intact. |
| FLOW-09 | Doctor Payroll | AC-FIN-028, AC-PAYROLL-009, AC-FIN-030, AC-PAYROLL-010, AC-PAYROLL-008, AC-PAYROLL-011 | AC-NEG-016, AC-NEG-038 | Earnings recognized in a month are paid once in that month's Doctor payroll (including inactive Doctors); pending entitlement excluded; finalized payroll sealed; Doctor reads own payroll only. |
| FLOW-10 | Sales Payroll | AC-STAFF-003, AC-STAFF-004, AC-PAYROLL-001, AC-PAYROLL-002, AC-PAYROLL-003, AC-PAYROLL-004, AC-PAYROLL-005, AC-PAYROLL-006, AC-PAYROLL-007, AC-PAYROLL-012, AC-PAYROLL-008, AC-PAYROLL-013 | AC-NEG-015, AC-NEG-024, AC-NEG-039 | 50 + 70 = 120 commission across a 5 % → 7 % change; reversal of the first = −50; Walk-In 0; inactive Sales no new items; net salary = base + commission + bonus − deductions; Sales reads own payroll only. |
| FLOW-11 | Assistant Attendance → Overtime → Payroll | AC-STAFF-006, AC-STAFF-007, AC-STAFF-008, AC-STAFF-012, AC-STAFF-013, AC-STAFF-014, AC-STAFF-015, AC-STAFF-016, AC-PAYROLL-014, AC-PAYROLL-015, AC-PAYROLL-016, AC-REPORT-005 | AC-NEG-004, AC-NEG-025, AC-NEG-026, AC-NEG-040 | Manager-maintained attendance changes no pay; approved overtime 5 h × 100 = 500 snapshotted; Final = Base Pay + Overtime (6,000 + 500 = 6,500; partial month manual Base Pay); payroll in reports only for full months. |
| FLOW-12 | Referral → Follow-Up | AC-REF-001, AC-NOTIF-006, AC-REF-003, AC-CRM-005, AC-CRM-006, AC-REF-004 | AC-NEG-036 | Referral created with notification; Sales sees type / reason / instructions but never notes; referral status and follow-up status progress independently. |
| FLOW-13 | Patient Website Feedback | AC-FDB-001, AC-FDB-002, AC-FDB-003, AC-FDB-004, AC-FDB-005, AC-FDB-006, AC-FDB-007, AC-FDB-011 | AC-NEG-012, AC-NEG-030, AC-NEG-041, AC-NEG-043 | Token link prepared and manually sent; patient submits anonymously at any later date; request Submitted; only the Manager reads the result. |
| FLOW-14 | Sales-Entered Feedback | AC-FDB-008, AC-FDB-010, AC-FDB-009, AC-FDB-011 | AC-NEG-028, AC-NEG-043 | Sales-entered feedback links the Visit's existing request (Submitted) or is general; exactly one accepted result per request; only the Manager reads results. |
| FLOW-15 | Refund / Financial Correction | AC-FIN-012, AC-FIN-013, AC-FIN-014, AC-FIN-015, AC-FIN-016, AC-LAB-004, AC-PAYROLL-006, AC-FIN-017, AC-ORT-019 | AC-NEG-021, AC-NEG-022 | Manager-only corrections append rows; propagation at recorded lab % and original commission rate; negative earnings in the current open payroll; sealed payrolls unchanged; generic adjustments refused on ortho charges. |
| FLOW-16 | Inactive Sales Redistribution | AC-STAFF-009, AC-CRM-010, AC-CRM-011, AC-CRM-009, AC-PAYROLL-005, AC-CRM-012 | AC-NEG-015, AC-NEG-024 | Open Leads / Follow-Ups redistributed evenly (counts differ ≤ 1) or unassigned for the Manager; Original Sales unchanged; no new commission for the inactive employee and none transferred. |

## 6. Acceptance Coverage Report

Computed mechanically from this file, docs/TRACEABILITY_MATRIX.md and docs/FRS.md.

| Check | Result |
|---|---|
| Primary AC IDs expected (allocated in the matrix) | 237 |
| Primary AC IDs defined | 237 |
| Missing primary IDs | 0 |
| Duplicate primary IDs | 0 |
| Unknown primary IDs | 0 |
| Primary ACs tracing to a different FR than `FR-XXX-nnn` | 0 |
| Negative AC IDs expected | 43 |
| Negative AC IDs defined | 43 |
| Missing negative IDs | 0 |
| Duplicate negative IDs | 0 |
| Unknown negative IDs | 0 |
| Total unique AC IDs expected | 280 |
| Total unique AC IDs defined | 280 |
| AC IDs referenced but not allocated in the matrix | 0 |
| FRs represented | 237 / 237 |
| FLOWs mapped | 16 / 16 (primary IDs identical to matrix §4) |
| Forbidden items represented (AC-NEG-001 … 033, FRS §30 numbering) | 33 / 33 |
| Privacy boundaries with negative canary criteria (AC-NEG-034 … 043) | 10 / 10 |
| Criteria missing Given / When / Then / Type / Task / Test level | 0 |
| Stable error codes used that are not defined in the FRS | 0 |
| Deferred items | 2 — Q-ORT-3b (V1 rejection: AC-ORT-010, AC-NEG-021); OPS-1 (outside AC-OPS-007 until go-live) |
| Unresolved acceptance gaps | 0 |

**Notes (not acceptance gaps):**

- AC-NEG-033 is a repository / package-hygiene criterion. It is expected to fail until the tracked rendered-diagram files recorded in TRACEABILITY_MATRIX.md are resolved during the Task 1 final audit; no file was changed in this step.
- Explicit numerical proofs: Fixed Lab Owner case (AC-LAB-008), commission 50 + 70 = 120 and reversal −50 (AC-STAFF-004, AC-PAYROLL-003, AC-PAYROLL-006), Walk-In zero commission (AC-PAYROLL-004, AC-NEG-015), Ortho 20,000 / 2,000 → 18,000 and 5 × 2,600 (AC-ORT-003, AC-ORT-004), PriceChange 22,000 (AC-ORT-009), General Lab 800 + 300 + 900 (AC-LAB-002), Assistant 6,000 + 500 = 6,500 and 3,500 + 400 = 3,900 (AC-PAYROLL-015, AC-PAYROLL-016).
- Latest Owner decisions represented: 11 expense categories and Sales `{ categoryId, amount, specification }` create-only entry (AC-EXP-001, AC-EXP-002); Doctor follow-up `durationMinutes` required 1..480 with no default (AC-SCH-008); 8 Lead statuses (AC-CRM-001); 6 Follow-Up sources (AC-CRM-005); 8 Sales dashboard items (AC-REPORT-009).
