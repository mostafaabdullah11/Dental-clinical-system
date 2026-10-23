# FRS.md — Functional Requirements Specification (V1)

**Design Baseline:** 1.1 — OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Document status:** TASK 1 / STEP 1.1 — FRS COMPLETE
**Authoritative sources:** OWNER_REVIEW_NOTES.md, DESIGN_BASELINE.md, IMPLEMENTATION_GUARDRAILS.md, SECURITY_DESIGN.md, FINANCE_DESIGN.md, DATABASE_DESIGN.md, API_ARCHITECTURE.md, OPEN_QUESTIONS.md, DESIGN_FREEZE.md. The frozen Design Baseline v1.1 remains the main source (source priority: OWNER_REVIEW_NOTES.md → DESIGN_BASELINE.md → the module documents).
**Owner decisions recorded in this FRS:** explicit Owner decisions made after the freeze, during the FRS phase, are recorded in this FRS and override the older rule **only where specifically stated** (the Doctor follow-up appointment duration in FR-SCH-008, the explicit Lead statuses in FR-CRM-001, the Follow-Up sources in FR-CRM-005, the Sales dashboard items in FR-REPORT-009, the expense category catalogue in FR-EXP-001 and the Sales operational expense entry in FR-EXP-002). No other frozen behavior is changed, and the frozen baseline is not redesigned or reopened. In every other case, where this FRS and a frozen document appear to differ, the frozen document wins.

Abbreviations used in the source column of requirements: OWN = OWNER_REVIEW_NOTES.md, BASE = DESIGN_BASELINE.md, GRD = IMPLEMENTATION_GUARDRAILS.md, SEC = SECURITY_DESIGN.md, FIN = FINANCE_DESIGN.md, DB = DATABASE_DESIGN.md, API = API_ARCHITECTURE.md, OQ = OPEN_QUESTIONS.md, FRZ = DESIGN_FREEZE.md.

---

## 1. Purpose

This document converts the frozen Design Baseline v1.1 into implementation-ready functional requirements. Each requirement has a stable, unique ID that later artifacts (traceability matrix, acceptance criteria, tests) will reference. The FRS adds **no** business behavior of its own: every rule here is taken from the frozen package, except the explicit post-freeze Owner decisions listed in the header, which override the older rule only where stated. Where a frozen document is silent, the field says `N/A` or the requirement states that the behavior is **not built**. Technical interpretations T1–T14 (OQ §3) and technical defaults are marked **[T]** / **[DEFAULT]** where they shape a requirement.

## 2. Scope

**In scope (V1):** a single-clinic, single-branch dental operations system with an Arabic-only, right-to-left web UI and a language-neutral backend API, covering: authentication and Manager-administered accounts; staff (Doctors, Sales / Reception, Assistants as staff records only); patients and the structured Medical History Intake; CRM (Leads, Follow-Ups, Referrals); scheduling (availability, appointments, arrival event, Doctor queue); clinical visits, treatment cases, the procedure catalog and per-Doctor priced work; prescriptions; charges, payments, allocations, Patient Credit, discounts and Manager corrections; Doctor entitlement and General / Fixed lab distribution; orthodontic terms and installment schedules; Sales commission; clinic operating expenses; Manager-maintained attendance and Assistant overtime; monthly Doctor / Sales / Assistant payroll; feedback (website token and Sales-entered); internal notifications and background jobs; Manager reports; privacy, authorization and audit.

**Out of scope:** everything listed in Section 30. **Deferred:** exactly the two items in Section 31.

## 3. Actors

| Actor | Kind | Application access | Summary of authority (detail: SEC §4) |
|---|---|---|---|
| **Manager** | Application role `MANAGER` | Yes (one account per person, one role) | Highest operational authority; creates accounts and sets passwords; publishes availability; configures percentages, rates, lab %, settings; corrections / revisions (never initial price, consultation fee, Fixed Lab or Ortho terms); payroll; expenses; attendance; overtime; reports; audit; feedback results. Never edits history destructively. |
| **Doctor** | Application role `DOCTOR` | Yes | Reads all patients' clinical history and the Medical History Intake (read-only); works own Visits; enters own work price, consultation fee, initial Fixed Lab, initial Ortho terms; may set Discount on own line; finalizes own Visit; corrects own finalized Visit / Prescription through the controlled path; own work finance and own payroll. Never receives patient contact data. No notifications, no dashboard. |
| **Sales / Reception** | Application role `SALES_RECEPTION` | Yes | Registers patients; contact data; Medical History Intake (collect / edit); booking, Confirm / Cancel / No Show / Reschedule; records arrival; Leads (My Leads), Follow-Ups, referral follow-up; Discount before finalization through a pricing view; payments and credit application; manual ortho installment application; Materials / Petty expenses (create only); feedback queue and Sales-entered feedback; own payroll; Sales dashboard; notifications. Never receives Doctor-authored clinical detail, referral notes, Doctor finance or lab data. |
| **Assistant** | **Staff type only** | **None** (no account, login, role, user_id, dashboard, notification, API audience) | Has a staff record, attendance, overtime and monthly payroll maintained by the Manager. |
| **Patient** | Business entity | None (no login, portal or app) | Only external interaction: submits feedback through the clinic-website token form (anonymous public endpoint). |
| **System (Worker)** | Background process | System principal | Runs jobs J1–J5 and J7; resolves recipients only through `identity.v_active_user_roles` and `staff.v_sales_employee_activity`. |
| **Operator** | Developer / system owner (not an application role) | Server shell only | Runs migrations, break-glass `set-manager-password`, `post-restore-lockdown`. |

---

## 4. Global Functional Rules

Global rules that apply to every module. Cross-cutting privacy, audit and historical-integrity rules are specified in Sections 27–28 and apply globally as well.

### FR-OPS-001

**Title:** Arabic-only, right-to-left user interface  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Any UI screen, validation message, report, export or application-generated printable document is shown.  
**Main Flow:** The SPA renders in Arabic, right-to-left from the first screen; the SPA maps stable API error `code`s to Arabic messages.  
**Business Rules:** Arabic only; no language switch; no bilingual mode (Q-UI-1). The API stays language-neutral (stable codes, no UI strings). Reports, exports and the prescription print layout use Arabic application text. Notification texts are Arabic.  
**Validation / Rejection:** N/A  
**Result:** Root document has `dir="rtl"` and `lang="ar"`; `GET /api/me` returns locale `ar` / `rtl`.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `configuration.settings` (clinic header details in Arabic)  
**Related API Capability:** `GET /api/me`; SPA RTL contract (API A11 #37)

### FR-OPS-002

**Title:** Single clinic, single branch  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** N/A (structural rule)  
**Main Flow:** N/A  
**Business Rules:** V1 serves one clinic and one branch (Q-BRN-1). No `branch_id`, branch module, branch selector or cross-branch report.  
**Validation / Rejection:** N/A  
**Result:** No branch concept exists anywhere.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** No table has `branch_id` (DB test (12))  
**Related API Capability:** No branch route (API A9)

### FR-OPS-003

**Title:** Configurable clinic timezone and clinic-local calendar dates  
**Actors:** Manager (configures); System  
**Preconditions:** Manager holds `Configuration.Manage`.  
**Trigger:** Manager changes settings; any date-based rule is evaluated.  
**Main Flow:** The clinic timezone is stored in `configuration.settings.clinic_timezone` (IANA; [DEFAULT] `Africa/Cairo`). All calendar-date rules (today, tomorrow, due dates, payroll months, report ranges, attendance dates, arrival day) use the clinic-local date.  
**Business Rules:** Missing or invalid timezone → jobs fail loudly and guess nothing. Date ranges are clinic-local inclusive dates converted to half-open UTC ranges.  
**Validation / Rejection:** Invalid timezone value rejected.  
**Result:** Consistent clinic-local date semantics.  
**Audit / History:** A timezone change is audited.  
**Permissions / Privacy:** `Configuration.Manage` (Manager only).  
**Related Data:** `configuration.settings`  
**Related API Capability:** `GET/PUT /api/manager/settings`; `GET /api/me` (returns clinic timezone)

### FR-OPS-004

**Title:** Money representation  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** Any monetary or percentage value is entered, stored, computed or returned.  
**Main Flow:** Money is EGP, stored `numeric(12,2)`, computed with `decimal`, transported as strings with two decimals; percentages `numeric(5,2)` within 0..100.  
**Business Rules:** No floating point anywhere. Rounding is half-up to 2 decimals where a formula applies. Discounts are fixed EGP amounts only (never percentages).  
**Validation / Rejection:** Amounts with more than two decimals rejected; money must be positive where the operation requires it.  
**Result:** Exact monetary arithmetic.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** All money columns  
**Related API Capability:** API A1 (money as strings), A4, A11 #32

### FR-OPS-005

**Title:** Optimistic concurrency and idempotency on writes  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** Resource is editable.  
**Trigger:** PUT / PATCH / state-changing POST on an editable resource; payment, credit-application or correction request.  
**Main Flow:** Editable resources return an ETag; writes require `If-Match`. Payments, credit application and correction routes require an `Idempotency-Key`. State transitions are idempotent when already in the target state.  
**Business Rules:** Same idempotency key + same payload → original result; same key + different payload → 409.  
**Validation / Rejection:** Missing `If-Match` → 428; stale → 412; idempotency mismatch → 409 `payment.idempotency_mismatch`; missing key → 400.  
**Result:** Concurrent edits produce exactly one success; no duplicate payment.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `xmin` row version; `payments.idempotency_key` (unique)  
**Related API Capability:** API A7

### FR-OPS-006

**Title:** Error responses with stable codes  
**Actors:** All callers  
**Preconditions:** N/A  
**Trigger:** Any refused or failed request.  
**Main Flow:** Every error is RFC 7807 problem+json with `type`, `title`, `status`, stable `code`, `traceId`, and field-level codes for validation errors.  
**Business Rules:** Messages are generic; never echo input values, SQL or stack traces. Validation errors name the field and a code, never the value.  
**Validation / Rejection:** Unknown request members → 400.  
**Result:** The SPA branches on `code` and shows the Arabic message.  
**Audit / History:** N/A  
**Permissions / Privacy:** No personal or medical data in any error body.  
**Related Data:** N/A  
**Related API Capability:** API A6 (status codes 400/401/403/404/409/412/428/422/429/500)

### FR-OPS-007

**Title:** Encrypted backups and recovery-key custody  
**Actors:** Operator; Clinic Manager (as a person)  
**Preconditions:** Production deployment.  
**Trigger:** Scheduled backup.  
**Main Flow:** Backups are encrypted client-side with `age` to two independent recovery recipients; either private key alone decrypts.  
**Business Rules:** The Clinic Manager, as a person, holds both recovery private keys (OPS-1a). A recovery private key is never stored on the production VM. Holding a key is not an application permission. The exact physical storage locations are **OPS-1 — DEFERRED** (Section 31).  
**Validation / Rejection:** N/A  
**Result:** Off-site encrypted backups; RPO 5 min / RTO 4 h for the realistic failures (OPS-4).  
**Audit / History:** N/A  
**Permissions / Privacy:** `clinic_backup` cannot read `infra.data_protection_keys`.  
**Related Data:** Backup dump (key-table data excluded)  
**Related API Capability:** N/A (operations)

### FR-OPS-008

**Title:** Post-restore account lockdown  
**Actors:** Operator; Manager  
**Preconditions:** A restore to an earlier point was performed.  
**Trigger:** Restore completed, before the system is reopened.  
**Main Flow:** Operator runs `post-restore-lockdown`: (1) rotate every security stamp; (2) lock every non-Manager account; operator runs `set-manager-password`; the Manager then sets a new password for each employee through the normal Set Password action (clears lockout); (3) re-apply deactivations / role changes only where the old audit gives reliable evidence; (4) re-run integrity queries and Finance invariants.  
**Business Rules:** No reset tokens or delivery channels. Narrowed action only with reliable audit evidence.  
**Validation / Rejection:** N/A  
**Result:** No pre-restore password works without Manager action.  
**Audit / History:** Operator commands audited with a system marker.  
**Permissions / Privacy:** Operator commands are server-local, never HTTP routes.  
**Related Data:** `identity.users`  
**Related API Capability:** Operator command (SEC §17)

### FR-OPS-009

**Title:** Health endpoints and controlled migrations  
**Actors:** Operator; System  
**Preconditions:** N/A  
**Trigger:** Deployment / monitoring.  
**Main Flow:** `GET /health/live` and `GET /health/ready` (ready checks DB connectivity and schema compatibility: required − applied migrations). Only the migrator container (role `clinic_migrator`) applies migrations; the application never migrates itself.  
**Business Rules:** No destructive migration at application startup; expand / contract only. `/health/ready` is not routed publicly.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Health responses carry no detail; alerts and pings contain no personal data.  
**Related Data:** `infra.__EFMigrationsHistory`  
**Related API Capability:** `GET /health/live`, `GET /health/ready`

---

## 5. Authentication and Accounts

### FR-AUTH-001

**Title:** Exactly three application roles; Assistant has no account  
**Actors:** System; Manager  
**Preconditions:** N/A  
**Trigger:** Role seeding; account creation; Assistant creation.  
**Main Flow:** The only application roles are `MANAGER`, `DOCTOR`, `SALES_RECEPTION`. Each account has exactly one role. Creating an Assistant creates no `identity.users` or `user_roles` row.  
**Business Rules:** ASSISTANT is a staff type, not a role. No role union is ever computed. A DOCTOR account links to a `staff.doctors` row and no Sales row; SALES_RECEPTION the reverse; MANAGER neither. Doctor ↔ Sales on one account is not possible in V1.  
**Validation / Rejection:** A second `user_roles` row for one user fails (unique `user_id`). Role/staff mismatch refused.  
**Result:** `identity.roles` contains exactly the three roles.  
**Audit / History:** N/A  
**Permissions / Privacy:** No contact-bearing permission key may be mapped to DOCTOR (migration + startup test).  
**Related Data:** `identity.roles`, `identity.user_roles`, `staff.assistants` (no `user_id`)  
**Related API Capability:** N/A (seed + invariants; SEC #26, #42, #46)

### FR-AUTH-002

**Title:** Manager creates user accounts  
**Actors:** Manager  
**Preconditions:** Manager holds `Identity.Users.Manage`; for Doctor / Sales the matching staff record is created / linked.  
**Trigger:** Manager submits a new account.  
**Main Flow:** Manager chooses username, initial password, role and staff link; the system stores a one-way salted hash; the Manager tells the employee the credentials directly.  
**Business Rules:** No email / mobile identity, no invitation or activation link (Q-ACC-1). Password policy [DEFAULT]: ≥ 12 characters, deny-list, no composition rules, no forced expiry. One role per account.  
**Validation / Rejection:** Duplicate normalized username rejected; password policy violation rejected; staff link not matching the role rejected.  
**Result:** Active account able to log in.  
**Audit / History:** A+ (actor, target, time); no password or hash in audit.  
**Permissions / Privacy:** Manager only. Passwords are write-only; no response contains a password or hash.  
**Related Data:** `identity.users`, `identity.user_roles`, `staff.doctors` / `staff.sales_employees`  
**Related API Capability:** `POST /api/manager/users`

### FR-AUTH-003

**Title:** Manager sets a NEW password for a forgotten password  
**Actors:** Manager; employee (asks offline)  
**Preconditions:** Manager holds `Identity.Users.SetPassword`.  
**Trigger:** Employee asks the Manager after forgetting the password.  
**Main Flow:** Manager sets a new password → hash replaced → user's security stamp rotated (all sessions end) → lockout cleared → Manager tells the employee.  
**Business Rules:** The old password is never displayed or recoverable (only a hash exists), including by the Manager. No self-service reset (FR-AUTH-004).  
**Validation / Rejection:** Password policy applies.  
**Result:** New password works; old password fails; existing sessions of that user fail on next request.  
**Audit / History:** A+ (actor, target, time) with no password material; a password change cannot be reconstructed from audit.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `identity.users` (password_hash, security_stamp, lockout fields)  
**Related API Capability:** `POST /api/manager/users/{id}/password`

### FR-AUTH-004

**Title:** No self-service password reset  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** N/A  
**Business Rules:** No reset tokens, reset flag, recovery state, email / SMS delivery or `/api/auth/password-reset/*` route exists (Q-ACC-1).  
**Validation / Rejection:** N/A  
**Result:** No recovery-initiation endpoint is mapped.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** No reset-token column  
**Related API Capability:** None (API A9; SEC #13)

### FR-AUTH-005

**Title:** Login  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** Active account.  
**Trigger:** User submits username and password.  
**Main Flow:** `GET /api/auth/csrf` → `POST /api/auth/login` → password verified → session cookie issued → role and permission keys returned (menu use only).  
**Business Rules:** Unknown username goes through a dummy hash verification; unknown user, wrong password and inactive account all return the same generic "Invalid credentials". Permission keys returned to the client are never an authority.  
**Validation / Rejection:** Per-IP limit [DEFAULT 10/min] → 429; lockout after [DEFAULT] 5 consecutive failures for 15 minutes.  
**Result:** Authenticated session.  
**Audit / History:** Login success and failed attempts audited (IP, user agent, username truncated to 64 chars); password never stored.  
**Permissions / Privacy:** Anonymous endpoint (one of the six).  
**Related Data:** `identity.users`, `audit.audit_log`  
**Related API Capability:** `GET /api/auth/csrf`, `POST /api/auth/login`

### FR-AUTH-006

**Title:** Session validity and logout  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** Authenticated session.  
**Trigger:** Every authenticated request; logout.  
**Main Flow:** Every request checks the security stamp and `is_active` against the database; idle [DEFAULT 20 min sliding] / absolute [DEFAULT 10 h] expiry; logout deletes the cookie and clears site data.  
**Business Rules:** Deactivation, password set, role change and forced sign-out rotate the stamp, ending sessions on the next request. Cookie `__Host-clinic.session`, HttpOnly, Secure, SameSite=Strict; CSRF token on every unsafe method.  
**Validation / Rejection:** Invalid / expired / rotated session → 401.  
**Result:** Immediate effect of deactivation and password changes.  
**Audit / History:** N/A  
**Permissions / Privacy:** `POST /api/auth/logout` is `[AuthenticatedOnly]`.  
**Related Data:** `identity.users.security_stamp`  
**Related API Capability:** `POST /api/auth/logout`

### FR-AUTH-007

**Title:** Change own password  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** Authenticated; knows the current password.  
**Trigger:** User submits current and new password.  
**Main Flow:** Current password verified → new hash stored.  
**Business Rules:** Technical convenience, not a reset workflow. Password policy applies.  
**Validation / Rejection:** Wrong current password rejected.  
**Result:** New password effective.  
**Audit / History:** Audited without password material.  
**Permissions / Privacy:** `[AuthenticatedOnly]`.  
**Related Data:** `identity.users`  
**Related API Capability:** `POST /api/auth/change-password`

### FR-AUTH-008

**Title:** Manager changes a user's role or forces sign-out  
**Actors:** Manager  
**Preconditions:** `Identity.Users.Manage`.  
**Trigger:** Manager changes a role or requests sign-out everywhere.  
**Main Flow:** Role change replaces the single role and rotates the stamp; force sign-out rotates the stamp.  
**Business Rules:** Role change refused if the target's staff row does not fit the new role.  
**Validation / Rejection:** Mismatched staff link → refused.  
**Result:** Target's sessions end on next request.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `identity.user_roles`  
**Related API Capability:** `PUT /api/manager/users/{id}/role`, `POST /api/manager/users/{id}/force-sign-out`

### FR-AUTH-009

**Title:** Initial Manager and break-glass Manager password  
**Actors:** Operator  
**Preconditions:** Server access.  
**Trigger:** First deployment; Manager's own password lost.  
**Main Flow:** A one-time deployment command creates the initial Manager with a password entered at the operator prompt; if the Manager's password is lost, the operator runs the local `set-manager-password` command inside the `api` container.  
**Business Rules:** No default account in any environment. Not an HTTP endpoint. The last active Manager cannot be deactivated (FR-STAFF-011).  
**Validation / Rejection:** N/A  
**Result:** Manager access restored.  
**Audit / History:** Audited with a system marker.  
**Permissions / Privacy:** Operator only.  
**Related Data:** `identity.users`  
**Related API Capability:** Operator command (SEC §2)

### FR-AUTH-010

**Title:** Current-user context  
**Actors:** Manager, Doctor, Sales / Reception  
**Preconditions:** Authenticated.  
**Trigger:** SPA start / refresh.  
**Main Flow:** Returns display name, role, permission keys (menu only), clinic timezone and locale `ar` / `rtl`.  
**Business Rules:** Permission keys returned are usability data only; backend enforces.  
**Validation / Rejection:** Unauthenticated → 401.  
**Result:** SPA context.  
**Audit / History:** N/A  
**Permissions / Privacy:** `[AuthenticatedOnly]`.  
**Related Data:** `identity.*`, `configuration.settings`  
**Related API Capability:** `GET /api/me`

---

## 6. Staff Management

### FR-STAFF-001

**Title:** Create Doctor staff record  
**Actors:** Manager  
**Preconditions:** `Staff.Manage`; a DOCTOR account to link (FR-AUTH-002).  
**Trigger:** Manager creates a Doctor.  
**Main Flow:** Manager enters full name, specialty (optional) and current percentage; the record links to exactly one user account.  
**Business Rules:** Work snapshots copy the current percentage at work-item creation; nothing refers back to it (FR-STAFF-002).  
**Validation / Rejection:** Percentage outside 0..100 rejected.  
**Result:** Active Doctor.  
**Audit / History:** A.  
**Permissions / Privacy:** Manager only. Sales lookup of Doctors (`SalesDoctorLookupItem`) never includes the percentage.  
**Related Data:** `staff.doctors`  
**Related API Capability:** `GET/POST /api/manager/staff/doctors`

### FR-STAFF-002

**Title:** Configure Doctor percentage  
**Actors:** Manager  
**Preconditions:** `Staff.DoctorPercentage.Configure`.  
**Trigger:** Manager changes a Doctor's percentage.  
**Main Flow:** `current_percentage` updated.  
**Business Rules:** Affects only work items created afterwards (snapshot at work-item creation). Existing work items, charges, entitlements and payroll never change. Validation T6: the change must keep Doctor % + current General Lab % ≤ 100 (see FR-LAB-003).  
**Validation / Rejection:** Out of range rejected.  
**Result:** New percentage applies to new work.  
**Audit / History:** A+ (old / new).  
**Permissions / Privacy:** Manager only; never visible to Sales.  
**Related Data:** `staff.doctors.current_percentage`, `clinical.case_work_items.doctor_percentage_snapshot`  
**Related API Capability:** `PUT /api/manager/doctors/{id}/percentage`

### FR-STAFF-003

**Title:** Create Sales / Reception employee with required commission rate  
**Actors:** Manager  
**Preconditions:** `Staff.Manage`, `Staff.SalesCommission.Configure`; a SALES_RECEPTION account to link.  
**Trigger:** Manager creates a Sales employee.  
**Main Flow:** Manager enters full name, base monthly salary and commission percentage; one transaction writes the employee and commission-rate revision 1 (`effective_from` = database time).  
**Business Rules:** Commission percentage is **required** at creation (Q-FIN-2). The rate is stored only in the rate history (no rate column on the employee).  
**Validation / Rejection:** Missing commission percentage → 400 / 422; percentage outside 0..100 rejected.  
**Result:** Active Sales employee with revision 1.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.sales_employees`, `staff.sales_commission_rate_history`  
**Related API Capability:** `GET/POST /api/manager/staff/sales-employees`

### FR-STAFF-004

**Title:** Change Sales commission rate (future collections only)  
**Actors:** Manager  
**Preconditions:** `Staff.SalesCommission.Configure`.  
**Trigger:** Manager saves a new rate.  
**Main Flow:** Append a new rate-history revision with `effective_from` = database time of the save.  
**Business Rules:** The new rate applies to **future collections only**; the rate in force at each collection's `allocated_at` applies (Q-COMM-1). No back-dating or future-dating. Old collections, Draft payroll items and finalized payroll are never recalculated. Walk-In / Direct stays zero.  
**Validation / Rejection:** Out of range rejected; Doctor / Sales → 403.  
**Result:** Example: 5 % on 1,000 = 50; after change to 7 %, next 1,000 = 70; total 120.  
**Audit / History:** A+ (old and new rate).  
**Permissions / Privacy:** Manager only; a Sales employee cannot read the rate history.  
**Related Data:** `staff.sales_commission_rate_history`  
**Related API Capability:** `PUT /api/manager/sales-employees/{id}/commission`; `GET /api/manager/sales-employees/{id}/commission-history`

### FR-STAFF-005

**Title:** Change Sales base salary  
**Actors:** Manager  
**Preconditions:** `Payroll.Sales.ManageAll`.  
**Trigger:** Manager changes base salary.  
**Main Flow:** `base_monthly_salary` updated; future Sales payroll drafts snapshot it.  
**Business Rules:** Existing payroll snapshots never change.  
**Validation / Rejection:** N/A  
**Result:** New base salary for later drafts.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.sales_employees.base_monthly_salary`, `payroll.sales_payrolls.base_salary_snapshot`  
**Related API Capability:** `PUT /api/manager/sales-employees/{id}/base-salary`

### FR-STAFF-006

**Title:** Create and edit Assistant staff record  
**Actors:** Manager  
**Preconditions:** `Staff.Manage`.  
**Trigger:** Manager creates / edits an Assistant.  
**Main Flow:** Manager enters full name / basic information and current salary (> 0); edits use `If-Match`.  
**Business Rules:** No account, login, role, `user_id` or application access is created (FR-AUTH-001). Assistants are never deleted. No bonus, deduction, commission or incentive exists for Assistants.  
**Validation / Rejection:** Salary ≤ 0 rejected.  
**Result:** Active Assistant staff record.  
**Audit / History:** A+ on creation and updates.  
**Permissions / Privacy:** Manager only; Assistant data never appears in any Doctor or Sales response.  
**Related Data:** `staff.assistants`  
**Related API Capability:** `GET/POST /api/manager/staff/assistants`, `PUT /api/manager/staff/assistants/{id}`

### FR-STAFF-007

**Title:** Change Assistant salary  
**Actors:** Manager  
**Preconditions:** `Staff.AssistantSalary.Configure`.  
**Trigger:** Manager sets a new current salary with a reason.  
**Main Flow:** `current_salary` updated (`If-Match`).  
**Business Rules:** Never alters an existing payroll snapshot. `Staff.Manage` alone cannot change a salary; `Payroll.Assistant.ManageAll` alone cannot change a salary.  
**Validation / Rejection:** Salary ≤ 0 rejected.  
**Result:** New salary used for later payroll drafts.  
**Audit / History:** A+ (old / new).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.assistants.current_salary`  
**Related API Capability:** `PUT /api/manager/staff/assistants/{id}/salary`

### FR-STAFF-008

**Title:** Configure Assistant overtime hourly rate  
**Actors:** Manager  
**Preconditions:** `Staff.AssistantOvertimeRate.Configure`.  
**Trigger:** Manager sets the rate with a reason.  
**Main Flow:** `overtime_hourly_rate` updated (`If-Match`).  
**Business Rules:** Rate ≥ 0. A rate change never alters an existing overtime entry or payroll item (entries carry their own snapshot).  
**Validation / Rejection:** Negative rate rejected.  
**Result:** New rate used for later overtime entries.  
**Audit / History:** A+ (old / new).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.assistants.overtime_hourly_rate`  
**Related API Capability:** `PUT /api/manager/staff/assistants/{id}/overtime-rate`

### FR-STAFF-009

**Title:** Deactivate an employee  
**Actors:** Manager  
**Preconditions:** `Staff.Manage`.  
**Trigger:** Manager deactivates a Doctor, Sales employee or Assistant.  
**Main Flow:** One transaction: staff `is_active = false`, `deactivated_at` set; for Doctor / Sales the linked user is deactivated, lockout set to maximum and the security stamp rotated; nothing is deleted. Sales: open Leads / Follow-Ups are redistributed (FR-CRM-010 / FR-CRM-011).  
**Business Rules:** Doctor deactivation changes nothing financial (FR-FIN-030). Sales: no new commission after `deactivated_at` (FR-PAYROLL-005). Notifications fan out only to active users. Employees are never deleted.  
**Validation / Rejection:** The last active Manager and the caller's own account cannot be deactivated.  
**Result:** Login disabled immediately; history preserved.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.*`, `identity.users`, `crm.responsibility_history`  
**Related API Capability:** `POST /api/manager/staff/{id}/deactivate`, `POST /api/manager/staff/assistants/{id}/deactivate`

### FR-STAFF-010

**Title:** Reactivate an employee  
**Actors:** Manager  
**Preconditions:** `Staff.Manage`; employee inactive.  
**Trigger:** Manager reactivates.  
**Main Flow:** Staff and user set active; stamp rotated; the Manager sets a new password before the user can log in (Doctor / Sales).  
**Business Rules:** Reactivation does not move or restore redistributed Leads / Follow-Ups (not designed) and does not create retroactive commission.  
**Validation / Rejection:** N/A  
**Result:** Active employee.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.*`, `identity.users`  
**Related API Capability:** `POST /api/manager/staff/{id}/reactivate`, `POST /api/manager/staff/assistants/{id}/reactivate`

### FR-STAFF-011

**Title:** Employees and Manager accounts are never deleted  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** N/A  
**Business Rules:** Employees are Active / Inactive only; login disabled on deactivation; history preserved. The last active Manager cannot be deactivated.  
**Validation / Rejection:** No delete route; `clinic_app` has no DELETE grant.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `staff.*`, `identity.users`  
**Related API Capability:** None (no delete route)

---

## 7. Patients

### FR-PAT-001

**Title:** Register a Walk-In / Direct patient  
**Actors:** Sales / Reception, Manager  
**Preconditions:** Caller holds **both** `Patients.Identity.Write` and `Patients.Contact.Write`.  
**Trigger:** A patient who was not brought or contacted by Sales is registered.  
**Main Flow:** Caller enters full name, date of birth **or** recorded age, and contact fields (phone, WhatsApp, email, address, CRM notes). The system assigns `patient_code`, sets `registered_at`, creates the contact profile with `patient_source = WalkIn` and no Original Sales Employee.  
**Business Rules:** Walk-In / Direct patients have **no Original Sales Employee** and generate **zero Sales commission**; no pool (Q-PAT-1). The registering user is recorded only as `created_by_user_id` and never acquires commission ownership. Registration contains **no medical fields** (the intake is separate, Section 8). A DOB is never inferred from an age.  
**Validation / Rejection:** An Original Sales Employee field (or any unknown member) → 400. Neither DOB nor recorded age → rejected. Age outside 0..120 rejected. Only one of the two permissions → 403.  
**Result:** Active patient, source `WalkIn`.  
**Audit / History:** A; contact values audited as restricted.  
**Permissions / Privacy:** Composite policy `[HasAllPermissions]`. Duplicate-phone checks only in Sales / Manager flows.  
**Related Data:** `patients.patients`, `patients.patient_contacts`  
**Related API Capability:** `POST /api/sales/patients`, `POST /api/manager/patients`

### FR-PAT-002

**Title:** Lead-sourced patient creation (via Lead conversion)  
**Actors:** Sales / Reception, Manager  
**Preconditions:** An open Lead exists (FR-CRM-004).  
**Trigger:** Lead conversion.  
**Main Flow:** Patient created with `patient_source = Lead` and the lead's Original Sales Employee copied once.  
**Business Rules:** `patient_source` and `original_sales_employee_id` are frozen after creation. CHECK: `WalkIn` ⇔ no Original Sales; `Lead` ⇔ Original Sales present.  
**Validation / Rejection:** `Lead` without Original Sales or `WalkIn` with one → rejected by CHECK.  
**Result:** Lead-sourced patient attributed to the Original Sales Employee.  
**Audit / History:** A.  
**Permissions / Privacy:** Original Sales Representative is C-Contact: never visible to Doctors.  
**Related Data:** `patients.patient_contacts`, `crm.leads.converted_patient_id`  
**Related API Capability:** `POST /api/sales/leads/{id}/convert`

### FR-PAT-003

**Title:** Patient identity and age display  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** Patient exists.  
**Trigger:** Any patient display or print.  
**Main Flow:** DOB present → current age computed from DOB. Only a recorded age → shown as "Age recorded at registration: N (recorded on D)", never aged forward.  
**Business Rules:** Same rule for Doctor DTO, Sales DTO, Manager view and prescription print model. Doctor sees DOB only while setting `doctor_dob_visible` is on ([DEFAULT] visible). Sales always sees DOB (Q-PRV-1).  
**Validation / Rejection:** Age columns both set or both null; at least one of DOB / recorded age present.  
**Result:** Consistent age display.  
**Audit / History:** N/A  
**Permissions / Privacy:** See FR-SEC-004.  
**Related Data:** `patients.patients` (date_of_birth, age_years_recorded, age_recorded_on)  
**Related API Capability:** Patient DTOs (API A3)

### FR-PAT-004

**Title:** Patient search by audience  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** Search permission.  
**Trigger:** User searches.  
**Main Flow:** Doctor searches by Patient ID or name only; Sales / Manager search by ID, name or phone.  
**Business Rules:** Search is a POST body, never a query string. A phone typed into the Doctor name box finds nothing. Max page size 50 for patient search; [DEFAULT] 60 searches/min per user.  
**Validation / Rejection:** GET with PII → rejected.  
**Result:** Audience-specific list items (Doctor: id, code, name, age display only).  
**Audit / History:** N/A  
**Permissions / Privacy:** `Patients.Search.Clinical` (Doctor, Manager); `Patients.Search.Contact` (Sales, Manager).  
**Related Data:** `patients.patients`, `patients.patient_contacts`  
**Related API Capability:** `POST /api/doctor/patients/search`, `POST /api/sales/patients/search`, `POST /api/manager/patients/search`

### FR-PAT-005

**Title:** Edit patient identity and contact data  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Patients.Identity.Write` / `Patients.Contact.Write`; `If-Match`.  
**Trigger:** Correction of name / DOB / age or contact fields.  
**Main Flow:** Identity and contact updated separately.  
**Business Rules:** `patient_source` and Original Sales Employee cannot be changed.  
**Validation / Rejection:** Stale ETag → 412.  
**Result:** Updated record.  
**Audit / History:** A; contact changes store previous / new values flagged `is_restricted` (Manager-only readable).  
**Permissions / Privacy:** Doctors cannot write identity or contact.  
**Related Data:** `patients.patients`, `patients.patient_contacts`  
**Related API Capability:** `PUT /api/sales/patients/{id}/identity`, `PUT /api/sales/patients/{id}/contact`

### FR-PAT-006

**Title:** Sales patient view  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Patients.Contact.Read`.  
**Trigger:** Open patient.  
**Main Flow:** Returns id, code, full name, DOB or recorded age, age display, contact fields, CRM notes, `patientSource`, Original Sales Employee (only for `Lead`), active flag, registered at, current Doctor, Patient Credit balance.  
**Business Rules:** No chief complaint and no Doctor-authored clinical field.  
**Validation / Rejection:** N/A  
**Result:** `SalesPatientDto`.  
**Audit / History:** Sales contact views write an access event.  
**Permissions / Privacy:** C-Contact allowed; C-Clinical forbidden.  
**Related Data:** `patients.*`, `finance.patient_credit_entries`  
**Related API Capability:** `GET /api/sales/patients/{id}`

### FR-PAT-007

**Title:** Doctor patient view (no contact data)  
**Actors:** Doctor, Manager  
**Preconditions:** `Patients.Clinical.Read`.  
**Trigger:** Doctor opens any patient.  
**Main Flow:** Returns id, code, full name, age display, chief complaint, active flag, registered at, current Doctor.  
**Business Rules:** All Doctors may open every patient (confirmed). Never phone, WhatsApp, email, address, CRM notes, Original Sales Employee or patient source.  
**Validation / Rejection:** N/A  
**Result:** `DoctorPatientDto`.  
**Audit / History:** Every Doctor open is audited as record access (A+).  
**Permissions / Privacy:** FR-SEC-002.  
**Related Data:** `patients.patients`  
**Related API Capability:** `GET /api/doctor/patients/{id}`

### FR-PAT-008

**Title:** Chief Complaint maintenance  
**Actors:** Doctor, Manager  
**Preconditions:** `Patients.Clinical.Write`; Doctor has an editable Visit for this patient; `If-Match`.  
**Trigger:** Doctor updates the chief complaint.  
**Main Flow:** Chief complaint updated on the patient.  
**Business Rules:** Chief Complaint is a separate field (not part of the Medical History Intake); written by the Doctor or Manager; not visible to Sales.  
**Validation / Rejection:** No editable Visit → refused.  
**Result:** Updated clinical profile.  
**Audit / History:** A.  
**Permissions / Privacy:** C-Clinical.  
**Related Data:** `patients.patients.chief_complaint`  
**Related API Capability:** `PUT /api/doctor/patients/{id}/clinical-profile`

### FR-PAT-009

**Title:** Manager reassigns a patient's Doctor  
**Actors:** Manager  
**Preconditions:** `Patients.Reassign`; `If-Match`.  
**Trigger:** Manager reassigns with a reason.  
**Main Flow:** Current assignment row gets `ended_at`; a new row stores previous Doctor, new Doctor, time, Manager and reason, in one transaction.  
**Business Rules:** One current Doctor per patient. Reassignment **does not move any financial ownership** and never delays or alters a payment, allocation, entitlement or earning (Q-FIN-9). Historical work never references assignment rows.  
**Validation / Rejection:** N/A  
**Result:** New current Doctor.  
**Audit / History:** A+; assignment history preserved.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `patients.patient_doctor_assignments`  
**Related API Capability:** `POST /api/manager/patients/{id}/reassign`, `GET /api/manager/patients/{id}/assignments`

### FR-PAT-010

**Title:** Patient timelines by audience  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** `Patients.Timeline.Clinical` (Doctor, Manager) or `Patients.Timeline.Operational` (Sales, Manager).  
**Trigger:** Open timeline.  
**Main Flow:** Clinical timeline: visits, treatments, prescriptions; payment entries limited to the Doctor's own work. Operational timeline: appointments, follow-ups, payments; no prescription or visit content.  
**Business Rules:** Complete history is preserved permanently. Doctor follow-up entries show type, due date and status only.  
**Validation / Rejection:** N/A  
**Result:** Audience-specific timeline.  
**Audit / History:** N/A  
**Permissions / Privacy:** No contact data to Doctors; no clinical content to Sales.  
**Related Data:** Multiple modules (read)  
**Related API Capability:** `GET /api/doctor/patients/{id}/timeline`, `GET /api/sales/patients/{id}/timeline`, `GET /api/manager/patients/{id}/timeline`

---

## 8. Structured Medical History

### FR-MED-001

**Title:** Structured Patient Medical History Intake content  
**Actors:** Sales / Reception, Manager (write); Doctor (read)  
**Preconditions:** Patient exists.  
**Trigger:** N/A (data definition)  
**Main Flow:** One current intake record per patient (0..1) with exactly these questions: Allergy; High Blood Pressure / Hypertension; Viral Hepatitis; Diabetes; Kidney Problems; Bleeding / Blood Clotting Problems; Heart Problems; Rheumatic Diseases; Smoker; Previous Surgical Operations; Currently Taking Medications (each Yes / No / Not answered); Last Dental Visit (free text ≤ 200 characters, never forced into a date); Pregnant; Breastfeeding (female patients only; Yes / No / Not answered or not applicable).  
**Business Rules:** It replaces the old Medical History, Chronic Diseases and Previous Surgeries fields; no generic free-text medical history exists. NOT designed and not built: allergy type, medication names / doses, surgery details, hepatitis / heart / kidney / rheumatic / bleeding details, any "other condition" field.  
**Validation / Rejection:** `lastDentalVisit` > 200 characters rejected; unknown members rejected.  
**Result:** Single structured source of truth.  
**Audit / History:** See FR-MED-006.  
**Permissions / Privacy:** C-MedicalIntake class.  
**Related Data:** `patients.patient_medical_histories`  
**Related API Capability:** Intake DTOs (API A3)

### FR-MED-002

**Title:** Sales / Reception collects and edits the intake  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Patients.MedicalHistory.EditIntake`.  
**Trigger:** Sales collects answers outside the Doctor room, before the Doctor consultation when possible.  
**Main Flow:** First PUT creates the intake (no `If-Match`); later PUTs replace the answers with `If-Match`.  
**Business Rules:** Independent of any appointment state; not part of registration; a patient may have no intake.  
**Validation / Rejection:** Stale ETag → 412; flags accept only `true` / `false` / `null`.  
**Result:** Intake created / updated.  
**Audit / History:** A+ with changed field **names** only.  
**Permissions / Privacy:** Sales holds no `Patients.Clinical.Read`; the intake key grants no other clinical access (FR-MED-008).  
**Related Data:** `patients.patient_medical_histories`  
**Related API Capability:** `GET/PUT /api/sales/patients/{id}/medical-history` (GET → 404 when no intake exists)

### FR-MED-003

**Title:** Doctor reads the intake (read-only)  
**Actors:** Doctor, Manager  
**Preconditions:** `Patients.MedicalHistory.Read`.  
**Trigger:** Doctor opens the patient clinical profile, before or during a Visit (including from the arrival queue).  
**Main Flow:** Returns the intake if it exists.  
**Business Rules:** No Doctor write route exists; Doctor never holds `EditIntake`. The DTO contains no contact field and no ETag.  
**Validation / Rejection:** Any Doctor write attempt → 403.  
**Result:** `DoctorPatientMedicalHistoryDto`.  
**Audit / History:** Record access audited.  
**Permissions / Privacy:** No contact data to Doctor.  
**Related Data:** `patients.patient_medical_histories`  
**Related API Capability:** `GET /api/doctor/patients/{id}/medical-history`

### FR-MED-004

**Title:** Unanswered is never "No"  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Intake stored or returned.  
**Main Flow:** Each flag is a nullable boolean: TRUE = Yes, FALSE = No, NULL = Not answered / Not applicable.  
**Business Rules:** NULL is never defaulted or converted to FALSE; the API returns `null`, never `false`, for unanswered.  
**Validation / Rejection:** N/A  
**Result:** Faithful tri-state round-trip.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Flag columns have no DEFAULT and no NOT NULL  
**Related API Capability:** API A11 #38

### FR-MED-005

**Title:** Pregnancy and breastfeeding questions  
**Actors:** Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Intake collection.  
**Main Flow:** Sales / Reception applies the female-only section when relevant.  
**Business Rules:** V1 stores no Patient sex field (accepted limitation). Not applicable or unanswered stays NULL, never FALSE; these answers are never required.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** As FR-MED-006.  
**Permissions / Privacy:** Notification text never names a condition, including pregnancy.  
**Related Data:** `is_pregnant`, `is_breastfeeding`  
**Related API Capability:** Intake PUT

### FR-MED-006

**Title:** Intake privacy and audit  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Intake create / update / read.  
**Main Flow:** Each create / update writes an audit row with actor, timestamp, patient id and changed field names.  
**Business Rules:** Medical History values never appear in the audit log, application logs, notification payloads, reports, Manager financial reports or exports.  
**Validation / Rejection:** N/A  
**Result:** Canary medical values never leak (SEC #51).  
**Audit / History:** A+ (names only).  
**Permissions / Privacy:** Worker has no access to the table.  
**Related Data:** `audit.audit_log`  
**Related API Capability:** N/A

### FR-MED-007

**Title:** Intake never blocks booking  
**Actors:** Sales / Reception, Manager, Doctor  
**Preconditions:** N/A  
**Trigger:** Booking, arrival, Visit start.  
**Main Flow:** None of these steps checks for an intake.  
**Business Rules:** A patient may be registered and booked with no intake; the Doctor reads the intake if it exists. The intake is not tied to Arrived / Waiting / check-in (none exist). The Assistant has no access.  
**Validation / Rejection:** N/A  
**Result:** Booking independent of intake completion.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-MED-008

**Title:** Intake access grants no other clinical access to Sales  
**Actors:** Sales / Reception  
**Preconditions:** Sales holds intake keys.  
**Trigger:** Any Sales request.  
**Main Flow:** N/A  
**Business Rules:** Holding the intake key gives no access to clinical notes, measurements, prescriptions, Doctor percentage, shares or lab.  
**Validation / Rejection:** Those routes → 403; fields absent from the Sales intake DTO.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Narrow, deliberate exception only.  
**Related Data:** N/A  
**Related API Capability:** SEC #50

---

## 9. CRM / Leads / Follow-Ups

### FR-CRM-001

**Title:** Create and manage Leads  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.Leads.Manage`.  
**Trigger:** A prospective patient is acquired / contacted by Sales.  
**Main Flow:** Lead recorded with name, phone, source, interested service, notes, next follow-up date and status; `original_sales_employee_id` (frozen attribution) and `responsible_sales_employee_id` (current operational owner) set.  
**Business Rules:** Lead status is exactly one of the **8 confirmed Lead statuses**: **New, Contacted, Interested, Call Later, No Answer, Not Interested, Booked, Converted**. No other Lead status exists. Open / closed behavior is only what the frozen sources define: an **open** Lead is one with no `converted_patient_id` and a status that is not classified closed; the open / closed classification of each status is seed data of the Lead status catalogue [T] (DB §12, §23; BASE §8). The open predicate drives My Leads / All Leads lists and the redistribution on Sales deactivation (FR-CRM-010). No other Lead lifecycle or transition rule is defined. `original_sales_employee_id` never changes.  
**Validation / Rejection:** Status outside the catalogue rejected; `If-Match` on update.  
**Result:** Lead in My Leads of the responsible employee.  
**Audit / History:** A.  
**Permissions / Privacy:** Lead data is C-Contact (never visible to Doctors).  
**Related Data:** `crm.leads`  
**Related API Capability:** `POST /api/sales/leads`, `GET/PUT /api/sales/leads/{id}`

### FR-CRM-002

**Title:** My Leads (Sales)  
**Actors:** Sales / Reception  
**Preconditions:** `Crm.Leads.ViewOwn`.  
**Trigger:** Sales opens My Leads.  
**Main Flow:** Returns leads where `responsible_sales_employee_id` = principal's Sales employee.  
**Business Rules:** Identity from principal only. Unassigned leads never appear here.  
**Validation / Rejection:** N/A  
**Result:** Own lead list.  
**Audit / History:** N/A  
**Permissions / Privacy:** Scope rule S13.  
**Related Data:** `crm.leads`  
**Related API Capability:** `GET /api/sales/leads/my`

### FR-CRM-003

**Title:** All Leads (Manager)  
**Actors:** Manager  
**Preconditions:** `Crm.Leads.ViewAll`.  
**Trigger:** Manager opens All Leads.  
**Main Flow:** Returns every lead including the Responsible Sales Member; filter `unassigned=true` shows Unassigned Leads.  
**Business Rules:** Unassigned leads are visible and assignable by the Manager only.  
**Validation / Rejection:** Sales → 403.  
**Result:** All Leads table.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** `crm.leads`  
**Related API Capability:** `GET /api/manager/leads`

### FR-CRM-004

**Title:** Convert a Lead to a Patient  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.Leads.Manage`; lead not yet converted.  
**Trigger:** The lead becomes a patient.  
**Main Flow:** Patient and contact profile created with `patient_source = Lead`; the lead's Original Sales Employee copied once; `converted_patient_id` set.  
**Business Rules:** Conversion preserves Original Sales attribution; this is the only way a patient gets an Original Sales Employee.  
**Validation / Rejection:** Already converted → refused (unique `converted_patient_id`).  
**Result:** Lead-sourced patient (FR-PAT-002).  
**Audit / History:** A.  
**Permissions / Privacy:** C-Contact.  
**Related Data:** `crm.leads`, `patients.*`  
**Related API Capability:** `POST /api/sales/leads/{id}/convert`

### FR-CRM-005

**Title:** Create and manage Follow-Ups  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.FollowUps.Manage`.  
**Trigger:** A follow-up task is needed.  
**Main Flow:** Follow-up stored with follow-up type (one of the **6 confirmed sources**), patient or lead, assigned Sales employee, due date, status, notes, result, next follow-up date and at most one source reference (lead, referral, appointment, installment or charge).  
**Business Rules:** Follow-up type (source) is exactly one of: **Lead, Doctor Referral, Implant Appointment, Ortho Installment, Remaining Balance (`RemainingBalance`), General Treatment Follow-Up**. No other source exists. Status is exactly one of the four frozen statuses (FR-CRM-006): **New / جديد, Contacted / تم التواصل, No Response / لم يرد, Followed Up / تمت المتابعة**; **open = New, Contacted, No Response; closed = Followed Up**. A Remaining Balance follow-up produces the Outstanding Payment Follow-Up notification; every other source produces Pending Follow-Up (FR-NOTIF-005). Follow-ups are not created automatically for remaining balances or installments (not built).  
**Validation / Rejection:** At most one source FK; patient or lead required; `If-Match` on update.  
**Result:** Follow-up in the responsible employee's list.  
**Audit / History:** A.  
**Permissions / Privacy:** Follow-up notes are C-Contact.  
**Related Data:** `crm.follow_ups`  
**Related API Capability:** `GET/POST /api/sales/follow-ups`, `PUT /api/sales/follow-ups/{id}`

### FR-CRM-006

**Title:** Exact Follow-Up statuses and open predicate  
**Actors:** Sales / Reception, Manager, System  
**Preconditions:** N/A  
**Trigger:** Follow-up status set / evaluated.  
**Main Flow:** Status ∈ `New` (جديد), `Contacted` (تم التواصل), `NoResponse` (لم يرد), `FollowedUp` (تمت المتابعة).  
**Business Rules:** **Open = New, Contacted, NoResponse; Closed = FollowedUp** (`status <> 'FollowedUp'`) (Q-CRM-1). This single predicate drives Pending Follow-Ups, J3, J7, dashboard counts, inactive-owner redistribution and validation. No other status exists. Referral progress statuses (FR-REF-004) are a different field.  
**Validation / Rejection:** Any other status → rejected (CHECK).  
**Result:** N/A  
**Audit / History:** A.  
**Permissions / Privacy:** N/A  
**Related Data:** `crm.follow_ups.status`  
**Related API Capability:** API A11 #15; B9 #9

### FR-CRM-007

**Title:** Follow-Up contact attempts  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.FollowUps.Manage`.  
**Trigger:** Sales records a contact attempt.  
**Main Flow:** Attempt appended with time, user, outcome and notes.  
**Business Rules:** Attempts are tracked and never overwritten (append-only).  
**Validation / Rejection:** N/A  
**Result:** Attempt history.  
**Audit / History:** Insert-only (H).  
**Permissions / Privacy:** C-Contact.  
**Related Data:** `crm.follow_up_contact_attempts`  
**Related API Capability:** `POST /api/sales/follow-ups/{id}/contact-attempts`

### FR-CRM-008

**Title:** Sales sees only own Follow-Ups  
**Actors:** Sales / Reception  
**Preconditions:** `Crm.FollowUps.Manage`.  
**Trigger:** Sales opens Follow-Ups.  
**Main Flow:** Returns follow-ups assigned to the principal.  
**Business Rules:** Follow-ups with no responsible Sales employee are not visible to Sales.  
**Validation / Rejection:** Another employee's follow-up → 404.  
**Result:** Own list.  
**Audit / History:** N/A  
**Permissions / Privacy:** Scope S13.  
**Related Data:** `crm.follow_ups`  
**Related API Capability:** `GET /api/sales/follow-ups`

### FR-CRM-009

**Title:** Manager assigns / reassigns Leads and Follow-Ups  
**Actors:** Manager  
**Preconditions:** `Crm.Responsibility.Assign` (and `Crm.Leads.ViewAll` for leads).  
**Trigger:** Manager reassigns an item or assigns an unassigned one.  
**Main Flow:** Responsible employee changed; `responsibility_history` row written (reason `ManagerReassigned` / `ManagerAssignedUnassigned`) in the same transaction.  
**Business Rules:** Original Sales attribution never changes. No `/api/unassigned/*` route exists; unassigned work is a filter on the Manager routes.  
**Validation / Rejection:** Sales → 403.  
**Result:** Item appears in the new owner's list.  
**Audit / History:** A+ (`responsibility_history`).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `crm.leads`, `crm.follow_ups`, `crm.responsibility_history`  
**Related API Capability:** `POST /api/manager/leads/{id}/reassign`, `GET /api/manager/follow-ups?unassigned=true`, `POST /api/manager/follow-ups/{id}/assign`

### FR-CRM-010

**Title:** Inactive Sales — even redistribution of open Leads and Follow-Ups  
**Actors:** Manager (deactivates); System  
**Preconditions:** A Sales employee is being deactivated and at least one other Active Sales employee exists.  
**Trigger:** FR-STAFF-009 for a Sales employee.  
**Main Flow:** In the deactivation transaction, every **open** Lead (not converted and not in a closed Lead status) and every **open** Follow-Up (`status <> 'FollowedUp'`) of the employee is reassigned across the Active Sales employees round-robin over the batch ordered by creation time, starting with the employee with the fewest open items; one `responsibility_history` row per item with reason `SalesDeactivated` and a shared batch id.  
**Business Rules:** Counts differ by at most one (Q-EMP-1). Operational responsibility only: Original Sales attribution is untouched and commission is never transferred.  
**Validation / Rejection:** N/A  
**Result:** Redistributed items notify their new owners; J7 reports zero open follow-ups owned by inactive Sales.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `crm.leads`, `crm.follow_ups`, `crm.responsibility_history`  
**Related API Capability:** `POST /api/manager/staff/{id}/deactivate`

### FR-CRM-011

**Title:** No Active Sales left — items become unassigned  
**Actors:** Manager; System  
**Preconditions:** The deactivated Sales employee is the last Active Sales employee.  
**Trigger:** FR-STAFF-009.  
**Main Flow:** Deactivation succeeds; open Leads / Follow-Ups get responsible = NULL; `responsibility_history` records `to_sales_employee_id = NULL`; the Manager sees them under Unassigned Leads / Unassigned Follow-Ups and assigns them (FR-CRM-009).  
**Business Rules:** Deactivation is never refused for lack of colleagues. Unassigned items trigger no reminder until assigned; J7 counts them.  
**Validation / Rejection:** N/A  
**Result:** Nothing lost; nothing transferred.  
**Audit / History:** A+.  
**Permissions / Privacy:** Unassigned items visible to the Manager only.  
**Related Data:** As FR-CRM-010  
**Related API Capability:** As FR-CRM-009

### FR-CRM-012

**Title:** Original Sales attribution is permanent  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any reassignment, redistribution, deactivation or contact edit.  
**Main Flow:** N/A  
**Business Rules:** `crm.leads.original_sales_employee_id` and `patient_contacts.original_sales_employee_id` / `patient_source` never change; responsibility changes affect only the responsible / assigned columns.  
**Validation / Rejection:** Attempts to change are rejected.  
**Result:** Commission attribution preserved (subject to FR-PAYROLL-005).  
**Audit / History:** N/A  
**Permissions / Privacy:** Never exposed to Doctors.  
**Related Data:** `crm.leads`, `patients.patient_contacts`  
**Related API Capability:** N/A

---

## 10. Scheduling / Appointments / Arrival Queue

### FR-SCH-001

**Title:** Manager publishes weekly Doctor availability  
**Actors:** Manager  
**Preconditions:** `Scheduling.Availability.Publish`.  
**Trigger:** Manager prepares a Doctor's week.  
**Main Flow:** Draft week with availability ranges per day → publish.  
**Business Rules:** One week record per Doctor per week start. A Doctor's availability ranges cannot overlap on the same day (exclusion constraint; applies to availability only, never to appointments). Ranges are withdrawn, not deleted.  
**Validation / Rejection:** Overlapping availability ranges → 409.  
**Result:** Published week visible to Sales and to the Doctor.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `scheduling.availability_weeks`, `scheduling.availability_ranges`  
**Related API Capability:** `GET/PUT /api/manager/doctors/{id}/availability/{weekStart}`, `POST …/publish`

### FR-SCH-002

**Title:** View availability and schedules  
**Actors:** Sales / Reception, Manager, Doctor  
**Preconditions:** `Scheduling.Availability.View` (Sales, Manager) / `.ViewOwn` (Doctor, Manager).  
**Trigger:** Booking or schedule view.  
**Main Flow:** Sales lists Doctors (`SalesDoctorLookupItem`: id, name, specialty) and slots from published weeks; the Doctor sees own published weeks.  
**Business Rules:** Published weeks only. Effective booked ranges account for early Visit end (FR-SCH-013).  
**Validation / Rejection:** N/A  
**Result:** Availability views.  
**Audit / History:** N/A  
**Permissions / Privacy:** Doctor lookup never includes percentages.  
**Related Data:** `scheduling.*`, `scheduling.v_effective_booked_ranges`  
**Related API Capability:** `GET /api/sales/doctors`, `GET /api/sales/availability/slots`, `GET /api/doctor/schedule`

### FR-SCH-003

**Title:** Book an appointment with manual duration  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Scheduling.Appointments.Book`.  
**Trigger:** Booking request.  
**Main Flow:** Appointment created with patient, Doctor, start time, **duration in minutes entered manually**, reason, Sales notes and kind (`Standard` / `ImplantFollowUp`); status `Scheduled`.  
**Business Rules:** Duration is required per appointment (1–480); no clinic-wide default, no procedure-duration mapping (Q-APP-1). Booking never depends on the Medical History intake. `reason` is Doctor-visible; `sales_notes` is Sales / Manager only. Sales-authored `reason` rejects phone / email patterns (heuristic).  
**Validation / Rejection:** Missing / 0 / > 480 duration → 400.  
**Result:** Scheduled appointment.  
**Audit / History:** A+.  
**Permissions / Privacy:** See FR-SCH-014.  
**Related Data:** `scheduling.appointments`  
**Related API Capability:** `POST /api/sales/appointments`, `POST /api/manager/appointments`

### FR-SCH-004

**Title:** Overlapping appointments allowed  
**Actors:** Sales / Reception, Manager  
**Preconditions:** As FR-SCH-003.  
**Trigger:** Booking that overlaps another appointment of the same Doctor.  
**Main Flow:** Booking accepted.  
**Business Rules:** Overlap is allowed for everyone who can book (Q-APP-1/2). No same-slot unique index, no exclusion constraint, no conflict response, no override permission or workflow.  
**Validation / Rejection:** Overlap is never a conflict.  
**Result:** Two concurrent bookings for the same Doctor and time both succeed.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments`  
**Related API Capability:** API A11 #9

### FR-SCH-005

**Title:** Exactly seven appointment statuses  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any status write.  
**Main Flow:** Status ∈ Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled.  
**Business Rules:** No Arrived, Waiting or Checked-In status or label exists (Q-APP-4). In Treatment is set by Visit start (FR-SCH-012); Completed by Visit finalization (FR-SCH-013); Rescheduled by reschedule (FR-SCH-007).  
**Validation / Rejection:** `Arrived` / `Waiting` → 400; any other value rejected by CHECK.  
**Result:** N/A  
**Audit / History:** Status changes record time, user and reason.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments.status`  
**Related API Capability:** API A8 "Appointment statuses and arrival"

### FR-SCH-006

**Title:** Confirm, Cancel, No Show  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Scheduling.Appointments.UpdateStatus`; `If-Match`.  
**Trigger:** Status action.  
**Main Flow:** Appointment moves to Confirmed, Cancelled (reason required) or No Show.  
**Business Rules:** Sales and Manager both hold these actions (Q-APP-3). Idempotent when already in the target state.  
**Validation / Rejection:** Doctor → 403; Cancel without reason → 400.  
**Result:** Status updated with `status_changed_at`, `status_changed_by_user_id`, `status_change_reason`.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments`  
**Related API Capability:** `POST /api/sales/appointments/{id}/status`, `POST /api/manager/appointments/{id}/status`

### FR-SCH-007

**Title:** Reschedule preserves history  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Scheduling.Appointments.Reschedule`; `If-Match`.  
**Trigger:** Time / Doctor change requested.  
**Main Flow:** New appointment inserted pointing back via `previous_appointment_id`; old appointment marked Rescheduled with reason.  
**Business Rules:** Start time, duration, Doctor, patient and reason are frozen after creation; a change is always a reschedule. The original is never updated or deleted. A pending reminder of the old appointment is Cancelled.  
**Validation / Rejection:** Two concurrent reschedules of one appointment → one success (409 for the other).  
**Result:** Reschedule chain.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments` (unique `previous_appointment_id`)  
**Related API Capability:** `POST /api/sales/appointments/{id}/reschedule`, `POST /api/manager/appointments/{id}/reschedule`

### FR-SCH-008

**Title:** Doctor creates the next (follow-up) appointment and reschedules it  
**Actors:** Doctor (also Sales and Manager hold `CreateFollowUp`)  
**Preconditions:** `Scheduling.Appointments.CreateFollowUp`; own Visit. Reschedule: `Scheduling.Appointments.RescheduleOwnFollowUp`, only a follow-up the Doctor created.  
**Trigger:** Doctor schedules the next appointment from a Visit.  
**Main Flow:** The Doctor submits `visitId`, `startAt`, **`durationMinutes`** and `reason`; the appointment is created with the **authoring Doctor**, status Scheduled; it appears in the Sales schedule and generates its reminder.  
**Business Rules:** "Doctor: book general appointment: LIMITED" = follow-up appointment only. **OWNER DECISION (FRS correction pass, option A):** the **Doctor manually enters the duration** of the follow-up appointment; `durationMinutes` is **required**, no default duration exists, and the same duration validation as every other appointment applies (1–480 minutes, FR-SCH-003). Duration selection is not delegated to Sales. Overlap is allowed (FR-SCH-004). This is a Follow-Up **Appointment** only: no Follow-Up Request workflow and no additional business flow exists.  
**Validation / Rejection:** Missing / 0 / > 480 `durationMinutes` → 400; another Doctor's visit / follow-up → 404.  
**Result:** Follow-up appointment.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments` (`doctor_id` = authoring Doctor, `duration_minutes` NOT NULL CHECK 1..480, `created_by_user_id`, `kind`)  
**Related API Capability:** `POST /api/doctor/appointments/follow-up` (body `{ visitId, startAt, durationMinutes, reason }`), `POST /api/doctor/appointments/{id}/reschedule`

### FR-SCH-009

**Title:** Record patient arrival (event, not status)  
**Actors:** Sales / Reception  
**Preconditions:** `Scheduling.Arrival.Record`; appointment is Scheduled or Confirmed and on the current clinic-local day [T5].  
**Trigger:** Patient physically arrives.  
**Main Flow:** Insert an arrival row with `arrived_at` and the recording user.  
**Business Rules:** Arrival **does not change the Appointment status** (Q-APP-4). One live arrival per appointment. The Manager is deliberately not granted the arrival action [T5]. Arrival does not gate starting a Visit.  
**Validation / Rejection:** Other status / other day → 422; second live arrival → 409; Doctor / Manager → 403.  
**Result:** Patient appears in the Doctor's queue.  
**Audit / History:** A+.  
**Permissions / Privacy:** Sales only.  
**Related Data:** `scheduling.appointment_arrivals`  
**Related API Capability:** `POST /api/sales/appointments/{id}/arrival`

### FR-SCH-010

**Title:** Void a mistaken arrival  
**Actors:** Sales / Reception  
**Preconditions:** `Scheduling.Arrival.Void`; a live arrival exists.  
**Trigger:** Arrival recorded by mistake.  
**Main Flow:** `voided_at` / `voided_by_user_id` set once; a new arrival may then be recorded.  
**Business Rules:** Arrival rows are never deleted.  
**Validation / Rejection:** No live arrival → refused.  
**Result:** Patient leaves the queue.  
**Audit / History:** A+.  
**Permissions / Privacy:** Sales only.  
**Related Data:** `scheduling.appointment_arrivals`  
**Related API Capability:** `POST /api/sales/appointments/{id}/arrival/void`

### FR-SCH-011

**Title:** Doctor waiting queue (derived)  
**Actors:** Doctor  
**Preconditions:** `Scheduling.Queue.ReadOwn`.  
**Trigger:** Doctor opens the queue.  
**Main Flow:** Returns the principal's appointments with a live arrival today, status Scheduled / Confirmed and no Visit started, ordered by `arrived_at`.  
**Business Rules:** Derived on read, never stored. Item: appointment id, patient {id, code, name, age display}, reason, start, duration, arrived at.  
**Validation / Rejection:** Sales → 403; no Doctor id parameter accepted.  
**Result:** `DoctorQueueItemDto` list.  
**Audit / History:** N/A  
**Permissions / Privacy:** No contact data, no `sales_notes`, no financial data, no Original Sales Employee.  
**Related Data:** `scheduling.appointments`, `scheduling.appointment_arrivals`, `clinical.visits`  
**Related API Capability:** `GET /api/doctor/queue`

### FR-SCH-012

**Title:** Visit start moves the appointment to In Treatment  
**Actors:** Doctor  
**Preconditions:** Linked appointment is Scheduled or Confirmed.  
**Trigger:** Doctor starts a Visit linked to the appointment (FR-CLN-001).  
**Main Flow:** Appointment status → In Treatment; the patient leaves the queue.  
**Business Rules:** Arrival is not required. An appointment in any other status is not changed by Visit start. One Visit per appointment.  
**Validation / Rejection:** N/A  
**Result:** In Treatment.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments`, `clinical.visits.appointment_id` (unique)  
**Related API Capability:** `POST /api/doctor/visits`

### FR-SCH-013

**Title:** Visit finalization completes the appointment and releases unused booked time  
**Actors:** Doctor; System  
**Preconditions:** Linked Visit is finalized (FR-CLN-012).  
**Trigger:** Visit finalization.  
**Main Flow:** In the finalize transaction the Scheduling contract sets the appointment Completed and `visit_ended_at` (once).  
**Business Rules:** Effective booked end = `LEAST(start_at + duration, visit_ended_at)`; the unused future part becomes available again; the Appointment is never deleted or altered otherwise and its history remains; a range is never extended past its nominal end.  
**Validation / Rejection:** N/A  
**Result:** Unused time no longer reported booked.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments.visit_ended_at`, `scheduling.v_effective_booked_ranges`  
**Related API Capability:** `POST /api/doctor/visits/{id}/finalize`

### FR-SCH-014

**Title:** Appointment views by audience  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** `Scheduling.Appointments.View.Own` (Doctor) / `.View.All` (Sales, Manager).  
**Trigger:** List / open appointments.  
**Main Flow:** Doctor: own appointments (patient id / code / name, start, duration, status, reason, kind, linked case, `arrivedAt`). Sales: all appointments plus `sales_notes`, patient contact, `arrivedAt`, `arrivalRecordedBy`.  
**Business Rules:** Doctor DTO excludes `sales_notes` and contact data; Sales DTO excludes clinical content. Arrived-today patients appear in the Sales appointment list, not as a status.  
**Validation / Rejection:** N/A  
**Result:** Audience DTOs.  
**Audit / History:** N/A  
**Permissions / Privacy:** FR-SEC-002 / FR-SEC-003.  
**Related Data:** `scheduling.*`  
**Related API Capability:** `GET /api/doctor/appointments`, `GET /api/sales/appointments`, `GET /api/manager/appointments`

---

## 11. Clinical Cases / Visits / Work

### FR-CLN-001

**Title:** Start a Visit  
**Actors:** Doctor (Manager holds `Clinical.Visits.Write` for non-finalized records only)  
**Preconditions:** `Clinical.Visits.Write`.  
**Trigger:** Doctor begins treating a patient (from the queue or directly).  
**Main Flow:** Visit created for the patient with Doctor = principal and optional `appointmentId`; status editable (Draft / In Progress); linked Scheduled / Confirmed appointment → In Treatment (FR-SCH-012).  
**Business Rules:** Visit ≠ Case: a Visit is one encounter; a case may span many Visits. Visit statuses: Draft → In Progress → Finalized. Finalizing protects that Visit's record, not the whole case.  
**Validation / Rejection:** N/A  
**Result:** Editable Visit.  
**Audit / History:** A.  
**Permissions / Privacy:** Doctor identity from principal.  
**Related Data:** `clinical.visits`  
**Related API Capability:** `POST /api/doctor/visits`, `PUT /api/doctor/visits/{id}`

### FR-CLN-002

**Title:** Record treatment: start or continue a treatment case  
**Actors:** Doctor  
**Preconditions:** `Clinical.Treatments.Write`; own editable Visit.  
**Trigger:** Doctor records work on a tooth / area.  
**Main Flow:** New case: case + treatment record created (procedure type, teeth, area, details, lab model snapshot). Existing case (any Doctor's): a treatment record for the signed-in Doctor in this Visit.  
**Business Rules:** Every procedure is a treatment case; cases span Visits through treatment records. One treatment record per (Visit, case). `started_by_doctor_id` is clinical context only (no revenue meaning). Case statuses: Planned → Active → Completed → Cancelled.  
**Validation / Rejection:** Catalog validation (FR-CLN-003).  
**Result:** Treatment record in the Visit.  
**Audit / History:** A; A+ on case status change.  
**Permissions / Privacy:** Doctor writes only own editable Visit and own work (S6, S9).  
**Related Data:** `clinical.treatment_cases`, `clinical.treatment_records`  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/treatments`, `PUT /api/doctor/visits/{visitId}/treatments/{recordId}`

### FR-CLN-003

**Title:** Procedure catalog and option validation  
**Actors:** Doctor  
**Preconditions:** N/A  
**Trigger:** Treatment details submitted.  
**Main Flow:** `details` validated against `procedure_types.option_definition`.  
**Business Rules:** Root Canal (tooth; canals 1–5 in mm; steps Access Cavity, Mechanical Preparation, Obturation, optional Post & Core); Fillings: GIC / Composite, Anterior / Posterior required only for Composite; Extraction: Simple / Surgical / Advanced Surgical (no sutures / bone removal); Pediatric: Pulpotomy, Pulpectomy, Space Maintainer, Stainless Steel Crown, Extraction (no type), Zircon Crown, Other (free-text name / details); Implant (healing 1–6 months, optional bone graft and membrane); Fixed Prosthetics: Porcelain/PFM, Zirconia, E-max; Removable Prosthetics: Complete / Partial; Flexible / Standard Acrylic; Scaling/Cleaning, Deep Cleaning, Polishing (no options); Laser Bleaching/Whitening: Basic / High Quality (only this type); Orthodontics (Upper Arch, Lower Arch, Appliance + free-text type); Other (free text). Lab model Fixed = Implant, Fixed Prosthetics, Removable Prosthetics; everything else General (Orthodontics General [T]). The catalog can evolve.  
**Validation / Rejection:** `catalog.option_invalid` for: composite zone on GIC / missing on Composite; extraction type on Pediatric Extraction; Basic / High Quality on anything but Laser Bleaching; missing appliance type for Appliance; canal outside 1–5; healing outside 1–6; Sutures / Bone Removal fields.  
**Result:** Valid case details.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `clinical.procedure_types`, `clinical.treatment_cases.details`  
**Related API Capability:** API A4

### FR-CLN-004

**Title:** Root canal steps and canal measurements  
**Actors:** Doctor  
**Preconditions:** `Clinical.Treatments.Write`; own editable Visit; Root Canal case.  
**Trigger:** Doctor completes a step / records canal lengths.  
**Main Flow:** Step Pending → Completed once (with Visit and Doctor); measurements per canal (1–5, mm) per Visit; latest per canal = current.  
**Business Rules:** Steps persist across Visits; Post & Core exists only if the Doctor adds it. Measurements become insert-only history after finalization.  
**Validation / Rejection:** Canal outside 1–5 rejected.  
**Result:** Step / measurement recorded.  
**Audit / History:** A.  
**Permissions / Privacy:** C-Clinical (never to Sales).  
**Related Data:** `clinical.treatment_steps`, `clinical.root_canal_measurements`  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/cases/{caseId}/steps/{stepCode}/complete`, `PUT …/canal-measurements`

### FR-CLN-005

**Title:** Implant details  
**Actors:** Doctor  
**Preconditions:** `Clinical.Treatments.Write`; Implant case.  
**Trigger:** Doctor records implant information.  
**Main Flow:** Bone graft, membrane, procedure date and healing months stored.  
**Business Rules:** Healing period 1–6 months (no hardcoded 3 months). No imaging field. An `ImplantFollowUp` appointment kind produces the Implant Follow-Up Tomorrow reminder (FR-NOTIF-003).  
**Validation / Rejection:** Healing outside 1–6 rejected.  
**Result:** Implant details.  
**Audit / History:** A.  
**Permissions / Privacy:** C-Clinical.  
**Related Data:** `clinical.implant_details`  
**Related API Capability:** `PUT /api/doctor/visits/{visitId}/cases/{caseId}/implant`

### FR-CLN-006

**Title:** Clinical notes on the Visit  
**Actors:** Doctor  
**Preconditions:** Own editable Visit.  
**Trigger:** Doctor writes notes.  
**Main Flow:** Notes stored on the Visit.  
**Business Rules:** Editable until finalization; afterwards only through FR-CLN-014.  
**Validation / Rejection:** Finalized → `visit.finalized`.  
**Result:** Notes saved.  
**Audit / History:** A.  
**Permissions / Privacy:** C-Clinical; never visible to Sales.  
**Related Data:** `clinical.visits.clinical_notes`  
**Related API Capability:** `PUT /api/doctor/visits/{id}`

### FR-CLN-007

**Title:** Doctor enters the initial price of own work  
**Actors:** Doctor  
**Preconditions:** `Clinical.WorkItems.EnterPrice`; own work item / own treatment record; no charge exists yet; `If-Match`.  
**Trigger:** Doctor prices the work they performed.  
**Main Flow:** Creates / updates **that Doctor's own** work item for the treatment record with `entered_price` and the Doctor percentage snapshot at creation.  
**Business Rules:** Only the performing Doctor enters / edits the price before finalization; the **Manager has no initial-price authority** (403). After the charge exists the price is frozen; a change is the Manager's audited correction (FR-FIN-017), never for an ortho Total.  
**Validation / Rejection:** Manager → 403; another Doctor's item → 404; charge exists → 422 `workitem.charge_exists`; new price below current discount → 422; price must be positive with ≤ 2 decimals.  
**Result:** Priced work item.  
**Audit / History:** A+.  
**Permissions / Privacy:** Body `{ price }` only.  
**Related Data:** `clinical.case_work_items`  
**Related API Capability:** `PUT /api/doctor/work-items/{id}/pricing`

### FR-CLN-008

**Title:** Per-Doctor priced work (multiple Doctors in one case)  
**Actors:** Doctor  
**Preconditions:** N/A  
**Trigger:** Another Doctor continues a case.  
**Main Flow:** Each Doctor prices the work they performed in their own Visit; each priced treatment record has one work item.  
**Business Rules:** Each Doctor earns only from own priced work; no automatic revenue sharing (Q-CLN-2). There is **no `separatelyPricedWork` flag**. A Visit with no price entered creates no work item and no charge. Orthodontics keeps a single work item / charge per ortho case. Example: Dr. A 3,000 (Visit 1) and Dr. B 2,000 (Visit 2) → two charges, each owned by its Doctor.  
**Validation / Rejection:** `separatelyPricedWork` member → 400; second price on an ortho case → 422.  
**Result:** Separate charges per Doctor at finalization.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `clinical.case_work_items` (unique `treatment_record_id`)  
**Related API Capability:** API A8 "Treatments and work items"

### FR-CLN-009

**Title:** Doctor percentage snapshot on work  
**Actors:** System  
**Preconditions:** Work item created.  
**Trigger:** FR-CLN-007.  
**Main Flow:** Copies the performing Doctor's current percentage into the work item; copied once to the charge at finalization.  
**Business Rules:** Snapshot frozen from creation together with the performing Doctor and case; never looked up again at payroll time.  
**Validation / Rejection:** N/A  
**Result:** Stable percentage for all collections on that work.  
**Audit / History:** N/A  
**Permissions / Privacy:** Never visible to Sales.  
**Related Data:** `case_work_items.doctor_percentage_snapshot`, `patient_charges.doctor_percentage_snapshot`  
**Related API Capability:** N/A

### FR-CLN-010

**Title:** Consultation Fee  
**Actors:** Doctor  
**Preconditions:** `Clinical.Visits.EnterConsultationFee`; own Visit before finalization.  
**Trigger:** Doctor enters the fee.  
**Main Flow:** `consultation_fee` stored on the Visit.  
**Business Rules:** Doctor only; the Manager cannot enter it; a later change is a Manager charge adjustment. Consultation becomes its own charge at finalization, earns no Doctor commission and is reported separately. It may carry a discount [T11].  
**Validation / Rejection:** Negative fee rejected; Manager → 403.  
**Result:** Fee to be charged at finalization.  
**Audit / History:** A.  
**Permissions / Privacy:** N/A  
**Related Data:** `clinical.visits.consultation_fee`  
**Related API Capability:** `PUT /api/doctor/visits/{id}`

### FR-CLN-011

**Title:** Treatment case lifecycle  
**Actors:** Doctor, Manager  
**Preconditions:** `Clinical.Treatments.Write`.  
**Trigger:** Case status change.  
**Main Flow:** Status among Planned, Active, Completed, Cancelled; `completed_at` set on completion.  
**Business Rules:** No OnHold / Abandoned states. Case completion does not establish charges (charges come only from Visit finalization).  
**Validation / Rejection:** Other statuses rejected.  
**Result:** Case status.  
**Audit / History:** A+.  
**Permissions / Privacy:** Sales sees treatment status through the whitelist summary.  
**Related Data:** `clinical.treatment_cases`  
**Related API Capability:** Treatment routes (API A8)

### FR-CLN-012

**Title:** Finalize a Visit  
**Actors:** Doctor (owner of the Visit)  
**Preconditions:** `Clinical.Visits.Finalize`; own Visit; Fixed-lab precondition (FR-LAB-007) and ortho Deposit precondition (FR-ORT-004) satisfied.  
**Trigger:** Doctor finalizes.  
**Main Flow:** One transaction: Visit → Finalized (`finalized_at` = Visit's actual end) → Finance establishes one Treatment charge per priced work item and one Consultation charge when a fee exists, each with the current discount snapshot (FR-FIN-001) → for an ortho line the initial schedule is generated (FR-ORT-004) → linked appointment Completed and `visit_ended_at` set (FR-SCH-013) → feedback request created (FR-FDB-001).  
**Business Rules:** No Sales / Manager approval step. Idempotent. Any failure rolls everything back.  
**Validation / Rejection:** 422 `fixed_lab.initial_required`; 422 `ortho.deposit_exceeds_effective_total`; discount > price rejected; another Doctor's Visit → 404.  
**Result:** Finalized Visit with established charges.  
**Audit / History:** A+ (finalize and each charge).  
**Permissions / Privacy:** N/A  
**Related Data:** `clinical.visits`, `finance.patient_charges`, `orthodontics.*`, `scheduling.appointments`, `feedback.feedback_requests`  
**Related API Capability:** `POST /api/doctor/visits/{id}/finalize`

### FR-CLN-013

**Title:** Finalized Visit protection and read breadth  
**Actors:** Doctor, Manager, System  
**Preconditions:** Visit Finalized.  
**Trigger:** Any write to Visit-owned clinical records.  
**Main Flow:** Rejected by command handlers and guard G1 unless it is the controlled correction (FR-CLN-014).  
**Business Rules:** All Doctors may read every patient's full clinical history (Visits, treatments, steps, measurements, prescriptions); read breadth never implies write rights. No reopen route. The Manager edits non-finalized records only and can never overwrite a finalized record.  
**Validation / Rejection:** `visit.finalized` (422); another Doctor's record → 404.  
**Result:** Historical evidence preserved.  
**Audit / History:** Doctor record opens audited.  
**Permissions / Privacy:** `Clinical.Visits.Read` (Doctor all, Manager).  
**Related Data:** G1 on treatment records, steps, measurements, prescriptions, items, Visit notes  
**Related API Capability:** `GET /api/doctor/patients/{id}/visits`, `GET /api/doctor/visits/{id}`, `GET /api/doctor/treatment-cases/{id}`

### FR-CLN-014

**Title:** Controlled correction of own finalized Visit  
**Actors:** Doctor (the Visit's own Doctor)  
**Preconditions:** `Clinical.Visits.CorrectFinalized`; Visit Finalized and owned by the principal.  
**Trigger:** Doctor corrects a clinical record of the finalized Visit.  
**Main Flow:** Submit record type (TreatmentRecord / TreatmentStep / RootCanalMeasurement / VisitNotes), record id, changed fields, optional reason → change applied → one `visit_corrections` row per corrected record + audit entry with previous and new values, same transaction.  
**Business Rules:** The Visit stays Finalized (never reopened); no Manager approval; no Manager or Sales correction right (Q-CLN-1). Price, discount and consultation fee are **not** correctable here (Manager Finance corrections only).  
**Validation / Rejection:** Other Doctor → 404; Manager / Sales → 403; price / discount / fee fields rejected.  
**Result:** Corrected record with full history.  
**Audit / History:** A+ (Doctor, timestamp, record, changed fields, correction relationship).  
**Permissions / Privacy:** N/A  
**Related Data:** `clinical.visit_corrections` (composite FK to the Visit's own Doctor)  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/corrections`

### FR-CLN-015

**Title:** Sales clinical summary (whitelist)  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Clinical.SalesSummary.Read`.  
**Trigger:** Sales opens a patient's treatment summary.  
**Main Flow:** Per case: procedure name, treating Doctor, status, referral destination, referral reason, follow-up instructions, price, discount, total, paid, remaining, ortho installments.  
**Business Rules:** Never notes, teeth / details, steps, measurements, medications, Doctor percentage, shares or lab.  
**Validation / Rejection:** N/A  
**Result:** `SalesClinicalSummaryDto`.  
**Audit / History:** N/A  
**Permissions / Privacy:** FR-SEC-003.  
**Related Data:** Clinical (read via `ISalesClinicalSummary`)  
**Related API Capability:** `GET /api/sales/patients/{id}/clinical-summary`

---

## 12. Prescriptions

### FR-RX-001

**Title:** Write a prescription  
**Actors:** Doctor (Manager holds the key for non-finalized records)  
**Preconditions:** `Prescriptions.Write`; own editable Visit.  
**Trigger:** Doctor prescribes.  
**Main Flow:** Prescription (Patient → Visit → Doctor) with general notes and multiple medication items (medication name, dosage, frequency, duration, instructions, order).  
**Business Rules:** Editable until the Visit is finalized; items are soft-removed (`removed_at`), never deleted.  
**Validation / Rejection:** Finalized Visit → 404 (use the correction route); `If-Match` on update.  
**Result:** Prescription.  
**Audit / History:** A.  
**Permissions / Privacy:** Medication details are C-Clinical; never visible to Sales.  
**Related Data:** `prescriptions.prescriptions`, `prescriptions.prescription_items`  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/prescriptions`, `PUT /api/doctor/prescriptions/{id}`

### FR-RX-002

**Title:** View all prescriptions  
**Actors:** Doctor, Manager  
**Preconditions:** `Prescriptions.Read`.  
**Trigger:** Open prescription history.  
**Main Flow:** All prescriptions of a patient readable by every Doctor.  
**Business Rules:** Read breadth does not imply write rights (Doctor B's prescription → 404 on write).  
**Validation / Rejection:** Sales → 403.  
**Result:** Prescription list / detail.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `prescriptions.*`  
**Related API Capability:** `GET /api/doctor/patients/{id}/prescriptions`, `GET /api/doctor/prescriptions/{id}`

### FR-RX-003

**Title:** Print prescription  
**Actors:** Doctor, Manager  
**Preconditions:** `Prescriptions.Print`.  
**Trigger:** Print request.  
**Main Flow:** PDF generated on demand from `PrescriptionPrintModel`: clinic name, logo, clinic's own contact, patient name, age, Doctor name, specialty, date, medications (dose, frequency, duration, instructions), signature / stamp area. Not stored.  
**Business Rules:** Clinic branding; Arabic application text; **no patient contact data**; no patient identifiers in PDF metadata; no QR / barcode with patient data; file name without patient name. Sales cannot print.  
**Validation / Rejection:** Sales → 403.  
**Result:** Downloaded PDF.  
**Audit / History:** Each print audited.  
**Permissions / Privacy:** PDF text canary scan has no contact value.  
**Related Data:** Print model (no contact fields)  
**Related API Capability:** `GET /api/doctor/prescriptions/{id}/print`

### FR-RX-004

**Title:** Controlled correction of own finalized prescription  
**Actors:** Doctor (owner)  
**Preconditions:** `Prescriptions.CorrectFinalized`; own prescription on a finalized Visit.  
**Trigger:** Doctor corrects the prescription or its items.  
**Main Flow:** Correction applied with a `clinical.visit_corrections` row (record type Prescription / PrescriptionItem) and an audit entry flagged `after_finalization` holding the previous version.  
**Business Rules:** Visit not reopened; no Manager approval (Q-CLN-1).  
**Validation / Rejection:** Other Doctor → 404; Sales / Manager denied.  
**Result:** Corrected prescription.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `prescriptions.*`, `clinical.visit_corrections`  
**Related API Capability:** `POST /api/doctor/prescriptions/{id}/corrections`

---

## 13. Referrals

### FR-REF-001

**Title:** Doctor creates a referral  
**Actors:** Doctor (Manager holds the key)  
**Preconditions:** `Crm.Referrals.Create`; own Visit.  
**Trigger:** Doctor refers the patient.  
**Main Flow:** Referral stored with patient, referring Doctor, referral type / destination (Surgery, Implant, Orthodontics, Prosthetics, Other), reason, Doctor notes, follow-up instructions; referral status New. The New Doctor Referral notification is enqueued in the same transaction (FR-NOTIF-006).  
**Business Rules:** `reason` and `notes` are different fields: `reason` is Sales-visible, `notes` is Doctor / Manager only.  
**Validation / Rejection:** Type outside the five values rejected.  
**Result:** Referral visible to Sales (without notes).  
**Audit / History:** A.  
**Permissions / Privacy:** FR-REF-003.  
**Related Data:** `crm.referrals`  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/referrals`

### FR-REF-002

**Title:** Doctor / Manager referral view  
**Actors:** Doctor, Manager  
**Preconditions:** `Crm.Referrals.Read.Clinical`.  
**Trigger:** Open a patient's referrals.  
**Main Flow:** Returns id, type, reason, **notes**, status, created at.  
**Business Rules:** Excludes Sales follow-up work, Original Sales Employee and patient source.  
**Validation / Rejection:** N/A  
**Result:** `DoctorReferralDto`.  
**Audit / History:** N/A  
**Permissions / Privacy:** No contact data.  
**Related Data:** `crm.referrals`  
**Related API Capability:** `GET /api/doctor/patients/{id}/referrals`

### FR-REF-003

**Title:** Sales referral view — reason yes, Doctor notes never  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.Referrals.Read.Sales`.  
**Trigger:** Sales lists / opens referrals.  
**Main Flow:** Returns id, patient, referral type (destination), reason, status, follow-up instructions.  
**Business Rules:** Sales sees Referral Destination / Type and Referral Reason unconditionally (Q-PRV-1; no visibility setting). The Doctor's referral `notes` property does not exist in the Sales DTO, Sales OpenAPI document, any Sales error body, export, notification or log.  
**Validation / Rejection:** A Sales request containing `notes` → 400 (unknown member).  
**Result:** `SalesReferralDto`.  
**Audit / History:** N/A  
**Permissions / Privacy:** Referral-notes canary never reaches Sales (SEC #64).  
**Related Data:** `crm.referrals` (projection without `notes`)  
**Related API Capability:** `GET /api/sales/referrals`, `GET /api/sales/referrals/{id}`

### FR-REF-004

**Title:** Referral follow-up status  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Crm.Referrals.UpdateStatus`; `If-Match`.  
**Trigger:** Sales progresses the referral.  
**Main Flow:** Referral status updated among New, Contacted, Booked, Completed, Lost.  
**Business Rules:** These referral statuses are distinct from the four Follow-Up statuses; a Sales follow-up task for a referral is a `crm.follow_ups` row with `referral_id` and follows FR-CRM-006.  
**Validation / Rejection:** Other values rejected.  
**Result:** Updated referral status.  
**Audit / History:** A.  
**Permissions / Privacy:** N/A  
**Related Data:** `crm.referrals.follow_up_status`, `crm.follow_ups.referral_id`  
**Related API Capability:** `PATCH /api/sales/referrals/{id}/status`

---

## 14. Charges / Payments / Patient Credit

### FR-FIN-001

**Title:** Charges are established at Visit finalization  
**Actors:** Doctor (finalizes); System (Finance)  
**Preconditions:** FR-CLN-012.  
**Trigger:** Visit finalization.  
**Main Flow:** Clinical passes Doctor, percentage snapshot, lab model, price, current discount (with setter, role, time) and consultation fee to Finance as data; Finance inserts one **Treatment** charge per priced work item and one **Consultation** charge when a fee was entered; `charged_at` = finalize time.  
**Business Rules:** Charges never exist before finalization (Q-FIN-W1). No Sales / Manager approval. Finance never calls Clinical. One charge per work item; one consultation charge per Visit.  
**Validation / Rejection:** `price > 0`; `0 ≤ discount ≤ price`; Fixed-lab precondition (FR-LAB-007).  
**Result:** Charges with Original Price, Discount, Final Price.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.patient_charges`  
**Related API Capability:** Internal `Finance.EstablishCharges` called by `POST /api/doctor/visits/{id}/finalize`

### FR-FIN-002

**Title:** Charge content and immutability  
**Actors:** System  
**Preconditions:** Charge established.  
**Trigger:** N/A  
**Main Flow:** Charge stores patient, kind, case, work item, Visit, performing Doctor, Doctor percentage snapshot, lab model, original price, discount amount, final price (= original − discount), discount setter / role / time, charged at.  
**Business Rules:** Insert-only; never updated. Treatment charges require work item, performing Doctor and snapshot; consultation charges have none of them. Agreement is never silently changed; any later change is a Manager adjustment row (FR-FIN-017) or, for ortho, a terms edit (FR-ORT-008).  
**Validation / Rejection:** CHECKs on kind-specific columns and `0 ≤ discount ≤ price`.  
**Result:** Permanent charge record.  
**Audit / History:** A+.  
**Permissions / Privacy:** Sales sees price, discount, total only.  
**Related Data:** `finance.patient_charges`  
**Related API Capability:** N/A

### FR-FIN-003

**Title:** Derived Billed, Collected, Outstanding  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any balance query.  
**Main Flow:** Billed = Σ charge final price + Σ adjustments; Collected = Σ allocation amounts (net of reversals / refunds); Outstanding = Billed − Collected (per charge and per patient); Cash received = Σ payments (net) by method; Patient Credit = Σ credit entries.  
**Business Rules:** Charge ≠ Payment. No stored balance column anywhere. Identity: Cash received = Collected on charges + Patient Credit. Example: 10,000 charge + 3,000 payment → Billed 10,000, Collected 3,000, Outstanding 7,000, Credit 0.  
**Validation / Rejection:** N/A  
**Result:** Derived figures.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Finance ledger tables  
**Related API Capability:** Balance queries (FR-FIN-010)

### FR-FIN-004

**Title:** Record a payment  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Finance.Payments.Record`; `Idempotency-Key` header.  
**Trigger:** Patient pays.  
**Main Flow:** One transaction: idempotency check → validate patient, active method (Cash, Visa, InstaPay), positive amount → lock target charge rows (`FOR UPDATE`, id order) and the patient's credit scope → insert payment (`Receipt`, `paid_at` = server time) → insert explicit allocations → insert remainder as Patient Credit → run Distribution Engine for each treatment / ortho allocation → audit.  
**Business Rules:** Doctors never record payments. No backdating in V1. Full, partial and multiple payments per charge; multiple charges per patient. Payments are never overwritten.  
**Validation / Rejection:** Same key + different payload → 409; missing key → 400; Doctor → 403; any failure rolls back everything.  
**Result:** `PaymentConfirmationDto` (payment id, amount, method, paid at, allocations, credit added, resulting outstanding, resulting credit balance).  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.payments`, `finance.payment_allocations`, `finance.patient_credit_entries`  
**Related API Capability:** `POST /api/sales/payments` (Manager uses the same route)

### FR-FIN-005

**Title:** Explicit payment allocations  
**Actors:** Sales / Reception, Manager  
**Preconditions:** FR-FIN-004.  
**Trigger:** Payment request lists allocations.  
**Main Flow:** One allocation row per requested (charge, amount) with `allocation_source = Payment`, `allocated_at` = server time (the collection moment, T1).  
**Business Rules:** Allocation is **manual / explicit**; there is no automatic oldest-first rule for charges. An allocation cannot exceed the charge's current Outstanding (the UI caps it; the API refuses).  
**Validation / Rejection:** Over-allocation → 422 `payment.exceeds_outstanding`.  
**Result:** Allocations.  
**Audit / History:** Insert-only.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.payment_allocations`  
**Related API Capability:** `POST /api/sales/payments`

### FR-FIN-006

**Title:** Overpayment and advance payment become Patient Credit  
**Actors:** Sales / Reception, Manager; System  
**Preconditions:** FR-FIN-004.  
**Trigger:** Payment amount exceeds Σ allocations, or a payment is made before any charge exists.  
**Main Flow:** The remainder (`amount − Σ allocations`) is inserted as a `FromPayment` (+) credit entry; a payment with no allocation is entirely credit.  
**Business Rules:** Credit belongs to the patient, persists and can be applied to later charges (Q-PAY-3). Example: 5,000 paid against Outstanding 3,000 → 3,000 allocated + 2,000 credit.  
**Validation / Rejection:** N/A  
**Result:** `creditAdded` in the response.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.patient_credit_entries`  
**Related API Capability:** `POST /api/sales/payments`

### FR-FIN-007

**Title:** Apply Patient Credit to a charge  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Finance.PatientCredit.Apply`; `Idempotency-Key`; credit balance and charge Outstanding > 0.  
**Trigger:** Staff applies credit.  
**Main Flow:** Oldest credit entries consumed first (FIFO bookkeeping); per consumed slice: an allocation (`allocation_source = Credit`, `payment_id` = funding payment, `allocated_at` = now) and an `AppliedToCharge` (−) credit entry; the Distribution Engine runs as for a direct payment.  
**Business Rules:** The collection moment is the application moment (T1).  
**Validation / Rejection:** Insufficient credit → 422 `credit.insufficient`; amount above Outstanding → 422.  
**Result:** Charge collected from credit.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.payment_allocations`, `finance.patient_credit_entries`  
**Related API Capability:** `POST /api/sales/patients/{id}/credit/apply`

### FR-FIN-008

**Title:** Patient Credit is a liability, never revenue  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any revenue, commission or entitlement computation.  
**Main Flow:** N/A  
**Business Rules:** Unapplied credit creates no Billed, Doctor entitlement / share, Lab, Clinic or Sales commission until applied to a charge. Credit balance is derived and never negative (checked under the patient lock). Reported as a memo line.  
**Validation / Rejection:** Any operation making credit negative → rejected.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Sales and Manager see credit balance and entries.  
**Related Data:** `finance.patient_credit_entries`  
**Related API Capability:** N/A

### FR-FIN-009

**Title:** Payment conservation  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Commit of any transaction touching a payment.  
**Main Flow:** Guard G8 verifies `payment.amount = Σ allocations(payment) + Σ credit entries(payment)`; no allocation pushes a charge's net allocated amount above Billed or below 0.  
**Business Rules:** Replaces the old "Σ allocations = payment" rule.  
**Validation / Rejection:** Violation → transaction rejected.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** G8 deferred trigger  
**Related API Capability:** N/A

### FR-FIN-010

**Title:** Patient balance, payment history and outstanding list  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Finance.PatientBalance.Read`.  
**Trigger:** Staff opens balances.  
**Main Flow:** Charges (price, discount, total), Billed, Collected, Outstanding, Patient Credit balance and entries, payment history; outstanding-balances list feeds dashboard and follow-ups.  
**Business Rules:** No shares, percentages or lab.  
**Validation / Rejection:** Doctor → 403.  
**Result:** Balance views.  
**Audit / History:** N/A  
**Permissions / Privacy:** C-Operational only.  
**Related Data:** Finance ledger  
**Related API Capability:** `GET /api/sales/patients/{id}/balance`, `GET /api/sales/patients/{id}/payments`, `GET /api/sales/outstanding-balances`

### FR-FIN-011

**Title:** No printed receipt or invoice  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** Payment confirmation is system data only.  
**Business Rules:** No printed receipt, printed invoice or tax invoice in V1 (Q-DOC-1); no print endpoint, receipt or invoice object.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** None  
**Related API Capability:** None (API A9)

### FR-FIN-012

**Title:** Manager Refund  
**Actors:** Manager  
**Preconditions:** `Finance.Payments.Correct`; `Idempotency-Key`; reason.  
**Trigger:** Money returned to the patient (partial allowed).  
**Main Flow:** `payment_corrections` header (Refund) + negative `payments` row (`kind = Refund`) with negative allocations (from a charge) and / or a negative credit entry (from credit); propagation per FR-FIN-016.  
**Business Rules:** Manager only (Q-PAY-1); new rows only; refund ≤ refundable (net collected on the allocation or unapplied credit); cannot be repeated beyond what remains. A refund of unapplied credit has no distribution effect.  
**Validation / Rejection:** Doctor / Sales → 403; over-refund → 422.  
**Result:** Reduced Collected / Credit and Cash received.  
**Audit / History:** A+ with reason.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.payment_corrections`, `payments`, `payment_allocations`, `patient_credit_entries`  
**Related API Capability:** `POST /api/manager/payments/{id}/refund`

### FR-FIN-013

**Title:** Manager Payment Reversal  
**Actors:** Manager  
**Preconditions:** `Finance.Payments.Correct`; `Idempotency-Key`; reason.  
**Trigger:** A receipt that should not exist.  
**Main Flow:** Header (Reversal) + negative `payments` row (`reverses_payment_id`), negative allocation rows for every allocation of the original, negative credit entries for its remaining unapplied credit; for ortho allocations, negative `installment_payments` rows for their applications; propagation per FR-FIN-016.  
**Business Rules:** Manager only; never edits or deletes the original.  
**Validation / Rejection:** Doctor / Sales → 403; cannot exceed what remains.  
**Result:** Money leaves Collected and Cash received.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** As FR-FIN-012, `orthodontics.installment_payments`  
**Related API Capability:** `POST /api/manager/payments/{id}/reversal`

### FR-FIN-014

**Title:** Manager Reallocation  
**Actors:** Manager  
**Preconditions:** `Finance.Payments.Correct`; `Idempotency-Key`; reason.  
**Trigger:** Money applied to the wrong charge.  
**Main Flow:** Negative allocation (same payment) + positive allocation to the right charge, or allocation ↔ credit moves with matching credit entries (`FromReallocation`).  
**Business Rules:** Cash received unchanged; Collected moves between charges; Doctor entitlement moves to the correct Doctor.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Corrected allocation.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** As FR-FIN-012  
**Related API Capability:** `POST /api/manager/payments/{id}/reallocate`

### FR-FIN-015

**Title:** Manager Payment Replacement (Payment Correction)  
**Actors:** Manager  
**Preconditions:** `Finance.Payments.Correct`; `Idempotency-Key`; reason.  
**Trigger:** Wrong amount or method recorded.  
**Main Flow:** Reversal + a new Receipt in the same correction.  
**Business Rules:** Net effect = corrected payment; history preserved.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Corrected payment.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** As FR-FIN-012  
**Related API Capability:** `POST /api/manager/payments/{id}/replace`

### FR-FIN-016

**Title:** Correction propagation  
**Actors:** System  
**Preconditions:** A correction inserted negative / moved allocations.  
**Trigger:** FR-FIN-012..015.  
**Main Flow:** Each negative allocation runs the Distribution Engine: general-lab money → exact negation using the original allocation's recorded lab % (FR-LAB-004); fixed-lab cases → `Reconcile(case)` delta rows (FR-LAB-010). Sales commission → negative item at the original rate (FR-PAYROLL-006).  
**Business Rules:** Resulting negative Doctor earnings carry `recognized_at` = now and reduce the **current open** Doctor payroll; sealed payrolls are never changed. Corrections propagate to Doctor entitlement, Lab, Clinic Share, Patient Credit and Payroll.  
**Validation / Rejection:** N/A  
**Result:** Ledger identities hold after the correction.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** Distribution tables  
**Related API Capability:** N/A

### FR-FIN-017

**Title:** Manager price / discount correction of a non-orthodontic charge  
**Actors:** Manager  
**Preconditions:** `Finance.Charges.Adjust`; `Idempotency-Key`; reason; charge is not an ortho charge.  
**Trigger:** Agreed price or discount must be corrected after establishment.  
**Main Flow:** Insert-only `charge_adjustments` row (`PriceCorrection` or `DiscountCorrection`, signed `amount_delta`, reason).  
**Business Rules:** After the charge exists only the Manager changes price / discount. Adjusted Billed may not fall below the charge's net Collected (reallocate / refund first). A correction never changes earlier distributions (entitlements follow money collected). **Refused for ortho charges** (FR-ORT-019).  
**Validation / Rejection:** Ortho charge → 422 `ortho.use_terms_edit`; Billed < Collected → 422; Doctor / Sales → 403; `OrthoTermsChange` from a client → rejected.  
**Result:** Adjusted Billed.  
**Audit / History:** A+ with reason.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.charge_adjustments`  
**Related API Capability:** `POST /api/manager/charges/{id}/adjustments`

### FR-FIN-018

**Title:** Doctor entitlement on every collection  
**Actors:** System  
**Preconditions:** An allocation to a Treatment or Ortho charge.  
**Trigger:** Payment allocation, credit application or correction.  
**Main Flow:** Insert one `doctor_entitlements` row: `round_half_up(allocated × charge.doctor_percentage_snapshot / 100, 2)`.  
**Business Rules:** Collected = Doctor Share + Lab + Clinic Share. Entitlement uses the work's snapshot, never the Doctor's current percentage. Lab is **not** removed before the Doctor percentage is applied. Entitlement is fixed at the collection and never changes with a lab revision.  
**Validation / Rejection:** N/A  
**Result:** Entitlement row (recognition per FR-LAB-002 / FR-LAB-008).  
**Audit / History:** A+.  
**Permissions / Privacy:** Never visible to Sales.  
**Related Data:** `finance.doctor_entitlements`, `finance.distribution_events`  
**Related API Capability:** N/A

### FR-FIN-019

**Title:** Consultation charges produce no Doctor commission  
**Actors:** System  
**Preconditions:** Allocation to a Consultation charge.  
**Trigger:** Collection.  
**Main Flow:** No entitlement, earning or distribution rows.  
**Business Rules:** Consultation fees are Clinic revenue, stored and reported as their own line. They do count in the Sales commission base [T3].  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.patient_charges.charge_kind = 'Consultation'`  
**Related API Capability:** N/A

### FR-FIN-020

**Title:** Distribution identity and non-negative Clinic Share  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Commit of any distribution event.  
**Main Flow:** Guard G8b: Σ(lab + clinic) + Σ Doctor earnings = event basis; per allocation 0 ≤ Σ earnings ≤ Σ entitlements; no cumulative per-allocation Clinic Share is negative.  
**Business Rules:** Rounding half-up; Clinic Share is always the remainder. **Clinic Share can never become negative** (general lab: validation T6; fixed lab: by construction of the waterfall).  
**Validation / Rejection:** Violation → transaction rejected.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.distribution_events`, `doctor_earnings`, `revenue_distributions`  
**Related API Capability:** N/A

---

## 15. Discounts

### FR-FIN-021

**Title:** Set a fixed-EGP Discount before Visit finalization  
**Actors:** Doctor (own pricing line), Sales / Reception (any line whose price exists), Manager (any line)  
**Preconditions:** `Finance.Discount.Set`; the line's price exists (Doctor-entered work price or consultation fee); the Visit is not Finalized; no charge exists for the line.  
**Trigger:** A discount is agreed.  
**Main Flow:** Append one `pricing_discount_entries` row with amount, actor, role (`DOCTOR` / `SALES_RECEPTION` / `MANAGER`), time and optional reason. Current discount of a line = highest `entry_seq` (0 when none).  
**Business Rules:** **Total = Price − Discount.** Discount is a **fixed EGP amount, never a percentage** (Q-FIN-3). Applies equally to work-item, consultation [T11] and ortho lines. The discount belongs to the pricing line, not to the person who last typed it.  
**Validation / Rejection:** Percentage → 400; discount > price → 422 `discount.exceeds_price`; finalized Visit → 422; Doctor on another Doctor's line → 404.  
**Result:** New current discount.  
**Audit / History:** A+ (every write: amount, actor, role, time).  
**Permissions / Privacy:** Sales uses the pricing view only (FR-FIN-022).  
**Related Data:** `clinical.pricing_discount_entries`  
**Related API Capability:** `PUT /api/doctor/pricing-lines/{lineId}/discount`, `PUT /api/sales/pricing-lines/{lineId}/discount` (Manager uses the Sales route)

### FR-FIN-022

**Title:** Sales pricing view without clinical data  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Finance.PricingLines.Read`.  
**Trigger:** Sales opens a patient's priced, not-yet-finalized lines.  
**Main Flow:** Returns per line: pricing-line id, kind (WorkItem / Consultation), patient {id, code, name}, procedure name, treating Doctor {id, name}, price, discount, total, `entrySeq`, last discount set by {role, at}.  
**Business Rules:** A Doctor-priced line is visible only once its price exists and the Visit is not finalized. Never clinical notes, teeth / details, steps, measurements, prescriptions, lab, Doctor percentage, entitlement, shares, commission or Doctor compensation.  
**Validation / Rejection:** N/A  
**Result:** `PricingLineSalesDto` list.  
**Audit / History:** N/A  
**Permissions / Privacy:** Discount access gives Sales no clinical-note access.  
**Related Data:** Clinical via `ISalesPricingLines`  
**Related API Capability:** `GET /api/sales/pricing-lines?patientId=`

### FR-FIN-023

**Title:** Discount concurrency  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** FR-FIN-021.  
**Trigger:** Two writers set the discount of one line concurrently.  
**Main Flow:** The request carries `expectedEntrySeq`; the first writer succeeds; the second collides on the unique (line, `entry_seq`).  
**Business Rules:** The set-discount command and finalization both lock the Visit row.  
**Validation / Rejection:** Stale token → 409 `discount.stale_entry`.  
**Result:** Exactly one success.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `pricing_discount_entries` partial unique indexes  
**Related API Capability:** As FR-FIN-021

### FR-FIN-024

**Title:** Discount snapshot at finalization; Manager-only afterwards  
**Actors:** System; Manager  
**Preconditions:** Visit finalization.  
**Trigger:** FR-CLN-012.  
**Main Flow:** The current discount (amount, setter, role, time) is copied onto the charge.  
**Business Rules:** After the charge exists Doctor and Sales can no longer change the discount; a change is a Manager `DiscountCorrection` (FR-FIN-017), refused for ortho charges. Commission and Doctor share follow collected money; no separate discount formula.  
**Validation / Rejection:** Doctor / Sales after finalization → 403 / 422.  
**Result:** Charge discount snapshot.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `patient_charges.discount_*`  
**Related API Capability:** N/A

### FR-FIN-025

**Title:** Price may not drop below the existing discount  
**Actors:** Doctor  
**Preconditions:** A discount exists on the line.  
**Trigger:** Doctor lowers the price (FR-CLN-007).  
**Main Flow:** N/A  
**Business Rules:** A Doctor lowering a price below the existing discount is refused [T11]; discount ≤ price is re-validated at finalization (G6).  
**Validation / Rejection:** 422.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `case_work_items.entered_price`, `pricing_discount_entries`  
**Related API Capability:** `PUT /api/doctor/work-items/{id}/pricing`

---

## 16. Doctor Financial Ownership

### FR-FIN-026

**Title:** Financial ownership stays with the performing Doctor  
**Actors:** System  
**Preconditions:** Charge established.  
**Trigger:** Any collection, correction or report.  
**Main Flow:** Entitlements and earnings are computed for `patient_charges.performing_doctor_id` at the charge's snapshot.  
**Business Rules:** Ownership = the Doctor who performed and priced the work, forever (Q-FIN-9). Patient assignment and financial ownership are different concepts.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `patient_charges.performing_doctor_id`  
**Related API Capability:** N/A

### FR-FIN-027

**Title:** Reassignment never moves money  
**Actors:** Manager; System  
**Preconditions:** FR-PAT-009.  
**Trigger:** Patient reassigned A → B, then a payment on A's work.  
**Main Flow:** Entitlement and earning go to Dr. A.  
**Business Rules:** Reassignment never delays or alters a payment, allocation, entitlement or earning. Earlier work stays in A's own-work financial view.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-FIN-028

**Title:** Earning belongs to the payroll month of the collection  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Earning recognized.  
**Main Flow:** `doctor_earnings.recognized_at` = server time of the collection (or of later recognition for fixed lab).  
**Business Rules:** Work in January, payment in March → March payroll. A pending fixed-lab amount enters the payroll month of the collection that settles it. No earning can appear in an already-finalized month.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.doctor_earnings.recognized_at`  
**Related API Capability:** N/A

### FR-FIN-029

**Title:** Doctor own-work financial view  
**Actors:** Doctor  
**Preconditions:** `Finance.OwnWork.Read`.  
**Trigger:** Doctor opens own work.  
**Main Flow:** One item per own work item: case, Visit, procedure, original price, discount, final price, collected, remaining, Doctor percentage, entitled, recognized, pending entitlement, Clinic Share, lab; fixed-lab work also "lab covered so far".  
**Business Rules:** Query starts from `performing_doctor_id = principal`; no Doctor id from the request. Never other Doctors' revenue or percentages, case / patient / clinic totals, clinic profit, expenses, Sales payroll or management reports. Manager → 404 (own route).  
**Validation / Rejection:** Another Doctor's item → 404.  
**Result:** `DoctorOwnWorkDto` list.  
**Audit / History:** N/A  
**Permissions / Privacy:** C-DoctorFinance own only.  
**Related Data:** Charges, allocations, entitlements, earnings, distributions  
**Related API Capability:** `GET /api/doctor/me/work-items`, `GET /api/doctor/me/work-items/{id}`

### FR-FIN-030

**Title:** Inactive Doctor keeps entitlement on earlier work  
**Actors:** Manager; System  
**Preconditions:** Doctor deactivated (FR-STAFF-009).  
**Trigger:** Collection on the inactive Doctor's earlier work.  
**Main Flow:** Entitlement and earning are created as usual; payroll drafts can be created for the inactive Doctor.  
**Business Rules:** Deactivation changes nothing financial; payroll never depends on `is_active`. (Different from Sales, FR-PAYROLL-005.)  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.doctor_earnings`, `payroll.doctor_payrolls`  
**Related API Capability:** `POST /api/manager/payroll/doctors/drafts`

### FR-FIN-031

**Title:** No attribution holds  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** N/A  
**Business Rules:** No attribution hold, pending Doctor attribution, `PatientReassigned` / `DoctorDeactivated` finance handler or reassignment-triggered finance route exists. Finance subscribes to no events.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** No hold tables  
**Related API Capability:** None (API A9)

---

## 17. General Lab

### FR-LAB-001

**Title:** General lab model applicability  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Collection on a Treatment / Ortho charge whose lab model is General.  
**Main Flow:** N/A  
**Business Rules:** General = every procedure not Fixed (Fixed = Implant, Fixed Prosthetics, Removable Prosthetics). Orthodontics uses General [T placeholder].  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `procedure_types.lab_model`, `treatment_cases.lab_model_snapshot`, `patient_charges.lab_model`  
**Related API Capability:** N/A

### FR-LAB-002

**Title:** General lab distribution at the allocation-time rate  
**Actors:** System  
**Preconditions:** FR-FIN-018 entitlement row.  
**Trigger:** Allocation to a General-lab charge.  
**Main Flow:** In the same event: Doctor earning = entitlement (recognized immediately); lab = `round_half_up(allocated × lab % at allocation time / 100, 2)`; clinic = allocated − Doctor − lab.  
**Business Rules:** The lab % is the latest `lab_percentage_history` row at `allocated_at` (payment time for a direct payment; application time for credit) (Q-FIN-W2) and is recorded on the distribution row. A later lab % change never changes an earlier distribution or any entitlement. Example: 100 collected, Doctor 40 %, Lab 20 % → 40 + 20 + 40; after Lab 15 %, next 100 → 40 + 15 + 45. Case A: 10,000 price, 40 %, Lab 15 %, payment 2,000 → Doctor 800 + Lab 300 + Clinic 900.  
**Validation / Rejection:** N/A  
**Result:** Distribution rows.  
**Audit / History:** Insert-only.  
**Permissions / Privacy:** Lab data never to Sales.  
**Related Data:** `finance.revenue_distributions`, `finance.doctor_earnings`, `configuration.lab_percentage_history`  
**Related API Capability:** N/A

### FR-LAB-003

**Title:** Configure the General Lab percentage  
**Actors:** Manager  
**Preconditions:** `Finance.LabPercentage.Configure`.  
**Trigger:** Manager sets a new lab %.  
**Main Flow:** Insert a history row (effective from now).  
**Business Rules:** Current = latest row. Validation T6: Doctor % + Lab % ≤ 100 for any Doctor percentage currently stored or snapshotted on a charge with Outstanding > 0.  
**Validation / Rejection:** Violation → 422.  
**Result:** New rate for later allocations.  
**Audit / History:** Insert-only history; A.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `configuration.lab_percentage_history`  
**Related API Capability:** `GET/PUT /api/manager/settings/lab-percentage`

### FR-LAB-004

**Title:** General-lab corrections use the recorded rate  
**Actors:** System  
**Preconditions:** Reversal / refund touching a general-lab allocation.  
**Trigger:** FR-FIN-016.  
**Main Flow:** Recompute the target for the reduced effective amount using the allocation's recorded lab % and insert delta rows.  
**Business Rules:** Today's lab % is never applied to old money.  
**Validation / Rejection:** N/A  
**Result:** Identity restored.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `revenue_distributions.lab_percentage_applied`  
**Related API Capability:** N/A

---

## 18. Fixed Lab

### FR-LAB-005

**Title:** One Fixed Lab obligation per case  
**Actors:** System  
**Preconditions:** Case procedure type has lab model Fixed (Implant, Fixed Prosthetics, Removable Prosthetics).  
**Trigger:** Fixed Lab entry.  
**Main Flow:** One `fixed_lab_expenses` row per case; amounts live in revisions (current = latest).  
**Business Rules:** One obligation `L` per case however many Doctors, charges or payments; never attached to a charge, payment or Doctor. A second Doctor can never create a duplicate obligation.  
**Validation / Rejection:** Second Fixed Lab → 409 `fixed_lab.already_exists`; non-Fixed procedure → rejected (G7(a)).  
**Result:** N/A  
**Audit / History:** A+.  
**Permissions / Privacy:** Sales never sees lab data.  
**Related Data:** `finance.fixed_lab_expenses`, `finance.fixed_lab_revisions`  
**Related API Capability:** N/A

### FR-LAB-006

**Title:** Doctor enters the initial Fixed Lab expense  
**Actors:** Doctor (performing priced work on the case)  
**Preconditions:** `Finance.FixedLab.SetInitial`; case has no Fixed Lab yet.  
**Trigger:** Doctor records the lab amount (for Fixed / Removable Prosthetics the Doctor determines both the price and the Lab Expense).  
**Main Flow:** Create the case's Fixed Lab with revision 1 (amount, Doctor, role `DOCTOR`, time).  
**Business Rules:** Explicit amount only; an entered 0 means "no lab cost". The Manager can never be the initial actor. Revision 1 precedes every charge and collection, so it produces no distribution event.  
**Validation / Rejection:** Manager → 403; existing Fixed Lab → 409; negative → rejected.  
**Result:** Revision 1.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.fixed_lab_expenses`, `finance.fixed_lab_revisions` (revision 1 role CHECK)  
**Related API Capability:** `POST /api/doctor/fixed-lab/{caseId}`

### FR-LAB-007

**Title:** Unknown Fixed Lab is never zero — finalize precondition  
**Actors:** Doctor; System  
**Preconditions:** A Visit contains Fixed-lab priced work.  
**Trigger:** Visit finalization.  
**Main Flow:** If the case has no revision 1, finalization is refused and nothing is established or distributed.  
**Business Rules:** Revision 1 must exist before the Visit that establishes the case's first Fixed-lab Treatment charge can be finalized [T13]. `L` is never defaulted to 0; the waterfall refuses to run without revision 1 (G7(b)).  
**Validation / Rejection:** 422 `fixed_lab.initial_required`.  
**Result:** No charge, entitlement or distribution exists.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** G7 deferred trigger  
**Related API Capability:** `POST /api/doctor/visits/{id}/finalize`

### FR-LAB-008

**Title:** Lab-First collection order (waterfall)  
**Actors:** System  
**Preconditions:** Allocation to a Fixed-lab charge.  
**Trigger:** Collection, credit application, correction or revision.  
**Main Flow:** For each effective allocation in `allocation_seq` order: lab_i = min(a_i, max(L − labPaid, 0)); rest = a_i − lab_i; entitlement_i = a_i × p_i appended to a FIFO queue; rest pays the queue FIFO (oldest entitlement first, then Doctor id) → Doctor recognized; whatever remains → Clinic.  
**Business Rules:** (1) Fixed Lab covered first; (2) then Doctor entitlement recognized; (3) unpaid entitlement stays **pending**; (4) pending carries forward to later collections; (5) Clinic receives only the remainder; (6) Clinic Share never negative (Q-FIN-1). Owner example: L 3,000, 40 %, charge 10,000, payments 2,000 / 3,000 / 5,000 → P1 Lab 2,000, entitlement 800 pending, Clinic 0; P2 Lab 1,000, new entitlement 1,200, pending 2,000 settled, Clinic 0; P3 Doctor 2,000, Clinic 3,000; totals Lab 3,000, Doctor 4,000, Clinic 3,000.  
**Validation / Rejection:** N/A  
**Result:** Entitlement, earning (possibly lagging) and distribution rows.  
**Audit / History:** Insert-only.  
**Permissions / Privacy:** N/A  
**Related Data:** `doctor_entitlements`, `doctor_earnings`, `revenue_distributions`, `distribution_events`  
**Related API Capability:** N/A

### FR-LAB-009

**Title:** Pending Doctor entitlement  
**Actors:** System; Manager (reads)  
**Preconditions:** Fixed-lab case.  
**Trigger:** Collection that cannot fund the entitlement.  
**Main Flow:** Pending = entitlement − earnings, carried forward and paid FIFO from later collections.  
**Business Rules:** The entitlement is preserved even while recognition is deferred; if charges can never fund it (e.g. L > (1 − p) × Billed) the remainder stays pending indefinitely, never taken from the Clinic into negative. Pending entitlement is never in a payroll and never labelled Clinic profit; it is a memo line.  
**Validation / Rejection:** N/A  
**Result:** Pending figure visible to the Doctor (own) and the Manager.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Derived  
**Related API Capability:** `GET /api/manager/finance/pending-entitlements`; `DoctorOwnWorkDto.pendingEntitlement`

### FR-LAB-010

**Title:** Manager revises the Fixed Lab  
**Actors:** Manager  
**Preconditions:** `Finance.FixedLab.Revise`; a Fixed Lab already exists; reason.  
**Trigger:** Lab cost changes.  
**Main Flow:** Insert revision n > 1 (role `MANAGER`, reason) → `Reconcile(case)` recomputes the waterfall and inserts only delta rows in a `FixedLabRevision` distribution event.  
**Business Rules:** Existing rows are never touched; Reconcile is idempotent. The Doctor percentage and entitlements never change; only recognition timing and Lab / Clinic split change. Lower L → previously pending entitlement recognized now; higher L → negative earnings now. Deltas land in the **current open** payroll month; finalized / paid payrolls are untouched (Q-FIN-6). Reversal / refund / reallocation on a fixed-lab case also triggers Reconcile.  
**Validation / Rejection:** No initial entry → 409 `fixed_lab.initial_required`; Doctor → 403.  
**Result:** Revised `L`.  
**Audit / History:** A+ with reason and revision reference.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `finance.fixed_lab_revisions`, distribution tables  
**Related API Capability:** `PUT /api/manager/fixed-lab/{caseId}`

---

## 19. Orthodontics

### FR-ORT-001

**Title:** Create an Orthodontic case  
**Actors:** Doctor  
**Preconditions:** `Orthodontics.Cases.Create` **and** `Orthodontics.Terms.SetInitial`; own editable Visit.  
**Trigger:** Doctor starts orthodontic treatment.  
**Main Flow:** Case created with area (Upper Arch / Lower Arch / Appliance) and appliance type (free text, required for Appliance), the case's single work item (price = Gross) and terms revision 1 (FR-ORT-002).  
**Business Rules:** An ortho case has exactly one work item and one charge; a later Visit cannot add a second price. Doctor percentage snapshot is fixed at case start and applies to every collection.  
**Validation / Rejection:** Missing appliance type → `catalog.option_invalid`; Manager / Sales → 403 (they lack `SetInitial`).  
**Result:** Ortho case with stored terms; **no charge and no installment yet**.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `orthodontics.ortho_cases`, `clinical.case_work_items`  
**Related API Capability:** `POST /api/doctor/visits/{visitId}/orthodontic-cases`

### FR-ORT-002

**Title:** Doctor enters initial Ortho terms (stored only)  
**Actors:** Doctor  
**Preconditions:** `Orthodontics.Terms.SetInitial`; own case; editable Visit.  
**Trigger:** FR-ORT-001.  
**Main Flow:** Store revision 1: Gross Total Price, Deposit, Number of Months (role `DOCTOR`).  
**Business Rules:** Once, by the Doctor only (Q-ORT-4). The Manager cannot set initial terms; Sales can never set or edit terms. Nothing else is created until finalization.  
**Validation / Rejection:** Gross ≥ 0, Deposit ≥ 0, Deposit ≤ Gross, Months ≥ 1.  
**Result:** Terms revision 1.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `orthodontics.ortho_term_revisions`  
**Related API Capability:** As FR-ORT-001

### FR-ORT-003

**Title:** Effective Ortho Total  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any ortho balance or schedule rule.  
**Main Flow:** N/A  
**Business Rules:** Gross Ortho Total = the Total the Doctor enters (later increased only by the Manager). Discount = fixed-EGP pre-finalization discount on the ortho line (FR-FIN-021), snapshotted at finalization and never re-applied. **Effective Ortho Total = Gross − Discount = Ortho Charge Billed.** Remaining = Effective − Deposit. The schedule always balances against the Effective Ortho Total, never the undiscounted Gross.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Charge original / discount / final; terms revisions  
**Related API Capability:** Ortho read (`grossTotal`, `discount`, `effectiveOrthoTotal`)

### FR-ORT-004

**Title:** Charge and initial installments generated at Visit finalization  
**Actors:** Doctor (finalizes); System  
**Preconditions:** FR-CLN-012 with an ortho line.  
**Trigger:** Visit finalization.  
**Main Flow:** Discount snapshotted → charge established (original = Gross, discount, final = Effective) → Orthodontics validates 0 ≤ Deposit ≤ Effective → sets `charge_id` → generates `Months` equal `Generated` installments from `Effective − Deposit`, the last absorbing rounding; first due date one calendar month after the charge date [T].  
**Business Rules:** Generated **once**, never before finalization and never twice (G10). The Deposit is never silently altered. Example: Gross 20,000, Discount 2,000, Deposit 5,000, Months 5 → Effective 18,000; 5 × 2,600 = 13,000; 5,000 + 13,000 = 18,000; no extra 2,000 outstanding.  
**Validation / Rejection:** Deposit > Effective → 422 `ortho.deposit_exceeds_effective_total`; the whole finalize fails, nothing is written, the Visit stays unfinalized.  
**Result:** Ortho charge and initial schedule.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `finance.patient_charges`, `orthodontics.ortho_installments`, `ortho_installment_revisions`  
**Related API Capability:** `POST /api/doctor/visits/{id}/finalize`

### FR-ORT-005

**Title:** Ortho read model  
**Actors:** Doctor, Sales / Reception, Manager  
**Preconditions:** `Orthodontics.Cases.Read`.  
**Trigger:** Open an ortho case.  
**Main Flow:** Per installment: number, origin (Generated / ManagerAdded / PriceChange), current due date, current amount, applied amount (`amountAlreadyPaid`), remaining, status, `isActive`. Per case: charge Collected / Outstanding, deposit paid, unapplied ortho collections, `grossTotal`, `discount`, `effectiveOrthoTotal`, `scheduleDifference`, `monthPlanDifference`, `requiresScheduleAdjustment`.  
**Business Rules:** Retired installments appear only in history (not payable). Percentages / shares only on the Doctor's own work. No `applicationState: "Pending"` field.  
**Validation / Rejection:** N/A  
**Result:** Ortho DTO.  
**Audit / History:** N/A  
**Permissions / Privacy:** Sales sees ortho installment / payment info (whitelist).  
**Related Data:** Orthodontics tables (derived values)  
**Related API Capability:** `GET /api/{doctor|sales}/orthodontic-cases/{id}`

### FR-ORT-006

**Title:** Manual installment payment application  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Orthodontics.Installments.ApplyPayment`; an allocation to this ortho charge.  
**Trigger:** After a payment is allocated to the ortho charge (FR-FIN-004), staff chooses installment(s).  
**Main Flow:** Body lists `{ allocationId, installmentId, amount }` rows (one→one, one→many, partial, early, future).  
**Business Rules:** **No automatic oldest-first** (Q-ORT-2). Applications are capped by the allocation and the installment's current amount (G5); only active installments accept applications. The Distribution Engine runs at allocation time, not at application time. Deposit paid = min(deposit, ortho allocations − Σ applications).  
**Validation / Rejection:** Over-application → 422 `ortho.application_exceeds_allocation`; retired installment → rejected; Doctor → 403.  
**Result:** Application rows; installment status updated (derived).  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `orthodontics.installment_payments`  
**Related API Capability:** `POST /api/sales/orthodontic-cases/{id}/installment-payments`

### FR-ORT-007

**Title:** Derived installment status  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Read.  
**Main Flow:** Status ∈ Upcoming, Due, Partially Paid, Paid, Overdue derived from applications, current amount, current due date and clinic-local today (remaining 0 ⇒ Paid).  
**Business Rules:** Never stored.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Derived  
**Related API Capability:** Ortho read

### FR-ORT-008

**Title:** Manager edits Ortho terms (the only route)  
**Actors:** Manager  
**Preconditions:** `Orthodontics.Terms.Edit`; charge established; reason; `If-Match`.  
**Trigger:** Gross Total, Deposit or Months must change.  
**Main Flow:** Insert a terms revision (role `MANAGER`, reason) and apply FR-ORT-009 / 010 / 011 / 012.  
**Business Rules:** The **only** way to change an established Gross Total, Deposit or Months; generic charge adjustments never apply (FR-ORT-019).  
**Validation / Rejection:** Before finalization → 422 `ortho.charge_not_established`; Doctor (after initial entry) / Sales → 403.  
**Result:** New current terms.  
**Audit / History:** A+ (actor, role, time, previous and new Gross / Deposit / Months, reason).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `orthodontics.ortho_term_revisions`  
**Related API Capability:** `PUT /api/manager/orthodontic-cases/{id}/terms`

### FR-ORT-009

**Title:** Ortho total increase adds one difference installment  
**Actors:** Manager  
**Preconditions:** FR-ORT-008; new Gross > current Gross.  
**Trigger:** Terms edit with a higher Gross.  
**Main Flow:** Insert terms revision + `charge_adjustments` (`OrthoTermsChange`, +Δ) + exactly one `PriceChange` installment of Δ (due one month after the latest active due date, or after the charge date if none [T]).  
**Business Rules:** Existing installments, applications and allocations untouched; nothing redistributed (Q-ORT-3). The Discount snapshot is **not** re-applied. A `PriceChange` installment is distinguishable and never counts toward the Months plan. Example: Gross 20,000 → 24,000 with Discount 2,000 → Billed 18,000 → 22,000; 5,000 + 13,000 + 4,000 = 22,000.  
**Validation / Rejection:** N/A  
**Result:** Billed +Δ.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `ortho_term_revisions`, `finance.charge_adjustments`, `ortho_installments`  
**Related API Capability:** As FR-ORT-008

### FR-ORT-010

**Title:** Ortho total decrease rejected in V1  
**Actors:** Manager  
**Preconditions:** FR-ORT-008; new Gross < current Gross.  
**Trigger:** Terms edit with a lower Gross.  
**Main Flow:** Rejected before any write.  
**Business Rules:** Not supported in V1 (Q-ORT-3b, DEFERRED / OPEN FOR FUTURE VERSION). No negative installment, no automatic reduction, no `DiscountCorrection` / `PriceCorrection` workaround, no silent rewrite.  
**Validation / Rejection:** 422 `ortho.total_decrease_not_supported` (backstop G9(b)).  
**Result:** Nothing written.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** As FR-ORT-008

### FR-ORT-011

**Title:** Deposit change after installments exist  
**Actors:** Manager  
**Preconditions:** FR-ORT-008.  
**Trigger:** Terms edit changing the Deposit.  
**Main Flow:** Only the terms revision is written.  
**Business Rules:** No regeneration, addition, removal or redistribution (Q-ORT-5). The case shows `requires_schedule_adjustment` until the Manager's batch rebalances (FR-ORT-014..018). Example: Effective 18,000, Deposit 5,000 → 7,000 → required Σ = 11,000.  
**Validation / Rejection:** Deposit outside 0..Effective → 422 `ortho.deposit_exceeds_effective_total`.  
**Result:** `requiresScheduleAdjustment: true`, `scheduleDifference` returned.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `ortho_term_revisions`  
**Related API Capability:** As FR-ORT-008

### FR-ORT-012

**Title:** Number of Months change after installments exist  
**Actors:** Manager  
**Preconditions:** FR-ORT-008.  
**Trigger:** Terms edit changing Months.  
**Main Flow:** Only the terms revision is written; nothing is generated, added or removed.  
**Business Rules:** Months ≥ 1 and ≥ the number of active regular installments with payment history. `month_plan_difference` becomes ≠ 0 (10 → 12: +2; 10 → 8: −2) until the Manager adds / retires regular installments. Months is never dead metadata.  
**Validation / Rejection:** 422 `ortho.months_conflict_with_paid_history`.  
**Result:** `requiresScheduleAdjustment: true`, `monthPlanDifference` returned.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `ortho_term_revisions`  
**Related API Capability:** As FR-ORT-008

### FR-ORT-013

**Title:** Derived schedule validity flag  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Read.  
**Main Flow:** `schedule_difference` = Effective − Deposit − Σ active current amounts; `month_plan_difference` = Months − active regular (Generated / ManagerAdded) installments; `requires_schedule_adjustment` = either ≠ 0.  
**Business Rules:** Derived read-model fields, **not a lifecycle status**. A schedule needing adjustment is never treated as valid or finalized. Payments never change the equation.  
**Validation / Rejection:** N/A  
**Result:** Warning shown with both differences.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Derived  
**Related API Capability:** Ortho read

### FR-ORT-014

**Title:** Manager schedule batch — revise installment amount  
**Actors:** Manager  
**Preconditions:** `Orthodontics.Installments.Edit`; reason; `If-Match`.  
**Trigger:** Manager adjusts amounts.  
**Main Flow:** New `ManagerEdit` revision with the new amount.  
**Business Rules:** Amount ≥ `amount_already_paid` and ≥ 0 (0 allowed for an unpaid installment). Part of an atomic batch (FR-ORT-018).  
**Validation / Rejection:** Below paid → 422 `ortho.installment_below_paid_amount`; negative → 422.  
**Result:** Revised amount; previous amount kept in history.  
**Audit / History:** A+ (actor, role, time, previous / new, reason).  
**Permissions / Privacy:** Manager only (Doctor / Sales 403).  
**Related Data:** `ortho_installment_revisions`  
**Related API Capability:** `PUT /api/manager/orthodontic-cases/{id}/installments`

### FR-ORT-015

**Title:** Manager schedule batch — revise due date of an unpaid installment  
**Actors:** Manager  
**Preconditions:** As FR-ORT-014; installment has no applications.  
**Trigger:** Manager re-dates an installment.  
**Main Flow:** New `ManagerEdit` revision with the new due date.  
**Business Rules:** Previous due date stays in the revision history.  
**Validation / Rejection:** Installment with payment history → 422 `ortho.installment_has_payment_history`.  
**Result:** Revised due date.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `ortho_installment_revisions`  
**Related API Capability:** As FR-ORT-014

### FR-ORT-016

**Title:** Manager schedule batch — add an installment manually  
**Actors:** Manager  
**Preconditions:** As FR-ORT-014.  
**Trigger:** Months increased or rebalancing needed.  
**Main Flow:** New `ManagerAdded` installment (next number) with revision 1: due date, amount, Manager, time, reason.  
**Business Rules:** Due date ≥ clinic-local today; amount ≥ 0; starts with no payments. No route creates an installment outside this batch.  
**Validation / Rejection:** Past due date → 422.  
**Result:** New active regular installment.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `ortho_installments`, `ortho_installment_revisions`  
**Related API Capability:** As FR-ORT-014

### FR-ORT-017

**Title:** Manager schedule batch — retire an unpaid installment  
**Actors:** Manager  
**Preconditions:** As FR-ORT-014; installment has no payment history at all.  
**Trigger:** Months decreased or rebalancing needed.  
**Main Flow:** New `ManagerRetire` revision (`is_active = false`, amount 0).  
**Business Rules:** Retirement is a revision, never a delete; a retired installment is never re-activated (a new one is added instead) and cannot receive applications.  
**Validation / Rejection:** Any payment history → 422 `ortho.installment_has_payment_history`.  
**Result:** Installment shown in history only.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `ortho_installment_revisions`  
**Related API Capability:** As FR-ORT-014

### FR-ORT-018

**Title:** Schedule batch is atomic and must end valid  
**Actors:** Manager  
**Preconditions:** FR-ORT-014..017 operations in one request `{ reason, changes: [...] }`.  
**Trigger:** Batch submission.  
**Main Flow:** All operations applied in one transaction; validated at the end.  
**Business Rules:** At the end: no negative active amount; every amount ≥ paid; no paid installment retired / re-dated; active regular installments = Months; **Deposit + Σ active installments = Effective Ortho Total**. Otherwise nothing is written.  
**Validation / Rejection:** 422 `ortho.schedule_unbalanced`; 422 `ortho.month_plan_mismatch`; plus FR-ORT-014..017 codes.  
**Result:** Valid schedule; `requiresScheduleAdjustment: false`.  
**Audit / History:** A+ per revision.  
**Permissions / Privacy:** Manager only.  
**Related Data:** Orthodontics tables  
**Related API Capability:** `PUT /api/manager/orthodontic-cases/{id}/installments`

### FR-ORT-019

**Title:** Paid history protection and no generic adjustments on ortho charges  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any terms edit, schedule batch or charge adjustment on an ortho case.  
**Main Flow:** N/A  
**Business Rules:** No installment below its paid amount; none with payment history deleted, retired or re-dated; no payment moved between installments; `payment_allocations` / `installment_payments` never changed by a terms or schedule edit; no negative installment ever. Generic `PriceCorrection` / `DiscountCorrection` / `Finance.Charges.Adjust` can never change an Ortho Total (G9).  
**Validation / Rejection:** 422 `ortho.use_terms_edit` for generic adjustments on an ortho charge.  
**Result:** Immutable payment history.  
**Audit / History:** N/A  
**Permissions / Privacy:** No DELETE grant on installment tables.  
**Related Data:** G5(b)–(d), G9, G10  
**Related API Capability:** `POST /api/manager/charges/{id}/adjustments` (refuses ortho)

### FR-ORT-020

**Title:** Refund / reversal / correction of ortho payments  
**Actors:** Manager  
**Preconditions:** FR-FIN-012..015.  
**Trigger:** Correction of an ortho payment.  
**Main Flow:** Normal finance correction flow; negative `installment_payments` rows for applications of a reversed allocation.  
**Business Rules:** Never touches Gross, Deposit, Months or the installment schedule. Sales / Manager manual application keeps working afterwards.  
**Validation / Rejection:** As FR-FIN-012..015.  
**Result:** N/A  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** Finance ledger, `installment_payments`  
**Related API Capability:** Manager correction routes

### FR-ORT-021

**Title:** Doctor and Sales cannot edit terms or schedule  
**Actors:** Doctor, Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Any attempt.  
**Main Flow:** N/A  
**Business Rules:** After the initial entry the Doctor cannot edit terms; Doctor and Sales never edit installment amounts, due dates, additions or retirements; Sales never sets terms. Sales may only apply payments.  
**Validation / Rejection:** 403.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** SEC #34, #71, #72.  
**Related Data:** N/A  
**Related API Capability:** `PUT …/terms`, `PUT …/installments`

---

## 20. Sales Commission

### FR-PAYROLL-001

**Title:** Commission base = collected money of Lead-sourced patients  
**Actors:** System; Manager (payroll)  
**Preconditions:** Patient is Lead-sourced with an Original Sales Employee.  
**Trigger:** Sales payroll draft for a month.  
**Main Flow:** One commission item per net `payment_allocations` row (all charge kinds [T3]) of the employee's Lead-sourced patients allocated in the calendar month (`allocated_at`, clinic-local): `commission = round_half_up(base × commission_percentage_applied / 100, 2)`.  
**Business Rules:** Base = **actual collected money after the charge discount** (Q-FIN-2): never billed value, undiscounted price, uncollected value or Clinic Share. An allocation yields at most one commission item, ever. Attendance never changes commission.  
**Validation / Rejection:** G2 insert trigger verifies employee = Original Sales Employee, month, deactivation date, effective rate revision, Draft payroll.  
**Result:** Commission items on the Sales payroll.  
**Audit / History:** Insert-only.  
**Permissions / Privacy:** Sales sees only own compensation.  
**Related Data:** `payroll.sales_payroll_commission_items`  
**Related API Capability:** `/api/manager/payroll/sales` drafts

### FR-PAYROLL-002

**Title:** Rate at collection time, snapshotted per item  
**Actors:** System  
**Preconditions:** Rate history exists (revision 1 at creation).  
**Trigger:** Commission item creation.  
**Main Flow:** Rate = revision with the greatest `effective_from ≤ allocated_at` [T12]; the item stores `rate_revision_id` and `commission_percentage_applied`.  
**Business Rules:** Each payroll item snapshots the percentage it used; the payroll header has no single percentage; a month with a rate change shows both rates.  
**Validation / Rejection:** Item with a non-effective revision → rejected (G2).  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `staff.sales_commission_rate_history`, commission items  
**Related API Capability:** N/A

### FR-PAYROLL-003

**Title:** New commission rate applies to future collections only  
**Actors:** Manager; System  
**Preconditions:** FR-STAFF-004.  
**Trigger:** Rate change.  
**Main Flow:** N/A  
**Business Rules:** Old collections, Draft items and finalized payroll are never recalculated (Q-COMM-1). Check: 5 % on 1,000 = 50; after change to 7 %, next 1,000 = 70; total 120; the first item still shows 5 % / 50; finalized payroll byte-identical.  
**Validation / Rejection:** No recalculation route exists.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** As FR-PAYROLL-002  
**Related API Capability:** None for recalculation (API A9)

### FR-PAYROLL-004

**Title:** Walk-In / Direct patients generate zero commission  
**Actors:** System  
**Preconditions:** Patient source `WalkIn`.  
**Trigger:** Collection from a Walk-In / Direct patient.  
**Main Flow:** No commission item is produced.  
**Business Rules:** Sales commission = 0; **no shared Walk-In pool**, no equal split, no implicit attribution to the registering Sales user (Q-PAT-1). Reports show Walk-In collections with commission 0.  
**Validation / Rejection:** No pool table, route or payroll line exists.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `patient_contacts.patient_source`  
**Related API Capability:** None (no pool routes)

### FR-PAYROLL-005

**Title:** Inactive Sales gets no new commission  
**Actors:** System  
**Preconditions:** Original Sales Employee deactivated.  
**Trigger:** Collection after `deactivated_at`.  
**Main Flow:** No commission item for allocations with `allocated_at ≥ deactivated_at`.  
**Business Rules:** Historical Original Sales ownership is not transferred and nobody else receives the commission (Q-EMP-1; T4). The Manager report lists such collections as "not accrued — inactive Sales". Different from Doctors (FR-FIN-030).  
**Validation / Rejection:** G2 rejects an item after deactivation.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `staff.sales_employees.deactivated_at`  
**Related API Capability:** N/A

### FR-PAYROLL-006

**Title:** Reversal / refund commission at the original rate  
**Actors:** System  
**Preconditions:** A negative allocation reverses a commissioned allocation.  
**Trigger:** FR-FIN-012..015.  
**Main Flow:** A negative item is produced at the rate of the allocation it reverses, in the month the reversal occurs.  
**Business Rules:** A 5 % collection reversed after a change to 7 % reverses exactly the 5 % commission (−50). Refunds / reversals reduce the month in which they occur; finalized payrolls are not changed.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** Insert-only.  
**Permissions / Privacy:** N/A  
**Related Data:** Commission items  
**Related API Capability:** N/A

### FR-PAYROLL-007

**Title:** Unapplied Patient Credit is not commission base  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Payment held as credit.  
**Main Flow:** N/A  
**Business Rules:** Only allocations to charges count; credit becomes base only when applied (T1).  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

---

## 21. Expenses

### FR-EXP-001

**Title:** Expense categories  
**Actors:** Manager  
**Preconditions:** `ExpenseCategories.Manage`.  
**Trigger:** Manager manages categories.  
**Main Flow:** Create / edit / activate / deactivate categories with the `salesCanRecord` flag.  
**Business Rules:** The V1 expense category catalogue is exactly these **11 categories** (BASE §4 count; names per Owner decision recorded in this FRS): (1) Water; (2) Electricity; (3) Rent; (4) Internet; (5) Maintenance; (6) Cleaning; (7) Office Supplies; (8) Materials / Medical Supplies; (9) Marketing; (10) Transportation; (11) Petty / Miscellaneous. Only **Materials / Medical Supplies** and **Petty / Miscellaneous** have `sales_can_record = true`; Sales / Reception may create operational expenses only in those two (FR-EXP-002). All other categories are Manager-only for expense creation. Category management is Manager-only. Materials / Medical Supplies is an **expense category, not Inventory** (no stock, warehouse, reorder level or valuation).  
**Validation / Rejection:** Duplicate name rejected; Sales → 403.  
**Result:** Category list.  
**Audit / History:** A.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `expenses.expense_categories`  
**Related API Capability:** `GET/POST/PUT /api/manager/expense-categories`

### FR-EXP-002

**Title:** Sales records Materials / Petty expenses (create only)  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Expenses.CreateOperational`; category active with `sales_can_record`.  
**Trigger:** Daily materials / petty spending.  
**Main Flow:** Sales selects one allowed Sales-recordable category (**Materials / Medical Supplies** or **Petty / Miscellaneous**, from FR-EXP-003) and enters **Amount** and **Specification / Note**. Request body: `{ categoryId, amount, specification }`. The system records `created_by_user_id`, `created_by_role = SALES_RECEPTION` and `created_at` automatically. Example: Category Materials / Medical Supplies; Amount 850 EGP; Specification "Purchase of gloves and masks for the clinic".  
**Business Rules:** **OWNER DECISION (latest; overrides the older broader Sales expense-entry field list):** a Sales-created operational expense consists of category, amount and a **required** free-text specification explaining what the money was spent on; Sales does not enter the other expense fields. Sales can only create; no list, read, edit, delete, totals, category management or Manager financial analytics / reports (Q-EXP-3). The Manager keeps full expense management (FR-EXP-004, FR-EXP-005). Materials / Medical Supplies remains an expense category, not Inventory: no stock, quantity or item behavior. Response echoes only the created record.  
**Validation / Rejection:** Category not open to Sales → 422 / 403; amount ≤ 0 or more than two decimals → rejected; specification missing or empty → 400; unknown request members → 400.  
**Result:** `ExpenseCreatedDto` (id, category, amount, specification, created at).  
**Audit / History:** A+ (actor, time; audit names the Sales user).  
**Permissions / Privacy:** Expense-create access gives Sales no Manager report access.  
**Related Data:** `expenses.clinic_expenses` (category, amount, specification text, `created_by_user_id`, `created_by_role`, `created_at`)  
**Related API Capability:** `POST /api/sales/expenses` — request `{ categoryId, amount, specification }` (all required); response `ExpenseCreatedDto`

### FR-EXP-003

**Title:** Sales allowed-categories lookup  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Expenses.Categories.ReadAllowed`.  
**Trigger:** Sales opens the expense form.  
**Main Flow:** Returns `[{ id, name }]` of active categories with `sales_can_record = true`.  
**Business Rules:** The only expense-category route Sales has; no flag, totals or expenses returned.  
**Validation / Rejection:** Doctor → 403.  
**Result:** Lookup list.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `expenses.expense_categories`  
**Related API Capability:** `GET /api/sales/expenses/allowed-categories`

### FR-EXP-004

**Title:** Manager expense management  
**Actors:** Manager  
**Preconditions:** `Expenses.ReadAll`, `Expenses.Manage`.  
**Trigger:** Manager records or reviews expenses.  
**Main Flow:** Create in any category; list / read all expenses with creator and creator role.  
**Business Rules:** Entries show who created them and in which role.  
**Validation / Rejection:** Amount ≤ 0 rejected.  
**Result:** Expense records.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `expenses.clinic_expenses`  
**Related API Capability:** `GET/POST /api/manager/expenses`

### FR-EXP-005

**Title:** Manager edits an expense directly, audited  
**Actors:** Manager  
**Preconditions:** `Expenses.Manage`; `If-Match`.  
**Trigger:** Correction of an expense.  
**Main Flow:** Fields updated in place; `updated_by_user_id` / `updated_at` set.  
**Business Rules:** `created_by_user_id`, `created_by_role` and `created_at` are never overwritten. Every edit audited with before / after of changed fields.  
**Validation / Rejection:** Stale ETag → 412; Sales → 403.  
**Result:** Updated expense.  
**Audit / History:** A+ (who, when, before / after).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `expenses.clinic_expenses`  
**Related API Capability:** `PUT /api/manager/expenses/{id}`

### FR-EXP-006

**Title:** No Expense Finalized state, no attachments, no deletion  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** N/A  
**Business Rules:** No `finalized_at`, Finalized state, finalize route or adjustment rows (Q-EXP-2); no attachment table, file column or route — no images, receipts, invoices or PDFs (Q-EXP-1); no physical delete.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** None (API A9)

### FR-EXP-007

**Title:** Expenses reduce only the Clinic result  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Reporting.  
**Main Flow:** Expenses are subtracted in the Estimated Clinic Operating Result (FR-REPORT-003).  
**Business Rules:** Clinic operating expenses never reduce a Doctor's share.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

---

## 22. Attendance / Assistant Overtime

### FR-STAFF-012

**Title:** Manager records attendance for Assistants and Sales / Reception  
**Actors:** Manager  
**Preconditions:** `Staff.Attendance.Manage`.  
**Trigger:** Daily attendance entry.  
**Main Flow:** Record with exactly one of Assistant / Sales employee, clinic-local date, status (Present / Late / Absent [T9]), optional check-in / check-out times, notes, recorded by.  
**Business Rules:** One record per person per day (Q-ATT-1). Maintained only by the Manager: no self-check-in, no clock-in route, no Assistant or Sales write route, no biometric / GPS source. Doctors have no attendance.  
**Validation / Rejection:** Duplicate day → 409 `attendance.duplicate_day`; both / neither person → rejected; Absent with times → rejected; check-out ≤ check-in → rejected; Doctor / Sales → 403.  
**Result:** Attendance record.  
**Audit / History:** A+.  
**Permissions / Privacy:** Attendance data appears in no Doctor or Sales response.  
**Related Data:** `staff.attendance_records`  
**Related API Capability:** `GET/POST /api/manager/staff/attendance`

### FR-STAFF-013

**Title:** Edit attendance  
**Actors:** Manager  
**Preconditions:** `Staff.Attendance.Manage`; `If-Match`.  
**Trigger:** Correction.  
**Main Flow:** Status, times, notes updated.  
**Business Rules:** Same constraints as FR-STAFF-012.  
**Validation / Rejection:** As FR-STAFF-012.  
**Result:** Updated record.  
**Audit / History:** A+ (before / after).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.attendance_records`  
**Related API Capability:** `PUT /api/manager/staff/attendance/{id}`

### FR-STAFF-014

**Title:** Attendance alone changes no pay  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Payroll / commission computation.  
**Main Flow:** N/A  
**Business Rules:** Attendance never deducts pay, never changes commission and never creates overtime; no lateness penalty, absence deduction or leave policy exists. No FK from payroll to attendance.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-STAFF-015

**Title:** Record approved Assistant overtime  
**Actors:** Manager  
**Preconditions:** `Staff.AssistantOvertime.Manage`; the Assistant's overtime hourly rate is set.  
**Trigger:** Manager approves overtime hours for a date.  
**Main Flow:** Entry with work date, approved hours, optional linked attendance record (same Assistant, same date), notes; the server copies the current rate as `hourly_rate_snapshot` and computes `amount = round_half_up(hours × snapshot, 2)`.  
**Business Rules:** Overtime is explicitly approved hours, never inferred from check-in / check-out. One live entry per Assistant per day. A later rate change never recalculates an existing entry. Example: 5 h × 100 = 500.  
**Validation / Rejection:** Hours ≤ 0 or > 24 rejected; no rate set → 422; voided / missing Assistant → 422; second live entry same day → 409; attendance link of another Assistant / date → rejected.  
**Result:** Overtime entry.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.assistant_overtime_entries`  
**Related API Capability:** `GET /api/manager/staff/assistants/{id}/overtime?month`, `POST /api/manager/staff/assistants/{id}/overtime`

### FR-STAFF-016

**Title:** Void an overtime entry (void-only lifecycle)  
**Actors:** Manager  
**Preconditions:** `Staff.AssistantOvertime.Manage`; reason.  
**Trigger:** Entry recorded wrongly.  
**Main Flow:** `voided_at`, `voided_by_user_id`, `void_reason` set once; correction = void and re-enter.  
**Business Rules:** Entries are never edited or deleted. An entry referenced by a payroll item cannot be voided.  
**Validation / Rejection:** Referenced entry → rejected (G2).  
**Result:** Voided entry.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `staff.assistant_overtime_entries`  
**Related API Capability:** `POST /api/manager/staff/assistants/overtime/{entryId}/void`

### FR-STAFF-017

**Title:** Sales has no overtime  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** N/A  
**Business Rules:** No overtime table, column or route accepts a Sales employee; Sales has attendance only.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

---

## 23. Payroll

### FR-PAYROLL-008

**Title:** Monthly payroll lifecycle  
**Actors:** Manager  
**Preconditions:** `Payroll.Doctor.ManageAll` / `Payroll.Sales.ManageAll` / `Payroll.Assistant.ManageAll`.  
**Trigger:** Monthly payroll processing.  
**Main Flow:** Draft → Finalized → Paid per person per calendar month (clinic-local first-to-last day).  
**Business Rules:** Monthly for Doctor, Sales and Assistant (Q-PAY-2). Finalization only after the month has ended. Finalized payroll is sealed (G2): sealed money columns frozen; items / adjustments cannot be added; status, finalized-at and paid-at transitions remain possible. Historical payroll is never recalculated.  
**Validation / Rejection:** Finalize before month end → 422; second payroll for the same person-month → 409 (e.g. `payroll.assistant_period_exists`); non-calendar-month period rejected.  
**Result:** Sealed payroll.  
**Audit / History:** A+ on create, finalize, mark paid.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.*`  
**Related API Capability:** `/api/manager/payroll/{doctors|sales|assistants}` (`drafts`, `finalize`, `mark-paid`)

### FR-PAYROLL-009

**Title:** Doctor payroll  
**Actors:** Manager  
**Preconditions:** `Payroll.Doctor.ManageAll`.  
**Trigger:** Manager creates a Doctor draft for a month.  
**Main Flow:** Items = the Doctor's `doctor_earnings` whose `recognized_at` falls in the month and belong to no payroll; `doctor_share_total` = Σ earnings; informational revenue, lab and clinic totals; `final_amount = doctor_share_total + adjustments_total`.  
**Business Rules:** An earning is paid at most once. Pending fixed-lab entitlement is never included. Drafts may be created for inactive Doctors with unpaid earnings. A negative total is shown as is; no automatic carry-forward. Unaffected by reassignment or deactivation. Doctor payroll is never subtracted again in the Clinic result.  
**Validation / Rejection:** Earning of another Doctor / month → rejected (G2).  
**Result:** Doctor payroll.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.doctor_payrolls`, `doctor_payroll_items`  
**Related API Capability:** `GET /api/manager/payroll/doctors`, `POST …/drafts`, `POST …/{id}/finalize`, `POST …/{id}/mark-paid`

### FR-PAYROLL-010

**Title:** Doctor payroll adjustments  
**Actors:** Manager  
**Preconditions:** Draft Doctor payroll.  
**Trigger:** Manager adds an adjustment.  
**Main Flow:** Insert adjustment (amount, reason).  
**Business Rules:** Blocked after finalization (G2).  
**Validation / Rejection:** Finalized → rejected.  
**Result:** `adjustments_total` updated.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.doctor_payroll_adjustments`  
**Related API Capability:** `POST /api/manager/payroll/doctors/{id}/adjustments`

### FR-PAYROLL-011

**Title:** Doctor reads own payroll  
**Actors:** Doctor  
**Preconditions:** `Payroll.Doctor.ReadOwn`.  
**Trigger:** Doctor opens payroll.  
**Main Flow:** Own payroll rows only.  
**Business Rules:** Identity from principal; Manager → 404 on own route.  
**Validation / Rejection:** N/A  
**Result:** Own payroll.  
**Audit / History:** N/A  
**Permissions / Privacy:** C-Compensation own only.  
**Related Data:** `payroll.doctor_payrolls`  
**Related API Capability:** `GET /api/doctor/me/payroll`

### FR-PAYROLL-012

**Title:** Sales payroll  
**Actors:** Manager  
**Preconditions:** `Payroll.Sales.ManageAll`.  
**Trigger:** Manager creates a Sales draft for a month.  
**Main Flow:** Base salary snapshot + commission items (FR-PAYROLL-001) + Bonus − Deductions = net salary.  
**Business Rules:** A payroll exists for each Sales employee active at any time in the month. Base salary of a partially worked month is not prorated by the system; the Manager uses Bonus / Deduction [T8]. Attendance has no effect. Walk-In / Direct contributes 0. `commission_total` = Σ items, verified at finalization.  
**Validation / Rejection:** As FR-PAYROLL-008.  
**Result:** Sales payroll.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.sales_payrolls`, `sales_payroll_commission_items`, `sales_payroll_adjustments`  
**Related API Capability:** `/api/manager/payroll/sales` (drafts, adjustments, finalize, mark-paid)

### FR-PAYROLL-013

**Title:** Sales reads own payroll only  
**Actors:** Sales / Reception  
**Preconditions:** `Payroll.Sales.ReadOwn`.  
**Trigger:** Sales opens payroll.  
**Main Flow:** Own payroll rows and items.  
**Business Rules:** Never another employee's compensation; no access to the commission rate history.  
**Validation / Rejection:** Another employee's payroll → 404.  
**Result:** Own payroll.  
**Audit / History:** N/A  
**Permissions / Privacy:** C-Compensation own only.  
**Related Data:** `payroll.sales_payrolls`  
**Related API Capability:** `GET /api/sales/me/payroll`

### FR-PAYROLL-014

**Title:** Assistant payroll draft  
**Actors:** Manager  
**Preconditions:** `Payroll.Assistant.ManageAll`.  
**Trigger:** Manager creates an Assistant draft for a month.  
**Main Flow:** Salary snapshot copied from the current salary; Base Pay defaults to the salary snapshot (`SalarySnapshot`); the month's unvoided, not-yet-paid overtime entries are attached as overtime items (hours, rate snapshot, amount copied).  
**Business Rules:** An overtime entry belongs to at most one payroll, only of its own Assistant and month. Salary changes after the draft never alter the snapshot.  
**Validation / Rejection:** Second payroll for Assistant-month → 409 `payroll.assistant_period_exists`.  
**Result:** Draft with Base Pay, Overtime, Final shown separately.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.assistant_payrolls`, `assistant_payroll_overtime_items`  
**Related API Capability:** `GET /api/manager/payroll/assistants`, `GET …/{id}`, `POST /api/manager/payroll/assistants/drafts`

### FR-PAYROLL-015

**Title:** Assistant partial-month manual Base Pay  
**Actors:** Manager  
**Preconditions:** `Payroll.Assistant.ManageAll`; Draft payroll.  
**Trigger:** Mid-month join, mid-month leave, mid-month salary change or another partial-month case.  
**Main Flow:** Manager enters Base Pay with mandatory reason `JoinedMidMonth` / `LeftMidMonth` / `SalaryChangedInMonth` / `OtherPartialMonth`; `base_pay_source = ManagerManual`.  
**Business Rules:** **No automatic proration**; no division by 30, calendar days or working days (Q-PAY-ASST-2). Overtime is not editable here. Example: Base Pay 3,500 + overtime 400 = 3,900.  
**Validation / Rejection:** Missing reason → rejected; not Draft → rejected.  
**Result:** Manual Base Pay.  
**Audit / History:** A+ (old, new, reason code).  
**Permissions / Privacy:** Manager only.  
**Related Data:** `payroll.assistant_payrolls`  
**Related API Capability:** `PUT /api/manager/payroll/assistants/{id}/base-pay`

### FR-PAYROLL-016

**Title:** Assistant Final = Base Pay + Overtime; finalize and pay  
**Actors:** Manager  
**Preconditions:** Draft Assistant payroll; month ended.  
**Trigger:** Finalize / mark paid.  
**Main Flow:** At finalization verify `overtime_amount = Σ items` and `final_amount = base_pay_amount + overtime_amount`; then mark paid.  
**Business Rules:** Components stored and reported separately. No bonus, deduction, commission or incentive for Assistants. No `final-amount` entry route. Example: salary 6,000, 5 h × 100 → 6,500. Finalized components immutable.  
**Validation / Rejection:** As FR-PAYROLL-008.  
**Result:** Finalized / Paid Assistant payroll.  
**Audit / History:** A+.  
**Permissions / Privacy:** Manager only; Assistant payroll never visible to Doctor or Sales.  
**Related Data:** `payroll.assistant_payrolls`  
**Related API Capability:** `POST /api/manager/payroll/assistants/{id}/finalize`, `POST …/{id}/mark-paid`

---

## 24. Feedback

### FR-FDB-001

**Title:** Feedback request created at Visit finalization  
**Actors:** System  
**Preconditions:** FR-CLN-012.  
**Trigger:** Visit finalized ("Visit completed").  
**Main Flow:** One feedback request per Visit (patient, Visit, Doctor) created in the finalize transaction; the Feedback Request Pending notification is enqueued in the same transaction.  
**Business Rules:** No stored status, result columns or `expires_at`.  
**Validation / Rejection:** Second request for a Visit → rejected (unique).  
**Result:** Pending request.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `feedback.feedback_requests`  
**Related API Capability:** `POST /api/doctor/visits/{id}/finalize`

### FR-FDB-002

**Title:** Sales feedback queue (derived state)  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Feedback.Queue.Manage`.  
**Trigger:** Sales opens the queue.  
**Main Flow:** Requests listed with derived state: **Submitted** = an entry of either source exists; else **Sent** = link sent; else **Pending** [T14].  
**Business Rules:** No rating or comment is returned.  
**Validation / Rejection:** N/A  
**Result:** Queue.  
**Audit / History:** N/A  
**Permissions / Privacy:** Sales never sees results.  
**Related Data:** `feedback.feedback_requests`, `feedback.feedback_entries`  
**Related API Capability:** `GET /api/sales/feedback-requests?status=`

### FR-FDB-003

**Title:** Prepare the unique feedback link  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Feedback.Queue.Manage`; request not Submitted.  
**Trigger:** Sales prepares the link.  
**Main Flow:** A fresh 256-bit random token is issued; only its hash is stored; the website URL is returned once.  
**Business Rules:** Re-preparing replaces the hash (the earlier link stops working) [T]. The token contains no patient or Visit identifier and is not derivable from them.  
**Validation / Rejection:** Submitted request → 409 `feedback.already_submitted`.  
**Result:** Link URL.  
**Audit / History:** Tokens are never logged or audited.  
**Permissions / Privacy:** N/A  
**Related Data:** `feedback_requests.link_token_hash`  
**Related API Capability:** `POST /api/sales/feedback-requests/{id}/link`

### FR-FDB-004

**Title:** Mark the link as sent  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Feedback.Queue.Manage`; request not Submitted.  
**Trigger:** Sales sends the link manually (V1: no automated channel).  
**Main Flow:** Record channel, sender and date → state Sent.  
**Business Rules:** External delivery channels are future work.  
**Validation / Rejection:** Submitted request → 409.  
**Result:** Sent.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `link_sent_at`, `sent_by_user_id`, `delivery_channel`  
**Related API Capability:** `POST /api/sales/feedback-requests/{id}/mark-sent`

### FR-FDB-005

**Title:** Patient opens the feedback form on the clinic website  
**Actors:** Patient (anonymous)  
**Preconditions:** Valid token.  
**Trigger:** Patient opens the link.  
**Main Flow:** Returns only the Arabic form definition and the Doctor's display name.  
**Business Rules:** No login; no patient identifier returned; rate-limited per IP and per token hash; CORS only for the clinic website origin. Already-Submitted request → neutral `feedback.already_submitted` state, nothing from the entry.  
**Validation / Rejection:** Unknown / malformed token → generic response (indistinguishable).  
**Result:** Form.  
**Audit / History:** N/A  
**Permissions / Privacy:** Anonymous endpoint.  
**Related Data:** `feedback_requests`  
**Related API Capability:** `GET /api/public/feedback/{token}`

### FR-FDB-006

**Title:** Patient submits website feedback  
**Actors:** Patient (anonymous)  
**Preconditions:** Valid token; request not Submitted.  
**Trigger:** Patient submits `{ rating, comment }`.  
**Main Flow:** Store a `PatientWeb` entry linked to the request; request becomes Submitted (derived); neutral acknowledgement.  
**Business Rules:** No login and no patient typing of identifying data (Q-FDB-2). Rating 1..5; comment stored as plain text.  
**Validation / Rejection:** Second submission or after a Sales entry → `feedback.already_submitted` (neutral).  
**Result:** Submitted.  
**Audit / History:** N/A  
**Permissions / Privacy:** Anonymous; CSRF-exempt token binding.  
**Related Data:** `feedback.feedback_entries`  
**Related API Capability:** `POST /api/public/feedback/{token}`

### FR-FDB-007

**Title:** Feedback links never expire  
**Actors:** N/A  
**Preconditions:** N/A  
**Trigger:** N/A (negative requirement)  
**Main Flow:** A valid token works at any later date.  
**Business Rules:** No Expired status, `expires_at`, timeout or J6 expiry job (Q-FDB-1).  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-FDB-008

**Title:** Sales-entered feedback about a Visit  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Feedback.Entries.Record`; Visit Finalized and belongs to the given Patient and Doctor.  
**Trigger:** Patient tells Sales their feedback about a Doctor.  
**Main Flow:** `{ patientId, doctorId, visitId, rating (1–5), comment }` stored as `SalesEntered` with `recorded_by_user_id`; the entry links the Visit's existing request, which thereby becomes Submitted; the public token then behaves as already submitted.  
**Business Rules:** Feedback is never a clinical note and is stored in no clinical table. Creates no notification.  
**Validation / Rejection:** Request already has an accepted entry → 409 `feedback.already_submitted`; Visit mismatch → rejected; rating outside 1..5 → rejected.  
**Result:** `FeedbackEntryCreatedDto` (id, recorded at).  
**Audit / History:** A+ (actor, time).  
**Permissions / Privacy:** Sales cannot read results.  
**Related Data:** `feedback.feedback_entries`  
**Related API Capability:** `POST /api/sales/feedback-entries`

### FR-FDB-009

**Title:** Sales-entered general feedback (no Visit)  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Feedback.Entries.Record`.  
**Trigger:** Feedback about a Doctor not tied to a Visit.  
**Main Flow:** `SalesEntered` entry with Visit and request both NULL.  
**Business Rules:** `(visit_id IS NULL) = (feedback_request_id IS NULL)`.  
**Validation / Rejection:** As FR-FDB-008.  
**Result:** General feedback entry.  
**Audit / History:** A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `feedback.feedback_entries`  
**Related API Capability:** `POST /api/sales/feedback-entries` (without `visitId`)

### FR-FDB-010

**Title:** At most one accepted result per request  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Concurrent / sequential submissions from both sources.  
**Main Flow:** Unique `feedback_request_id` decides; the loser gets `feedback.already_submitted`.  
**Business Rules:** Applies to both paths in either order and under a race.  
**Validation / Rejection:** 409 / neutral already-submitted.  
**Result:** Exactly one entry.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Unique index  
**Related API Capability:** API A11 #50

### FR-FDB-011

**Title:** Only the Manager reads feedback results  
**Actors:** Manager  
**Preconditions:** `Feedback.Results.Read`.  
**Trigger:** Manager reviews feedback.  
**Main Flow:** Ratings and comments of both sources with `source` shown; filters Doctor, date range, source.  
**Business Rules:** Doctors and Sales never receive results.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Feedback results.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** `feedback.feedback_entries`  
**Related API Capability:** `GET /api/manager/feedback?doctorId&from&to&source`

---

## 25. Notifications

### FR-NOTIF-001

**Title:** Seven notification types  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Planner jobs / source events.  
**Main Flow:** Types: Appointment Tomorrow; Implant Follow-Up Tomorrow; Orthodontic Installment Due Tomorrow; New Doctor Referral; Outstanding Payment Follow-Up; Pending Follow-Up; Feedback Request Pending.  
**Business Rules:** Record fields: type, recipient user and role, related patient, entity type and id, scheduled / sent / read time, status, retry count, dedupe key. One row per recipient. In-app channel only in V1; WhatsApp / SMS / email are future work behind an abstraction.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** No patient name or phone stored in the row.  
**Related Data:** `notifications.notifications`  
**Related API Capability:** N/A

### FR-NOTIF-002

**Title:** Recipients  
**Actors:** System  
**Preconditions:** Active recipients.  
**Trigger:** Enqueue.  
**Main Flow:** Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral, Feedback Request Pending → all active Sales / Reception users. Pending Follow-Up, Outstanding Payment Follow-Up → the follow-up's assigned Sales employee.  
**Business Rules:** No notification targets a Doctor or an Assistant. Inactive users receive nothing; their Pending rows are Cancelled. Unassigned follow-ups notify nobody. No confirmed type targets the Manager. Attendance, overtime and Sales-entered feedback create no notification.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Recipients resolved only through `v_active_user_roles` / `v_sales_employee_activity`.  
**Related Data:** Views  
**Related API Capability:** N/A

### FR-NOTIF-003

**Title:** J1 appointment and implant reminders  
**Actors:** System (Worker)  
**Preconditions:** N/A  
**Trigger:** Every 15 minutes [DEFAULT].  
**Main Flow:** Appointments on tomorrow's clinic-local date, status not Cancelled / Rescheduled: `Standard` → Appointment Tomorrow; `ImplantFollowUp` → Implant Follow-Up Tomorrow.  
**Business Rules:** Calendar-date semantics: an item created or rescheduled today for tomorrow is reminded on the next run; one created on its own date gets none. A rescheduled appointment has a new id → new reminder; the old pending one is Cancelled.  
**Validation / Rejection:** N/A  
**Result:** Reminder rows.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `scheduling.appointments`  
**Related API Capability:** Hangfire J1

### FR-NOTIF-004

**Title:** J2 orthodontic installment reminders  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Every 15 minutes [DEFAULT].  
**Main Flow:** Active installments whose current due date is tomorrow and whose ortho charge has Outstanding > 0.  
**Business Rules:** Retired installments are never reminded; a fully paid ortho charge gets none; the notification shows the charge-level balance. Reminder one day before due.  
**Validation / Rejection:** N/A  
**Result:** Reminder rows.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** Orthodontics, Finance (read)  
**Related API Capability:** Hangfire J2

### FR-NOTIF-005

**Title:** J3 follow-up due notifications  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Every 15 minutes [DEFAULT].  
**Main Flow:** Open follow-ups (`status <> 'FollowedUp'`) with due date ≤ today: `RemainingBalance` → Outstanding Payment Follow-Up; others → Pending Follow-Up; to the assigned employee.  
**Business Rules:** NULL-responsible follow-ups skipped (counted by J7). A due-date change notifies again.  
**Validation / Rejection:** N/A  
**Result:** Reminder rows.  
**Audit / History:** N/A  
**Permissions / Privacy:** Worker reads follow-ups excluding notes.  
**Related Data:** `crm.follow_ups`  
**Related API Capability:** Hangfire J3

### FR-NOTIF-006

**Title:** Event-driven notifications  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Referral created (FR-REF-001); feedback request created (FR-FDB-001).  
**Main Flow:** New Doctor Referral / Feedback Request Pending enqueued in the same transaction as the source; dispatched within a minute.  
**Business Rules:** A rolled-back source leaves no notification.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `notifications.notifications`  
**Related API Capability:** N/A

### FR-NOTIF-007

**Title:** Notification inbox  
**Actors:** Sales / Reception, Manager  
**Preconditions:** `Notifications.Read.Own`.  
**Trigger:** User opens the inbox / polls the unread count (every 60 s).  
**Main Flow:** Paged Sent rows of the principal; unread count; mark one / all read.  
**Business Rules:** Rendered at display time by the source module under the viewer's permissions (Sales whitelist only). **Doctors have no inbox** (403).  
**Validation / Rejection:** Another user's id → 404; Doctor → 403.  
**Result:** Inbox.  
**Audit / History:** N/A  
**Permissions / Privacy:** No contact or clinical canary in rendered DTOs.  
**Related Data:** `notifications.notifications`  
**Related API Capability:** `GET /api/me/notifications`, `GET …/unread-count`, `PATCH …/{id}/read`, `POST …/read-all`

### FR-NOTIF-008

**Title:** Idempotent, timezone-aware, retry-safe processing  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Planner and dispatcher runs.  
**Main Flow:** Dedupe key `{type}:{entity_id}:{clinic-local target date}:{recipient}` with `ON CONFLICT DO NOTHING`; J4 dispatcher every minute processes each notification in its own transaction (`SKIP LOCKED`), re-validates source and recipient, delivers in-app; a failure commits `Failed` with incremented retry count in a new transaction; J5 retries `Failed` rows while retry count < 5 [DEFAULT].  
**Business Rules:** Lifecycle Pending → Sent / Cancelled; Failed → Pending via retry. One poison notification never blocks others. Job arguments are ids only. A missed whole reminder day invents no late reminder.  
**Validation / Rejection:** N/A  
**Result:** No duplicates.  
**Audit / History:** N/A  
**Permissions / Privacy:** `last_error` scrubbed, ≤ 500 chars, no names / contact data.  
**Related Data:** `notifications.notifications`  
**Related API Capability:** Hangfire J4, J5

### FR-NOTIF-009

**Title:** J7 operations health  
**Actors:** System; Operator  
**Preconditions:** N/A  
**Trigger:** Every 5 minutes.  
**Main Flow:** Reports job freshness, dispatcher lag, exhausted retries, failed jobs, count of open follow-ups owned by inactive Sales (should be 0) and count of unassigned open Leads / Follow-Ups.  
**Business Rules:** Counts only; never names, ids or text. The recurring job list is exactly J1–J5 and J7 (no J6).  
**Validation / Rejection:** N/A  
**Result:** Health ping / alert.  
**Audit / History:** N/A  
**Permissions / Privacy:** No personal data in alerts.  
**Related Data:** Views / probes  
**Related API Capability:** Hangfire J7

---

## 26. Reports

### FR-REPORT-001

**Title:** Manager live daily dashboard  
**Actors:** Manager  
**Preconditions:** `Reports.Dashboard.Manager`.  
**Trigger:** Manager opens the dashboard for a date (default today, clinic-local).  
**Main Flow:** Live figures computed on read; includes Unassigned Leads / Follow-Ups counters.  
**Business Rules:** **No Daily Closing**: no Close Day, no sealed daily state, no manual report generation job (Q-FIN-DC).  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Live dashboard.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only over modules  
**Related API Capability:** `GET /api/manager/dashboard?date=`

### FR-REPORT-002

**Title:** Financial report  
**Actors:** Manager  
**Preconditions:** `Reports.Financial`.  
**Trigger:** Daily, Weekly, Monthly or Custom `from` / `to` range.  
**Main Flow:** Layout: REVENUE (Billed, Discounts, Collected, Consultation Revenue, General Lab, Fixed Lab funding, Clinic Treatment Revenue Share, Clinic Gross Revenue); COMPENSATION / EXPENSES (Sales Salaries, Sales Commissions with Walk-In shown at 0, Assistant Base Salaries, Assistant Overtime, Assistant Payroll Expense, Clinic Operating Expenses with Materials / Petty as separate lines); RESULT; MEMO (FR-REPORT-004).  
**Business Rules:** Reporting reads Finance, Payroll and Expenses rows; owns no business table; nothing depends on it. Only Finalized and Paid payrolls count; Drafts listed as not yet finalized.  
**Validation / Rejection:** N/A  
**Result:** Report.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** `GET /api/manager/reports/financial`

### FR-REPORT-003

**Title:** Estimated Clinic Operating Result  
**Actors:** Manager  
**Preconditions:** FR-REPORT-002.  
**Trigger:** Report computation.  
**Main Flow:** Clinic Treatment Revenue Share = Σ clinic share; Clinic Gross Revenue = that + Consultation fees collected; Result = Clinic Gross Revenue − Sales compensation (salary + commission + bonus − deductions) − Assistant Payroll Expense (Base Pay + Overtime) − Clinic Operating Expenses (incl. Materials / Petty).  
**Business Rules:** An operational metric, not accounting profit. **Doctor payroll is never subtracted again** (already removed by the distribution). Expenses never reduce a Doctor share.  
**Validation / Rejection:** N/A  
**Result:** Result line.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** `GET /api/manager/reports/financial`, `GET /api/manager/finance/summary`

### FR-REPORT-004

**Title:** Memo lines  
**Actors:** Manager  
**Preconditions:** FR-REPORT-002.  
**Trigger:** Report computation.  
**Main Flow:** Doctor recognized share, Pending Fixed-Lab Doctor entitlement, Patient Credit, Cash received by method, Outstanding, refund / correction effects, "not accrued — inactive Sales", Lab total.  
**Business Rules:** Pending entitlement and Patient Credit are memo lines, never deductions and never revenue / Clinic profit.  
**Validation / Rejection:** N/A  
**Result:** Memo section.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** As FR-REPORT-002

### FR-REPORT-005

**Title:** Assistant and Sales payroll appear only in full months  
**Actors:** Manager  
**Preconditions:** FR-REPORT-002.  
**Trigger:** Report range.  
**Main Flow:** Monthly reports include the Assistant Payroll Expense with Base Pay and Overtime as separate lines; a Custom range includes an Assistant (and Sales) payroll month only when it fully contains that month.  
**Business Rules:** Daily / Weekly / partial Custom ranges allocate **no fraction**; no division by 30, calendar or working days; no PENDING / INCOMPLETE label (Q-PAY-ASST-1). Attendance / overtime activity may be reported separately, never as proration.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** As FR-REPORT-002

### FR-REPORT-006

**Title:** Finance summary and finance lists  
**Actors:** Manager  
**Preconditions:** `Finance.Management.Read`.  
**Trigger:** Manager reviews finance.  
**Main Flow:** Summary (Billed, Collected, Outstanding, cash received by method, discounts by setter role, consultation fees, Doctor recognized share, pending Doctor entitlement, Lab, Clinic Treatment Revenue Share, Clinic Gross Revenue, Patient Credit, refunds / corrections, estimated result); pending entitlements; charges; payments; credit.  
**Business Rules:** Derived figures only (FR-FIN-003).  
**Validation / Rejection:** N/A  
**Result:** Finance views.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** `GET /api/manager/finance/summary`, `/finance/pending-entitlements`, `/finance/charges`, `/finance/payments`, `/finance/credit`

### FR-REPORT-007

**Title:** Doctor-performance, Sales-performance, orthodontic-receivables, expenses and attendance reports  
**Actors:** Manager  
**Preconditions:** `Reports.Financial` / `Reports.Attendance`.  
**Trigger:** Manager runs a report.  
**Main Flow:** Doctor performance; Sales performance (commission on collected money after discount; Walk-In vs Lead-sourced collections; Walk-In at zero commission; "not accrued — inactive Sales"); orthodontic receivables; expenses; attendance (Assistants and Sales attendance and approved overtime, by month).  
**Business Rules:** No proration; Medical History values never appear in any report.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Reports.  
**Audit / History:** N/A  
**Permissions / Privacy:** Manager only.  
**Related Data:** Read-only  
**Related API Capability:** `GET /api/manager/reports/doctor-performance`, `/sales-performance`, `/orthodontic-receivables`, `/expenses`, `/attendance`

### FR-REPORT-008

**Title:** Report exports  
**Actors:** Manager  
**Preconditions:** `Reports.Export`.  
**Trigger:** Export request.  
**Main Flow:** CSV / PDF in Arabic built from an audience DTO with an explicit column whitelist; streamed, not stored; `Content-Disposition: attachment`.  
**Business Rules:** No Doctor or Sales data export exists. File names never contain patient names or phones. CSV cells starting with `=`, `+`, `-`, `@` neutralized.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** Download.  
**Audit / History:** Every export audited (user, type, filter, row count).  
**Permissions / Privacy:** No medical values; no contact data in Doctor-facing outputs.  
**Related Data:** N/A  
**Related API Capability:** `GET /api/manager/reports/…/export`

### FR-REPORT-009

**Title:** Sales dashboard  
**Actors:** Sales / Reception  
**Preconditions:** `Reports.Dashboard.Sales`.  
**Trigger:** Sales opens the dashboard.  
**Main Flow:** The eight V1 dashboard items: (1) Today's Appointments; (2) New Leads; (3) Pending Follow-Ups; (4) New Referrals; (5) Tomorrow's Appointments; (6) Tomorrow's Orthodontic Installments; (7) Outstanding Balances; (8) Feedback Requests to Send.  
**Business Rules:** Pending Follow-Ups use the open predicate (`status <> 'FollowedUp'`, FR-CRM-006). Patient arrival is shown only through the Appointment / Arrival flow: arrived-today patients appear inside Today's Appointments with their `arrivedAt` (FR-SCH-009, FR-SCH-014), never as an Arrived or Waiting status and never as a separate "Waiting Patients" widget. No attendance, overtime, Doctor finance or Manager report data. No Doctor dashboard exists.  
**Validation / Rejection:** Doctor → 403.  
**Result:** Sales dashboard.  
**Audit / History:** N/A  
**Permissions / Privacy:** Sales whitelist.  
**Related Data:** Read-only  
**Related API Capability:** `GET /api/sales/dashboard`

---

## 27. Privacy / Authorization

### FR-SEC-001

**Title:** Four-layer backend authorization on every request  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** Every request.  
**Main Flow:** (1) authentication → 401; (2) endpoint permission key → 403; (3) resource scope → 404; (4) audience DTO projection.  
**Business Rules:** Backend enforcement only; React guards are usability. Fallback policy denies any endpoint without metadata. Every mapped route has a key assigned to at least one role, or `[AuthenticatedOnly]`, or is one of the six anonymous endpoints. The Manager is not a wildcard; anything not confirmed is denied. No in-app role editor.  
**Validation / Rejection:** As listed.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** SEC §4 matrix is authoritative.  
**Related Data:** `identity.permissions`, `identity.role_permissions`  
**Related API Capability:** All routes

### FR-SEC-002

**Title:** Doctors never receive patient contact data  
**Actors:** Doctor  
**Preconditions:** N/A  
**Trigger:** Any Doctor response, OpenAPI document, search, PDF, CSV, notification, log, error body or audit view.  
**Main Flow:** N/A  
**Business Rules:** Never phone, WhatsApp, email, address, CRM / contact notes, appointment `sales_notes`, Original Sales Representative or patient source. Storage and contract separation; Doctor DTOs selected from clinical tables only; contact types tagged `[ContactData]`; no contact-bearing key mapped to DOCTOR.  
**Validation / Rejection:** Contact endpoints → 403 for Doctor.  
**Result:** Canary scans find no contact value.  
**Audit / History:** N/A  
**Permissions / Privacy:** Defense D1–D7.  
**Related Data:** `patients.patient_contacts`, `crm.*`  
**Related API Capability:** All `/api/doctor/*`

### FR-SEC-003

**Title:** Sales never receives Doctor-authored clinical detail or Doctor finance  
**Actors:** Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Any Sales response.  
**Main Flow:** N/A  
**Business Rules:** Never clinical notes, root canal measurements, detailed procedure notes, prescription medication details, the Doctor's referral notes, Doctor percentage, Doctor Share, entitlement, Clinic Share, lab / fixed-lab data, other employees' compensation or Assistant data. Sales code never reads clinical tables; only `ISalesClinicalSummary` and `ISalesPricingLines`.  
**Validation / Rejection:** 403 on those routes.  
**Result:** Canary scans find no clinical value.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** All `/api/sales/*`

### FR-SEC-004

**Title:** Sales whitelist and deliberate exceptions  
**Actors:** Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Sales views.  
**Main Flow:** N/A  
**Business Rules:** Sales may see: contact data; DOB; Original Sales Representative of Lead-sourced patients; procedure name, treating Doctor, treatment status; Referral Destination / Type, Referral Reason and follow-up instructions (Q-PRV-1); appointment and arrival; price, discount (and may set it before finalization), total, paid, remaining, Patient Credit; ortho installment information; the structured Medical History Intake (collect / read / edit).  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** C-Operational + C-MedicalIntake.  
**Related Data:** N/A  
**Related API Capability:** Sales DTOs (API A3)

### FR-SEC-005

**Title:** Resource scoping — identity from the principal  
**Actors:** Doctor, Sales / Reception  
**Preconditions:** N/A  
**Trigger:** "Own" requests.  
**Main Flow:** Own work, own payroll, own compensation, own notifications, own queue, My Leads / own Follow-Ups take the identity from the principal.  
**Business Rules:** No `doctorId` / `salesEmployeeId` parameter on own routes. Doctor writes only own editable Visit / work; Doctor reschedules only own created follow-ups. One role per account; no union.  
**Validation / Rejection:** A scoped resource that is not the caller's → 404.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** S1–S13.  
**Related Data:** N/A  
**Related API Capability:** Own routes

### FR-SEC-006

**Title:** Audience DTOs and strict request binding  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** Any request / response.  
**Main Flow:** Responses are projections into audience DTOs; requests bind to dedicated command records.  
**Business Rules:** Entities are never serialized or bound. Unknown request members rejected. One OpenAPI document per audience; the Doctor document has no contact property name; the Sales document has no clinical property name. The Manager receives the audience's DTO on shared routes.  
**Validation / Rejection:** Unknown member → 400.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** API A3

### FR-SEC-007

**Title:** No personal data in URLs, logs, alerts or job arguments  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Logging, searching, job scheduling, alerting.  
**Main Flow:** N/A  
**Business Rules:** Searches with names / phones are POST bodies; URLs hold only opaque ids and patient codes; no request / response bodies logged on patient, contact, finance or payroll routes; masking of Egyptian mobiles / `+20` / emails; Hangfire arguments ids only; notification payloads without patient name or phone; error tracking scrubbed.  
**Validation / Rejection:** N/A  
**Result:** Canary values absent from logs.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-SEC-008

**Title:** Sales-authored Doctor-visible text check  
**Actors:** Sales / Reception  
**Preconditions:** N/A  
**Trigger:** Sales writes an appointment `reason`.  
**Main Flow:** Phone / email patterns rejected with a message pointing to `sales_notes`.  
**Business Rules:** A heuristic; it catches obvious numbers and addresses, not every form of personal information.  
**Validation / Rejection:** Phone / email pattern → 400.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** Protects FR-SEC-002.  
**Related Data:** `scheduling.appointments.reason`  
**Related API Capability:** Appointment booking

### FR-SEC-009

**Title:** Exactly six anonymous endpoints  
**Actors:** Anonymous  
**Preconditions:** N/A  
**Trigger:** N/A  
**Main Flow:** `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /health/live`, `GET /health/ready`, `GET /api/public/feedback/{token}`, `POST /api/public/feedback/{token}`.  
**Business Rules:** No other route is anonymous. `GET /api/me`, logout and change-password are `[AuthenticatedOnly]`.  
**Validation / Rejection:** N/A  
**Result:** Startup test enforces the list.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** API A2

### FR-SEC-010

**Title:** Assistant, attendance and payroll data are Manager-only  
**Actors:** Manager  
**Preconditions:** N/A  
**Trigger:** Any Assistant / attendance / overtime / payroll route.  
**Main Flow:** N/A  
**Business Rules:** Doctor and Sales get 403 on every such route; no such data appears in any Doctor or Sales response; the Worker has no access to these tables.  
**Validation / Rejection:** 403.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `staff.assistants`, `staff.attendance_records`, `staff.assistant_overtime_entries`, `payroll.assistant_*`  
**Related API Capability:** `/api/manager/staff/*`, `/api/manager/payroll/*`

### FR-SEC-011

**Title:** Denied-by-default capabilities  
**Actors:** Doctor, Sales / Reception  
**Preconditions:** N/A  
**Trigger:** N/A  
**Main Flow:** N/A  
**Business Rules:** Not confirmed, hence denied: Doctor dashboard, Doctor notifications, Doctor feedback results, Doctor payment recording, any Sales view of Doctor Share / Clinic Share / lab / percentages, Sales expense lists / reports / edits, Sales category management, Sales feedback results, Sales prescription print, Manager arrival recording [T5].  
**Validation / Rejection:** 403.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** Generated role-matrix test

### FR-SEC-012

**Title:** Web security controls  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** Every request / render.  
**Main Flow:** N/A  
**Business Rules:** HTTPS; HSTS and security headers; CSP without inline scripts; CSRF token on unsafe methods; `Cache-Control: no-store` on authenticated responses; free text rendered as plain text (no HTML / Markdown); parameterized SQL only; per-endpoint sort / filter whitelists; pagination max 100; no browser storage of data or tokens; frontend cache cleared on logout / 401; rate limits [DEFAULT] general 300/min/user.  
**Validation / Rejection:** CSRF missing → rejected; 429 on limits.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** SEC §3, §12, §13

---

## 28. Audit / Historical Integrity

### FR-AUDIT-001

**Title:** Audit written atomically and insert-only  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any audited action.  
**Main Flow:** The audit row is written in the same transaction as the business change.  
**Business Rules:** If the audit write fails the business action fails (fail closed). `clinic_app` has INSERT and SELECT only on the audit log.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** `audit.audit_log` (H).  
**Permissions / Privacy:** N/A  
**Related Data:** `audit.audit_log`  
**Related API Capability:** N/A

### FR-AUDIT-002

**Title:** Audited events  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** N/A  
**Main Flow:** N/A  
**Business Rules:** At least: login success / failure; account creation, password set, role change, deactivation / reactivation, forced sign-out; Doctor patient-record opens, Sales contact views and Manager views; contact edits (values flagged restricted); Medical History create / update (field names only); patient reassignment; appointment / arrival / status changes; Visit finalize; controlled corrections; price entries; discount entries (amount, actor, role, time); charge adjustments; payments, refunds, reversals, reallocations, replacements; fixed-lab entries and revisions; Ortho terms and schedule revisions; installment applications; percentage, commission-rate, lab-rate, salary and overtime-rate changes; attendance create / edit; overtime record / void; payroll create / Base Pay / finalize / paid; expense create and edits (before / after); Sales-entered feedback; exports and prescription prints; timezone change; audit reads.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** A / A+.  
**Permissions / Privacy:** N/A  
**Related Data:** `audit.audit_log`  
**Related API Capability:** N/A

### FR-AUDIT-003

**Title:** Never-stored audit content  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Audit write.  
**Main Flow:** Interceptor deny-list.  
**Business Rules:** Never passwords, password hashes, security stamps, reset tokens, feedback tokens or Medical History values. A password change cannot be reconstructed from audit. The Data Protection key table is skipped.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** N/A  
**Related API Capability:** N/A

### FR-AUDIT-004

**Title:** Manager-only audit read  
**Actors:** Manager  
**Preconditions:** `Audit.Read`.  
**Trigger:** Manager browses the audit log.  
**Main Flow:** Cursor-paged list and detail.  
**Business Rules:** Reading the audit log is itself audited.  
**Validation / Rejection:** Doctor / Sales → 403.  
**Result:** `AuditEntryDto`.  
**Audit / History:** A.  
**Permissions / Privacy:** Manager only.  
**Related Data:** `audit.audit_log`  
**Related API Capability:** `GET /api/manager/audit`, `GET /api/manager/audit/{id}`

### FR-AUDIT-005

**Title:** No physical deletion of business history  
**Actors:** All  
**Preconditions:** N/A  
**Trigger:** N/A  
**Main Flow:** N/A  
**Business Rules:** No physical deletion of treatments, payments, payroll, prescriptions, expenses, lab expenses, Doctor assignments, appointments, arrivals, installments or employees. Soft markers only (`voided_at`, `removed_at`, `withdrawn_at`, `is_active`). `clinic_app` has no DELETE grant on business schemas.  
**Validation / Rejection:** No delete routes.  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** All business schemas  
**Related API Capability:** N/A

### FR-AUDIT-006

**Title:** Insert-only ledgers; corrections are new rows  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any correction.  
**Main Flow:** N/A  
**Business Rules:** Charges, adjustments, payments, allocations, credit entries, corrections, distribution events, entitlements, earnings, distributions, fixed-lab expenses / revisions, lab-percentage history, commission-rate history, ortho term / installment revisions, installment applications, discount entries, Visit corrections, feedback entries, responsibility history, contact attempts, payroll items / adjustments are insert-only (no UPDATE / DELETE grant). Expenses are the exception: edited in place **with** audit.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** R1  
**Related API Capability:** N/A

### FR-AUDIT-007

**Title:** Snapshots are never recalculated  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Any later configuration change.  
**Main Flow:** N/A  
**Business Rules:** Doctor percentage (work item → charge), lab % per distribution, commission rate per item, overtime rate per entry / item, Assistant salary snapshot, Sales base salary snapshot, discount snapshot on the charge and sealed payroll totals never change after creation.  
**Validation / Rejection:** N/A  
**Result:** N/A  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** DB §20 snapshots  
**Related API Capability:** N/A

### FR-AUDIT-008

**Title:** Historical relationships preserved  
**Actors:** System  
**Preconditions:** N/A  
**Trigger:** Reassignment, reschedule, deactivation, redistribution.  
**Main Flow:** N/A  
**Business Rules:** Reassignment never moves history (past work references the performing Doctor); appointment changes create a new appointment and mark the old Rescheduled; Doctor assignment history, responsibility history and Original Sales attribution are kept permanently.  
**Validation / Rejection:** N/A  
**Result:** Complete patient timeline.  
**Audit / History:** N/A  
**Permissions / Privacy:** N/A  
**Related Data:** `patient_doctor_assignments`, `appointments` chain, `responsibility_history`  
**Related API Capability:** N/A

---

## 29. Cross-Module End-to-End Flows

Each flow reads **Actor → Action → State / Data Change → Next Actor / Action**. The FR column links the governing requirements.

### FLOW-01 Lead → Patient

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Sales | Creates a Lead | `crm.leads` row; Original Sales = Responsible = principal's Sales employee | Sales works the lead in My Leads | FR-CRM-001, FR-CRM-002 |
| 2 | Sales | Creates Follow-Ups, logs contact attempts | `crm.follow_ups` (status جديد / تم التواصل / لم يرد); attempts appended | J3 notifies on due date | FR-CRM-005..007, FR-NOTIF-005 |
| 3 | Sales | Converts the Lead | Patient + contact profile, `patient_source = Lead`, Original Sales copied once; `converted_patient_id` set | Sales books an appointment (FLOW-03) | FR-CRM-004, FR-PAT-002 |
| 4 | System | Later collections from this patient | Commission items for the Original Sales Employee at the rate in force at `allocated_at` (while active) | Sales payroll (FLOW-10) | FR-PAYROLL-001..005 |

### FLOW-02 Walk-In → Patient

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Sales / Manager | Registers the patient (name, DOB or recorded age, contact) | Patient + contact profile, `patient_source = WalkIn`, Original Sales NULL; registering user only `created_by_user_id` | Sales collects the intake (optional) | FR-PAT-001 |
| 2 | Sales | Collects the Medical History Intake (when possible, before consultation) | `patient_medical_histories` (tri-state flags) | Booking does not wait for it | FR-MED-002, FR-MED-007 |
| 3 | Sales | Books an appointment | Appointment Scheduled | FLOW-03 | FR-SCH-003 |
| 4 | System | Collections from this patient | **No commission item; no pool** | Reports show Walk-In collections at 0 commission | FR-PAYROLL-004 |

### FLOW-03 Appointment → Arrival → Doctor Queue → Visit

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Manager | Publishes the Doctor's weekly availability | Published week | Sales sees slots | FR-SCH-001, FR-SCH-002 |
| 2 | Sales / Manager | Books with a manual duration (overlap allowed) | Appointment Scheduled | J1 reminds Sales the day before | FR-SCH-003, FR-SCH-004, FR-NOTIF-003 |
| 3 | Sales / Manager | Confirms (or Cancels / No Show / Reschedules) | Status Confirmed (or Cancelled / No Show; reschedule → new appointment, old Rescheduled) | — | FR-SCH-006, FR-SCH-007 |
| 4 | Sales | Records arrival on the appointment day | `appointment_arrivals` row; **status unchanged** | Patient appears in the Doctor queue | FR-SCH-009 |
| 5 | Doctor | Reads own queue; opens patient and intake | Read only (record access audited) | Doctor starts the Visit | FR-SCH-011, FR-MED-003, FR-PAT-007 |
| 6 | Doctor | Starts the linked Visit | Visit created (editable); appointment In Treatment; leaves queue | FLOW-04 | FR-CLN-001, FR-SCH-012 |

### FLOW-04 Visit → Priced Work → Finalize → Charge

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Doctor | Records treatment (new / continued case), steps, measurements, implant details, notes | Case, treatment record, clinical rows | Doctor prices own work | FR-CLN-002..006 |
| 2 | Doctor | Enters the price of own work; consultation fee if any | Own work item with `entered_price` + percentage snapshot; `consultation_fee` | Line visible in the Sales pricing view | FR-CLN-007..010 |
| 3 | Doctor | (Fixed-lab work) enters the initial Fixed Lab | Fixed Lab revision 1 | — | FR-LAB-006 |
| 4 | Doctor / Sales / Manager | Sets a fixed-EGP discount (optional) | `pricing_discount_entries` row (actor, role, time) | — | FR-FIN-021..023 |
| 5 | Doctor | Writes prescription, referral, next appointment with a manually entered duration (optional) | Prescription; referral (+ New Doctor Referral notification); follow-up appointment with the authoring Doctor (`durationMinutes` required) | — | FR-RX-001, FR-REF-001, FR-SCH-008 |
| 6 | Doctor | Finalizes the Visit | One transaction: Visit Finalized; Treatment charge per work item + Consultation charge with discount snapshot; ortho schedule if any; appointment Completed + `visit_ended_at` (unused time released); feedback request (+ notification) | Sales / Manager collect payment (FLOW-05) | FR-CLN-012, FR-FIN-001, FR-FIN-024, FR-SCH-013, FR-FDB-001 |
| 7 | Doctor | (later) corrects own finalized Visit / Prescription | `visit_corrections` + audit; Visit stays Finalized | — | FR-CLN-014, FR-RX-004 |

### FLOW-05 Payment → Allocation → Distribution

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Sales / Manager | Records a payment (method, amount, explicit allocations, Idempotency-Key) | `payments` Receipt; `payment_allocations` (≤ Outstanding); remainder → credit | — | FR-FIN-004..006 |
| 2 | System | Per allocation to a Treatment / Ortho charge: entitlement | `doctor_entitlements` = allocated × snapshot % | — | FR-FIN-018 |
| 3 | System | General lab: recognize at once | Doctor earning + lab (rate at `allocated_at`) + clinic remainder | Doctor payroll month = recognition month | FR-LAB-002, FR-FIN-028 |
| 4 | System | Fixed lab: replay Lab-First waterfall | Lab first → pending entitlement queue → clinic remainder (never negative) | FLOW-07 | FR-LAB-008 |
| 5 | System | Consultation allocation | No entitlement / distribution | — | FR-FIN-019 |
| 6 | System | Commit guards G8 / G8b | Conservation and identity verified | Sales commission (FLOW-10); ortho manual application (FLOW-08) | FR-FIN-009, FR-FIN-020 |

### FLOW-06 Patient Credit

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Sales / Manager | Records a payment larger than allocations, or before any charge | `FromPayment` credit entry; no revenue, entitlement or commission | Credit shown on balance and as liability memo | FR-FIN-006, FR-FIN-008 |
| 2 | Sales / Manager | Applies credit to a charge with Outstanding | FIFO credit slices → allocations (`Credit`) + `AppliedToCharge` entries | Distribution Engine runs; collection moment = application time | FR-FIN-007 |
| 3 | Manager | Refunds unapplied credit (optional) | Negative credit entry; no distribution effect | — | FR-FIN-012 |

### FLOW-07 Fixed Lab

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Doctor | Prices Implant / Fixed / Removable work and enters the initial Fixed Lab | Work item; `fixed_lab_expenses` + revision 1 (role DOCTOR) | Doctor finalizes | FR-CLN-007, FR-LAB-005, FR-LAB-006 |
| 2 | Doctor | Finalizes | Refused with `fixed_lab.initial_required` if revision 1 is missing; else Fixed-lab Treatment charge | Collections | FR-LAB-007 |
| 3 | Sales / Manager | Records collections | Waterfall: Lab covered first; entitlement pending until funded; Clinic remainder ≥ 0 | Later collections settle pending FIFO | FR-LAB-008, FR-LAB-009 |
| 4 | Manager | Revises the Fixed Lab (reason) | Revision n; `Reconcile` delta rows in the current open payroll month; entitlements unchanged | — | FR-LAB-010 |

### FLOW-08 Orthodontics

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Doctor | Creates the ortho case and enters Gross, Deposit, Months | Case, single work item (price = Gross), terms revision 1; **no charge, no installment** | Optional discount | FR-ORT-001, FR-ORT-002 |
| 2 | Doctor / Sales / Manager | Sets a fixed-EGP discount on the ortho line | Discount entry | Doctor finalizes | FR-FIN-021, FR-ORT-003 |
| 3 | Doctor | Finalizes the Visit | Charge (original Gross, discount, final Effective); Deposit validated ≤ Effective; `Months` equal installments from Effective − Deposit, once | Sales / Manager collect | FR-ORT-004 |
| 4 | Sales / Manager | Records payment to the ortho charge, then manually applies it to installment(s) | Allocation (distribution at allocation time); `installment_payments` rows; derived statuses | J2 reminds Sales the day before due | FR-FIN-004, FR-ORT-006, FR-ORT-007, FR-NOTIF-004 |
| 5 | Manager | Edits terms: Gross increase | +Δ `OrthoTermsChange`; one `PriceChange` installment of Δ; discount not re-applied | — | FR-ORT-008, FR-ORT-009 |
| 6 | Manager | Attempts Gross decrease | Rejected `ortho.total_decrease_not_supported`; nothing written | — | FR-ORT-010 |
| 7 | Manager | Edits Deposit or Months | Terms revision only; `requires_schedule_adjustment` true | Manager adjusts schedule | FR-ORT-011..013 |
| 8 | Manager | Submits the schedule batch (revise amount / due date, add, retire) | Atomic revisions; must end balanced and conforming; paid history untouched | Valid schedule | FR-ORT-014..019 |
| 9 | Manager | Refund / reversal of an ortho payment | Normal correction rows; negative applications; terms / schedule untouched | — | FR-ORT-020 |

### FLOW-09 Doctor Payroll

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | System | Recognizes Doctor earnings at collection / waterfall release | `doctor_earnings.recognized_at` decides the month | — | FR-FIN-028 |
| 2 | Manager | Creates the Doctor draft for a calendar month (inactive Doctors included) | Items = unpaid earnings of the month; totals | Optional adjustments | FR-PAYROLL-009, FR-FIN-030 |
| 3 | Manager | Adds adjustments (Draft only) | Adjustment rows | — | FR-PAYROLL-010 |
| 4 | Manager | Finalizes after month end, then marks paid | Sealed (G2); Paid | Doctor reads own payroll | FR-PAYROLL-008, FR-PAYROLL-011 |

### FLOW-10 Sales Payroll

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Manager | Creates Sales employee with required rate; may change the rate later | Rate history revisions (future collections only) | — | FR-STAFF-003, FR-STAFF-004 |
| 2 | Manager | Creates the Sales draft for a month | Base salary snapshot; one commission item per net allocation of Lead-sourced patients before deactivation, at the rate in force at `allocated_at`; Walk-In none | Bonus / Deduction | FR-PAYROLL-001..007, FR-PAYROLL-012 |
| 3 | Manager | Adds Bonus / Deductions; finalizes after month end; marks paid | Net = base + commission + bonus − deductions; sealed | Sales reads own payroll | FR-PAYROLL-008, FR-PAYROLL-013 |

### FLOW-11 Assistant Attendance → Overtime → Payroll

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Manager | Creates the Assistant staff record (no account) and sets salary and overtime hourly rate | `staff.assistants` | — | FR-STAFF-006..008 |
| 2 | Manager | Records daily attendance | One record per Assistant per day; no pay effect | — | FR-STAFF-012..014 |
| 3 | Manager | Records approved overtime hours | Entry with server-computed rate snapshot and amount | (void-and-re-enter to correct) | FR-STAFF-015, FR-STAFF-016 |
| 4 | Manager | Creates the monthly draft | Salary snapshot; Base Pay = snapshot; overtime items attached | Partial month? | FR-PAYROLL-014 |
| 5 | Manager | (Partial month) enters Base Pay with reason code | `ManagerManual` Base Pay; no proration | — | FR-PAYROLL-015 |
| 6 | Manager | Finalizes after month end; marks paid | Final = Base Pay + Overtime; sealed | Monthly report shows Base Pay and Overtime | FR-PAYROLL-016, FR-REPORT-005 |

### FLOW-12 Referral → Follow-Up

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Doctor | Creates a referral from own Visit (type, reason, notes, follow-up instructions) | `crm.referrals` status New; New Doctor Referral notification to all active Sales | Sales sees it | FR-REF-001, FR-NOTIF-006 |
| 2 | Sales | Reads the referral (destination / type, reason, instructions — never notes) | — | Sales follows up | FR-REF-003 |
| 3 | Sales | Creates / works a Follow-Up linked to the referral; updates referral status (Contacted / Booked / Completed / Lost) | `crm.follow_ups` (open predicate); referral status | Booking (FLOW-03) if booked | FR-CRM-005, FR-CRM-006, FR-REF-004 |

### FLOW-13 Patient Website Feedback

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | System | Visit finalized | Feedback request (Pending); Feedback Request Pending notification | Sales sees the queue | FR-FDB-001, FR-FDB-002 |
| 2 | Sales | Prepares the unique link, sends it manually, marks sent | Token hash stored; state Sent | Patient opens the link | FR-FDB-003, FR-FDB-004 |
| 3 | Patient | Opens and submits the form on the clinic website (no login, no expiry) | `PatientWeb` entry; state Submitted | Manager reads results | FR-FDB-005..007, FR-FDB-011 |

### FLOW-14 Sales-Entered Feedback

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Sales / Manager | Enters feedback about a Doctor for a finalized Visit | `SalesEntered` entry linked to the Visit's request; request Submitted; public token now answers already-submitted | Manager reads results | FR-FDB-008, FR-FDB-010 |
| 2 | Sales / Manager | Enters general feedback (no Visit) | `SalesEntered` entry with NULL Visit and request | Manager reads results | FR-FDB-009, FR-FDB-011 |

### FLOW-15 Refund / Financial Correction

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Manager | Chooses Refund / Reversal / Reallocation / Replacement with reason and Idempotency-Key | `payment_corrections` header + negative / moved rows | Propagation | FR-FIN-012..015 |
| 2 | System | Propagates | General lab: negation at recorded lab %; fixed lab: Reconcile deltas; negative earnings in current open month; negative commission at original rate; sealed payroll untouched | — | FR-FIN-016, FR-LAB-004, FR-PAYROLL-006 |
| 3 | Manager | Corrects a non-ortho charge price / discount | `charge_adjustments` row; Billed ≥ Collected; ortho → `ortho.use_terms_edit` | — | FR-FIN-017, FR-ORT-019 |

### FLOW-16 Inactive Sales Redistribution

| # | Actor | Action | State / Data Change | Next Actor / Action | FR |
|---|---|---|---|---|---|
| 1 | Manager | Deactivates a Sales employee | Staff + user inactive; stamp rotated (sessions end) | Redistribution in the same transaction | FR-STAFF-009 |
| 2 | System | Other Active Sales exist | Open Leads / Follow-Ups redistributed evenly (counts differ by ≤ 1); `responsibility_history` batch | New owners receive reminders | FR-CRM-010 |
| 3 | System | No Active Sales exists | Items unassigned (NULL); deactivation still succeeds | Manager assigns from Unassigned lists | FR-CRM-011, FR-CRM-009 |
| 4 | System | Later collections from the employee's Lead-sourced patients | No commission accrues to anyone; shown as "not accrued — inactive Sales" | — | FR-PAYROLL-005, FR-CRM-012 |

---

## 30. Forbidden / Out-of-Scope V1 Behavior

V1 **must not implement** any of the following (not as a module, table, column, permission, route, job, enum value, label, UI element or test fixture other than negative tests):

| # | Forbidden behavior | Basis |
|---|---|---|
| 1 | Inventory (stock, warehouse, reorder, valuation; "Medical Supplies" exists only as an expense category) | BASE §5, GRD §2 |
| 2 | X-ray / imaging | BASE §5, GRD §2 |
| 3 | Multi-branch (`branch_id`, branch module, selector, cross-branch report) | Q-BRN-1 |
| 4 | Assistant login, account, `user_id`, role, dashboard, API audience, self-check-in or notification | GRD §2 |
| 5 | Patient portal / patient accounts | BASE §5 |
| 6 | Patient mobile app | BASE §5 |
| 7 | Insurance integration | BASE §5 |
| 8 | Digital consent forms, e-signature, consent storage | Q-DOC-2 |
| 9 | Printed invoice / receipt / tax invoice | Q-DOC-1 |
| 10 | Expense attachments (files, images, receipts, PDFs) | Q-EXP-1 |
| 11 | Daily Closing / Close Day / sealed daily state | Q-FIN-DC |
| 12 | Feedback expiry (`expires_at`, Expired status, J6) | Q-FDB-1 |
| 13 | `Arrived` appointment status | Q-APP-4 |
| 14 | `Waiting` appointment status (and any Checked-In status or CheckIn permission) | Q-APP-4 |
| 15 | Walk-In commission pool (tables, payroll lines, routes, rate default) | Q-PAT-1 |
| 16 | Doctor attribution holds / pending Doctor attribution / reassignment- or deactivation-triggered finance handlers | Q-FIN-9 |
| 17 | `separatelyPricedWork` flag | Q-CLN-2 |
| 18 | Self-service password reset, reset tokens, email / SMS reset | Q-ACC-1 |
| 19 | Expense Finalized state / finalize or adjustment routes | Q-EXP-2 |
| 20 | Percentage discounts | Q-FIN-3 |
| 21 | Ortho Total decrease handling (V1 rejects it) | Q-ORT-3b |
| 22 | Automatic installment regeneration / redistribution / oldest-first application; negative installment; installment deletion | Q-ORT-2, Q-ORT-5 |
| 23 | Manager initial work price, consultation fee, Fixed Lab or Ortho terms | OWN Change 13 |
| 24 | Recalculation of old Sales commission after a rate change | Q-COMM-1 |
| 25 | Assistant bonus, deduction, commission, incentive, automatic proration; Assistant `final-amount` route | Q-PAY-ASST-2 |
| 26 | Attendance-based deductions; overtime inferred from attendance; Sales overtime; Doctor attendance | Q-ATT-1, Q-OT-1 |
| 27 | Reopen-Visit route; Manager approval or Manager clinical correction of finalized records | Q-CLN-1 |
| 28 | Doctor notifications, Doctor dashboard, Doctor feedback access | SEC §4 note 3 |
| 29 | Language switch / non-Arabic UI | Q-UI-1 |
| 30 | Automated WhatsApp / SMS / email delivery | BASE §5 |
| 31 | Unconfirmed proposals: `ILabAllocationStrategy`, `CaseDoctorAllocation`, TreatmentPlan / FinancialAgreement workflow, OnHold / Abandoned case states, addendum workflow, `PatientContact` aggregate, Manager approval workflows, price lists, deviation reports, Doctor-share clawback rules | GRD §2 |
| 32 | Generic Medical History / Chronic Diseases / Previous Surgeries free-text fields; medical detail fields; "other condition" field | GRD §4a |
| 33 | Rendered diagram files (PNG / SVG / JPG) or a rendered-output folder in the design package | OWN Change 11 |

---

## 31. Deferred Items

Exactly two items remain deferred. **Neither blocks implementation.** Nothing else is open.

| ID | Item | Status | V1 behavior / timing |
|---|---|---|---|
| **Q-ORT-3b** | Orthodontic Total Price **decrease** (which installments shrink; treatment of collected money, Patient Credit and Doctor entitlement) | DEFERRED / OPEN FOR FUTURE VERSION | V1 **rejects** any decrease with `ortho.total_decrease_not_supported` (FR-ORT-010). No negative installment, no automatic reduction, no generic-correction workaround. Not a V1 blocker. |
| **OPS-1** | Exact physical / storage locations of the two backup recovery private keys (held by the Clinic Manager as a person) | DEFERRED | Must be decided **before production go-live** (before the first production backup). Blocks no application code (FR-OPS-007). |

---

## 32. Functional Completion Definition

V1 is **functionally complete** only when all of the following hold:

1. **Every FR is implemented** exactly as written, with no added business behavior, status, permission, field, workflow or automatic allocation.
2. **Every FLOW-01 … FLOW-16** runs end-to-end in an integration environment against real PostgreSQL with the real role bootstrap, guards G1, G2, G4–G10 and G8b active.
3. **Every negative requirement** (Section 30 and the "negative requirement" FRs) is proven by a negative test (no route, no column, no status, no job).
4. **Owner-required tests pass:** the 94 tests of GRD §13, FC-1…FC-20, OT-1…OT-22, OC-1…OC-24, SEC #1–72, FIN #1–38, API A11 #1–58 and B9 #1–27, the DATABASE_DESIGN test paragraphs, including the executable finance proofs (FIN §17 A–V: Owner cases A–G, Fixed Lab P1–P3, commission 50 + 70 = 120, Ortho 20,000 / 2,000 / 5,000 / 5 → 18,000 / 13,000).
5. **Privacy canaries pass** over every Doctor and Sales response, OpenAPI document, PDF, CSV, notification, log and error body (contact, clinical, referral-notes and medical-value canaries).
6. **Role matrix:** the generated matrix over every mapped route equals SEC §4; every mapped route has a key assigned to at least one role; exactly six anonymous endpoints.
7. **Ledger invariants** hold under randomized replays: payment = allocations + credit; event identity; Clinic Share ≥ 0; 0 ≤ earnings ≤ entitlements; Σ lab = min(L, Σ effective collections) per fixed-lab case.
8. **Arabic RTL contract** holds on every screen, message, report, export and print.
9. **The first implementation database gate** (DB §24) has passed: all migrations apply to an empty PostgreSQL database and every documented FK / UNIQUE / CHECK exists.
10. **Deferred items** are untouched except: Q-ORT-3b rejection implemented; OPS-1 resolved before production go-live.

Exactly two items remain deferred (Section 31: Q-ORT-3b and OPS-1).
