# SECURITY_DESIGN.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Source:** Step 8 as approved plus its corrections, the Step 10/11 security amendments, the Step 12–14 key-grant, backup and restore corrections, **synchronized with the completed Owner Review and the final Owner business changes** (OWNER_REVIEW_NOTES.md): Manager-created accounts and Manager-set passwords (no reset workflow), Sales/Manager appointment status actions, patient arrival and the Doctor queue, Doctor price / Doctor-Sales-Manager discount, controlled correction of own finalized records, Manager-only refunds and corrections, Manager Ortho terms and installment edits, Assistant and Sales attendance, Assistant overtime, Sales-recordable Materials / Petty expenses, dual-entry feedback, referral Doctor notes hidden from Sales, and the Sales visibility of DOB / referral destination / referral reason / follow-up instructions.

Labels: **CONFIRMED** = a business rule the Owner stated; enforced literally, nothing added. **TECHNICAL** = my security implementation of a confirmed rule; adds no business behavior. **DEFAULT** = a technical value that may be vetoed. **DEFERRED** = intentionally not decided in the design (OPS-1 key locations; Q-ORT-3b Ortho total decrease); nothing is implemented.

---

## 1. Security model: four layers on every request

**CONFIRMED** Backend enforcement, never frontend-only.

| Layer | What it does | Fails as |
|---|---|---|
| 1. Authentication | Valid session, active user, current security stamp | 401 |
| 2. Endpoint permission | Every mapped endpoint declares one permission key (or a composite policy); the user's role must hold it | 403 |
| 3. Resource scope | "Own work" / "own compensation" filters; identity taken only from the signed-in principal | 404 (existence not revealed) |
| 4. Role-specific projection | Each audience has its own DTO built by its own query; a forbidden field does not exist in that DTO | n/a |

No layer relies on another. A Doctor endpoint never loads the full patient and then hides a field; it selects only clinical columns into a DTO that has no contact property.

## 2. Authentication

**Login flow.** `GET /api/auth/csrf`; `POST /api/auth/login` (per-IP limit first); ASP.NET Core Identity verifies the password; unknown users go through a dummy hash verification so time and message never reveal whether a username exists (always "Invalid credentials"); `is_active = false` fails with the same generic message and is audited; on success the server issues the session cookie and returns role and permission keys (menu use only, never an authority). Login success and failed attempts are audited (CONFIRMED); the attempted username is truncated to 64 characters; the password is never stored.

| Item | Decision |
|---|---|
| Hashing (CONFIRMED: secure hashing) | ASP.NET Core Identity PBKDF2-HMAC-SHA512, per-user salt, ≥ 210,000 iterations (Argon2id custom hasher acceptable). **Passwords are never stored readable and never recoverable, including by the Manager.** |
| Policy (DEFAULT) | Minimum 12 characters, deny-list of common/breached passwords, no composition rules, no forced periodic expiry. Applies to every password the Manager sets. |
| Initial Manager | Created by a one-time deployment command with the initial password entered at the operator prompt; no default account in any environment |
| **Account creation (Q-ACC-1, CONFIRMED)** | The **Manager creates staff application accounts**, choosing the **username and initial password**, and tells the employee directly. No email / mobile identity, no invitation or activation link. |
| **Forgotten password (Q-ACC-1, CONFIRMED)** | **Employee asks the Manager → the Manager SETS A NEW PASSWORD** (`Identity.Users.SetPassword`) → tells the employee. The old password is not displayed or recoverable (only a hash exists). Setting a password rotates the user's security stamp (ends their sessions), clears any lockout, and writes an **A+** audit entry (actor, target, time; **never** the password or hash). |
| Changing one's own password | `POST /api/auth/change-password` (authenticated, requires the current password). Not a reset workflow; a technical convenience. |
| MFA | Not required by any confirmed rule; Identity supports TOTP; recommended for the Manager as later hardening, not V1 scope unless requested |

**No self-service password-reset workflow exists (CONFIRMED).** There are no reset tokens, no reset flag, no recovery state, no email / SMS delivery and no `/api/auth/password-reset/*` route. If the **Manager's own** password is lost, the operator (project developer) runs the local break-glass command `set-manager-password` on the server (inside the `api` container, never over HTTP); it is audited with a system marker.

**Lockout and rate limiting (CONFIRMED: auth rate limiting).** Per-IP limit on `/api/auth/*` 10/min then 429 (DEFAULT); account lockout after 5 consecutive failures for 15 minutes (DEFAULT); operator command unlocks a locked Manager; client IP taken from forwarded headers only when they come from the reverse proxy's internal network; general per-user limit 300/min (DEFAULT); patient search additionally 60/min per user (DEFAULT, technical).

## 3. Sessions, CSRF, headers

| Property | Value |
|---|---|
| Cookie | `__Host-clinic.session`, HttpOnly, Secure, Path=/, no Domain, **SameSite=Strict** (SPA and API share one origin through the proxy) |
| Idle / absolute | 20 minutes sliding / 10 hours (DEFAULT) |
| Per-request validation | Security stamp checked against the database on **every** request, plus `is_active` (deactivation is immediate) |
| Permissions | Loaded per request from a cached role-permission map; the cookie holds none |
| Logout | Deletes the cookie, sends `Clear-Site-Data: "cache","storage"` |
| Sign-out everywhere | Manager can rotate any user's stamp |

Honest trade-off: no server-side session table, so a cookie copied before logout stays valid until idle/absolute expiry or stamp rotation (password change, role change, deactivation, forced sign-out). Making logout rotate the stamp is a one-line change if wanted.

**CSRF (CONFIRMED where applicable).** SameSite=Strict; anti-forgery token in `X-CSRF-TOKEN` on every POST/PUT/PATCH/DELETE (global; an endpoint without it fails the startup test); `Origin` and `Sec-Fetch-Site` verified on unsafe methods; GET never changes state; production CORS off (shared origin); staging/dev use an explicit allow-list, never `*` with credentials.

**Anonymous endpoints (the complete allow-list: six endpoints).** `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /health/live`, `GET /health/ready`, `GET /api/public/feedback/{token}` and `POST /api/public/feedback/{token}` (Q-FDB-2: the unique-token feedback form on the clinic website; 256-bit random token stored only as a hash, **no expiry**, one submission per request, rate-limited per IP and per token hash, no login; the GET returns only the Arabic form definition and the Doctor's display name — never a patient identifier; the POST returns a neutral acknowledgement; unknown, malformed and already-submitted tokens give indistinguishable generic responses except the neutral "already submitted" page; CORS only for the clinic website origin on these routes). `/health/ready` is not routed publicly (internal network only). **Authenticated without a permission key** (declared `[AuthenticatedOnly]`): `GET /api/me`, `POST /api/auth/logout`, `POST /api/auth/change-password`.

**Headers.** HSTS, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy` (all denied), `frame-ancestors 'none'`, CSP `default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'`. No inline scripts. `Cache-Control: no-store` on every authenticated response.

## 4. Role model and RBAC

**CONFIRMED business rule.** The business roles are Manager, Doctor and Sales / Reception. An account operating as a Doctor must never gain patient-contact access through another role.

**TECHNICAL implementation.**

| Control | Detail |
|---|---|
| **One role per account** | `identity.user_roles` has a **unique constraint on `user_id`**. A combination cannot be stored. |
| **No union** | Permissions come from the single role's row set. No merging logic exists anywhere. |
| **Staff link must match the role** | DOCTOR ⇒ a `staff.doctors` row and no sales row; SALES_RECEPTION ⇒ the reverse; MANAGER ⇒ neither. Command-handler check + startup consistency test. |
| **Role change** | Manager action; audited (A+); rotates the stamp; refused if the target's staff row does not fit the new role. Doctor ↔ Sales on one account is not possible in V1. |
| **Later combinations** | Would need an explicitly designed and approved role definition, never a union. |
| **Hard guard for contact data** | `Patients.Contact.Read`, `Patients.Contact.Write` and every key returning C-Contact data can be seeded only to SALES_RECEPTION and MANAGER; a migration test and a startup test fail if any contact-bearing key maps to DOCTOR. |

**Key structure.** `<Module>.<Resource>.<Action>[.<Scope>]`. Keys live in `identity.permissions`; mappings in `identity.role_permissions`. **No in-app role editor in V1**: changes go through a reviewed deployment migration. The Manager is not a wildcard: every Manager capability is listed and testable. **Anything not confirmed is denied.**

### Final RBAC matrix

✔ allowed · — denied · **own** = only own records. Basis: C = CONFIRMED, T = technical consequence of confirmed rules.

**Patients**

| Permission key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Patients.Search.Clinical` (Patient ID, name only) | ✔ | — | ✔ | C |
| `Patients.Search.Contact` (ID, name, phone) | — | ✔ | ✔ | C |
| `Patients.Clinical.Read` | ✔ all | — | ✔ | C |
| `Patients.Clinical.Write` | own current visit | — | ✔ | T |
| `Patients.Identity.Write` | — | ✔ | ✔ | C |
| **`Patients.MedicalHistory.Read`** *(v1.1)* — read the structured Patient Medical History Intake | ✔ all | ✔ | ✔ | C (Doctor / Sales access confirmed); Manager: T, preserves the Manager's existing authority |
| **`Patients.MedicalHistory.EditIntake`** *(v1.1)* — create and update the intake | **— (never)** | ✔ | ✔ | C (Sales / Reception collects and edits); Manager: T. Doctor write access is not approved. |
| `Patients.Contact.Read` / `.Write` | — | ✔ | ✔ | C |
| `Patients.Reassign` | — | — | ✔ | C |
| `Patients.Timeline.Clinical` | ✔ | — | ✔ | C |
| `Patients.Timeline.Operational` | — | ✔ | ✔ | T |

**Patient registration requires BOTH `Patients.Identity.Write` AND `Patients.Contact.Write`** (composite policy `[HasAllPermissions(...)]`). An identity-only permission can never write contact data. No role gains a capability from this.

**Scheduling**

| Key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Scheduling.Availability.Publish` | — | — | ✔ | C |
| `Scheduling.Availability.View` | — | ✔ | ✔ | C |
| `Scheduling.Availability.ViewOwn` | ✔ | — | ✔ | C |
| `Scheduling.Appointments.View.All` | — | ✔ | ✔ | C |
| `Scheduling.Appointments.View.Own` | ✔ | — | ✔ | C |
| `Scheduling.Appointments.Book` | — | ✔ | ✔ | C |
| `Scheduling.Appointments.CreateFollowUp` | ✔ | ✔ | ✔ | C |
| `Scheduling.Appointments.Reschedule` | — | ✔ | ✔ | C |
| `Scheduling.Appointments.RescheduleOwnFollowUp` | own | — | — | C |
| `Scheduling.Appointments.UpdateStatus` (**Confirm, Cancel, No Show**) | — | ✔ | ✔ | **C (Q-APP-3)** |
| **`Scheduling.Arrival.Record`** — record that the patient physically arrived (event, not a status) | — | ✔ | — | **C (Q-APP-4)**; Manager deliberately not granted (T5) |
| **`Scheduling.Arrival.Void`** — undo a mistaken arrival | — | ✔ | — | T |
| **`Scheduling.Queue.ReadOwn`** — the Doctor's own arrival queue | own | — | — | **C (Q-APP-4)** |

"Doctor: book general appointment: LIMITED" is implemented as `CreateFollowUp` only. **Overlapping appointments for the same Doctor are allowed for everyone who can book (Sales and Manager); there is no override permission because there is nothing to override (Q-APP-1/2).** Arrival never changes Appointment status; starting the linked Visit (a Doctor action) moves the Appointment to In Treatment.

**Clinical, prescriptions, referrals, CRM**

| Key | Doctor | Sales | Manager | Resource scope |
|---|---|---|---|---|
| `Clinical.Visits.Read` | ✔ all | — | ✔ | none |
| `Clinical.Visits.Write` | own visit | — | ✔ | `visit.doctor_id` = principal and Draft/InProgress |
| `Clinical.Visits.Finalize` | own visit | — | ✔ | `visit.doctor_id` = principal |
| `Clinical.Treatments.Write` | own visit | — | ✔ | Work through the Doctor's own editable visit and own work item |
| `Clinical.SalesSummary.Read` (whitelist) | — | ✔ | ✔ | none |
| `Clinical.WorkItems.EnterPrice` (**Price**) — the **initial** price of a work item | own work item | — | **— (final correction pass: the Manager has no initial-price authority)** | `performing_doctor_id` = principal and no charge exists yet. After the Charge exists the only price change is the Manager's audited `Finance.Charges.Adjust` (`PriceCorrection`; never for an Orthodontic Total, which only `Orthodontics.Terms.Edit` changes) |
| **`Finance.Discount.Set`** (**Discount**, fixed EGP, before Visit finalization) | **own pricing line** | **✔ any pricing line whose price the Doctor has entered, through the audience-safe `PricingLineSalesDto` (no clinical content)** | ✔ any line (after establishment only through `Finance.Charges.Adjust`) | **C (Q-FIN-3, final Owner change)** |
| **`Finance.PricingLines.Read`** — the Sales / Manager pricing-line list (Price, Discount, Total, procedure name, treating Doctor) | — | ✔ | ✔ | T (Finance-facing contract; no clinical notes, teeth, steps, measurements, lab or percentages) |
| **`Clinical.Visits.EnterConsultationFee`** (the Doctor enters the fee, Q-FIN-W1) | own visit before finalization | — | **— (a later change is a Manager `Finance.Charges.Adjust` correction)** | **C (Q-FIN-W1)** |
| **`Clinical.Visits.CorrectFinalized`**, **`Prescriptions.CorrectFinalized`** — controlled, audited correction of one's own finalized Visit / Prescription | **own** | — | **— (no Manager approval, no Manager clinical correction)** | **C (Q-CLN-1)** |
| **`Finance.FixedLab.SetInitial`** — the **initial** Fixed Lab amount of a case (once; must exist before the first Fixed-lab Treatment charge is established) | own (the Doctor performing priced work on the case) | — | **—** | case has no Fixed Lab yet; the Manager can never be the initial actor |
| **`Finance.FixedLab.Revise`** — revise an existing Fixed Lab (new audited revision with reason) | — | — | ✔ | a Fixed Lab already exists (otherwise 409 `fixed_lab.initial_required`). *There is no `Finance.FixedLab.Enter` or `.Edit` key: the ambiguous names are retired.* |
| `Prescriptions.Read` / `.Print` | ✔ all | — | ✔ | none |
| `Prescriptions.Write` | own editable visit | — | ✔ | prescription's visit is the Doctor's and not finalized |
| `Crm.Referrals.Create` | own visit | — | ✔ | |
| `Crm.Referrals.Read.Clinical` (includes the Doctor's referral **notes**) | ✔ | — | ✔ | T |
| `Crm.Referrals.Read.Sales`, `.UpdateStatus` (Sales sees **Referral Destination / Type, Referral Reason and Follow-Up Instructions**, Q-PRV-1; **never the Doctor's referral notes**) | — | ✔ | ✔ | C |
| `Crm.Leads.Manage`, `Crm.FollowUps.Manage` | — | ✔ (own = **My Leads**) | ✔ | C |
| **`Crm.Leads.ViewAll`** — the **All Leads** table incl. Responsible Sales Member; reassign | — | — | ✔ | **C (Q-EMP-1)** |
| **`Crm.Leads.ViewOwn`** — **My Leads** (rows where the principal is responsible) | — | ✔ | — | **C (Q-EMP-1)** |
| **`Crm.Responsibility.Assign`** — assign / reassign Leads and Follow-Ups, including those left unassigned when no Active Sales employee existed | — | — | ✔ | T (Q-EMP-1) |

**Orthodontics and finance**

| Key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Orthodontics.Cases.Read` (schedule and payment status) | ✔ | ✔ | ✔ | C |
| `Orthodontics.Cases.Create` (area, appliance; own visit) | ✔ | — | ✔ | C |
| **`Orthodontics.Terms.SetInitial`** (Gross Total Price, Deposit, Months, once, stored during the editable Visit for the Doctor's own ortho case; the charge and the initial installments are created later, at Visit finalization) | ✔ own | — | **— (Manager cannot set initial terms)** | **C (Q-ORT-4)** |
| **`Orthodontics.Terms.Edit`** (the **only** route that changes an established Ortho Gross Total, Deposit or Months; audited; a Total decrease is refused in V1; **Sales cannot set or edit terms**) | — | — | ✔ | **C (Q-ORT-4)** |
| **`Orthodontics.Installments.Edit`** (the Manager's schedule batch: change installment amounts, revise the due date of an unpaid installment, **add** a future installment and **retire** an unpaid one (never delete) — the manual schedule adjustment after a Deposit / Months edit; **Doctor and Sales cannot edit generated amounts**) | **—** | **—** | ✔ | **C (Q-ORT-1)** |
| **`Orthodontics.Installments.ApplyPayment`** (manually choose the installment(s) a payment is applied to; the same permission as the shorthand `Orthodontics.Payments.Apply`; Sales may apply payments but never edit terms or amounts) | — | ✔ | ✔ | **C (Q-ORT-2)** |
| `Finance.Payments.Record` | — | ✔ | ✔ | C |
| `Finance.PatientBalance.Read` (billed, collected, outstanding, **Patient Credit**) | — | ✔ | ✔ | C |
| **`Finance.PatientCredit.Apply`** — apply a patient's credit to a Charge | — | ✔ | ✔ | T (Q-PAY-3) |
| `Finance.OwnWork.Read` | own | — | n/a | C |
| **`Finance.Payments.Correct`** — Refund, Payment Correction, Payment Reversal, Reallocation | **—** | **—** | ✔ | **C (Q-PAY-1) — Manager only** |
| **`Finance.Charges.Adjust`** — audited price / discount correction of **non-Orthodontic** charges. It **cannot override `Orthodontics.Terms.Edit`**: on an ortho charge it returns 422 `ortho.use_terms_edit` for every kind (guard G9) even for the Manager | — | — | ✔ | **C (Q-FIN-3/Q-PAY-1)** |
| `Finance.LabPercentage.Configure` | — | — | ✔ | C |
| `Finance.Management.Read`, `Reports.Financial` | — | — | ✔ | C |
| `Reports.Export` | — | — | ✔ | T – technical seed (each export has its own key) |
| **`Expenses.CreateOperational`** — create a daily Materials / Medical Supplies or Petty / Miscellaneous expense (create only; no list, read, edit or totals) | — | ✔ | ✔ | **C (final Owner change)** |
| **`Expenses.Categories.ReadAllowed`** — read-only lookup of the categories Sales may use (`GET /api/expenses/allowed-categories`: active categories with `sales_can_record = true`, id and name only) | — | ✔ | ✔ | **C (final correction pass)** |
| `Expenses.ReadAll`, `Expenses.Manage` (Manager edits expenses directly; every edit audited with before / after; the creator fields are never overwritten; no attachments, no finalization), `ExpenseCategories.Manage` (**Manager only; Sales cannot manage categories, read unrestricted expense analytics or edit an existing expense**) | — | — | ✔ | C |

**Compensation, staff, reports, system**

| Key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Staff.DoctorPercentage.Configure` | — | — | ✔ | C |
| **`Staff.SalesCommission.Configure`** — set the Sales commission rate **at creation (required)** and change it later (Manager only, audited; **FUTURE COLLECTIONS ONLY** — the rate in force at each collection's `allocated_at` applies, old collections and finalized payroll are never recalculated; effective-dated history `staff.sales_commission_rate_history`) | — | — | ✔ | **C (Q-COMM-1)** |
| `Staff.Manage` (covers the whole **Assistant staff lifecycle** plus Doctor and Sales records: create, edit name / basic information, activate, deactivate) | — | — | ✔ | T |
| **`Staff.AssistantSalary.Configure`** *(v1.1)* — change an Assistant's current salary (audited) | — | — | ✔ | T – Manager-only technical key; **not** a business role |
| **`Staff.AssistantOvertimeRate.Configure`** — set an Assistant's overtime hourly rate (audited) | — | — | ✔ | **C (Owner change)** |
| **`Staff.AssistantOvertime.Manage`** — record / approve / void Assistant overtime hours | — | — | ✔ | **C (Owner change)**; Sales has no overtime |
| **`Staff.Attendance.Manage`** — create, edit and read Attendance records of Assistants and Sales / Reception employees (Manager-maintained; no self-check-in) | — | — | ✔ | **C (Owner change)** |
| `Reports.Attendance` — attendance and approved-overtime reports | — | — | ✔ | **C (Owner change)** |
| `Payroll.Doctor.ReadOwn` | own | — | — | C |
| `Payroll.Doctor.ManageAll`, `Payroll.Sales.ManageAll` (monthly; Walk-In / Direct collections give Sales commission 0 — there is no pool) | — | — | ✔ | C |
| **`Payroll.Assistant.ManageAll`** *(v1.1)* — view Assistant payroll, create monthly drafts, **manually enter Base Pay for a partial month (audited)**, add the month's overtime items, finalize, mark paid | — | — | ✔ | T – Manager-only technical key |
| `Payroll.Sales.ReadOwn` | — | own | — | C |
| `Reports.Dashboard.Sales` | — | ✔ | — | C |
| `Reports.Dashboard.Manager` | — | — | ✔ | C |
| `Feedback.Queue.Manage` (see requests, send the unique link) | — | ✔ | ✔ | C |
| **`Feedback.Entries.Record`** — record Patient feedback about a Doctor directly (source `SalesEntered`; stored as feedback, never as a clinical note) | — | ✔ | ✔ | **C (Owner change)** |
| `Feedback.Results.Read` | — | — | ✔ | **C (Owner change: Manager reads results)**; Doctor and Sales get no results access |
| **`Notifications.Read.Own`** | **— (denied)** | ✔ | ✔ | T – **Doctors have no notification inbox in V1** |
| `Audit.Read` | — | — | ✔ | C |
| `Configuration.Manage`, `Identity.Users.Manage` (create accounts with username and initial password) | — | — | ✔ | C / T |
| **`Identity.Users.SetPassword`** — set a new password for a user | — | — | ✔ | **C (Q-ACC-1)** |

Notes: (1) **Registration and intake split (v1.1):** the Sales registration form contains only name, DOB or recorded age, and contact fields. The structured Patient Medical History Intake is collected and edited by Sales / Reception through its own form and keys (`Patients.MedicalHistory.*`); it is not part of registration and a patient may have no intake yet. Chief Complaint is written by the Doctor or Manager. Sales still MUST NOT see detailed Doctor-authored clinical information. This is a deliberate, narrow exception to the earlier Sales clinical-privacy rule. (2) Post-finalization corrections are governed by Q-CLN-1: only the owning Doctor, through the controlled path; G1 blocks every other change, including the Manager's. (3) Not confirmed, hence denied: a Doctor dashboard, Doctor access to feedback results, Doctor notifications, any Sales view of Doctor Share/Clinic Share/lab/percentages, any Sales access to expense lists or reports, any Assistant data for Doctor or Sales. Discount access gives Sales no clinical-note access, and expense-create access gives Sales no Manager report access. (4) The Manager keeps the key `Notifications.Read.Own` as technical administrative visibility; no confirmed notification type targets the Manager, so the inbox is empty. (5) A mapped route whose key has no role fails the build; every key above is assigned. No `Scheduling.Appointments.CheckIn` key exists (arrival uses `Scheduling.Arrival.Record`).

## 5. Endpoint authorization

- Every endpoint carries `[HasPermission("Key")]`; the **fallback policy denies** any endpoint without metadata.
- Routes are separated by audience (`/api/doctor`, `/api/sales`, `/api/manager`, `/api/me`, `/api/public`, `/api/auth`); the prefix is organization only, the permission key is the check. The Manager holds both audiences' keys and receives **that audience's DTO** on shared routes; "own" endpoints return 404 for the Manager.
- **Startup test:** every mapped route has a key assigned to at least one role, or `[AuthenticatedOnly]`, or is on the six-item anonymous list.
- **Generated matrix test:** calls every mapped endpoint as each role, expecting 200 or 403 (composite policies understood).
- Authorization and validation run before handler logic. Errors are RFC 7807 with generic text.
- Cross-role calls fail by design: Doctor on `/api/sales/*` → 403; Sales on `/api/doctor/patients/{id}` → 403.

## 6. Resource-scoping rules

| # | Rule | Enforcement |
|---|---|---|
| S1 | **Identity comes from the principal, never from input.** Own endpoints accept no `doctorId`/`salesEmployeeId`. | Repository takes the id from the authenticated user; architecture test forbids such parameters on Doctor/Sales routes |
| S2 | **Doctor own-work finance** computed only from charges where `performing_doctor_id` is the Doctor (ownership never moves on reassignment); entitlements / earnings by `doctor_id` | Query joins from the Doctor's own work items; never a case/patient total |
| S3 | **Sales own compensation** filtered on principal's `sales_employee_id` | Same pattern; Manager has a separate endpoint family |
| S4 | **Doctor payroll own** | Same pattern |
| S5 | **Own notifications** `recipient_user_id = principal` | Sales and Manager only |
| S6 | **Doctor writes are tied to own, editable work.** A Draft/InProgress visit may be modified only if `visit.doctor_id` is theirs | Command-handler check, backed by G1 and column-level grants |
| S7 | **Doctor reschedule** only a follow-up the Doctor created | Check on `created_by_user_id` and `kind` |
| S8 | **404 instead of 403** for scoped resources | |
| S9 | **Read breadth does not imply write rights.** Doctor A can read Doctor B's visits, treatments, steps, measurements and prescriptions; no write endpoint accepts them | Handlers load the target and compare its owning Doctor to the principal; mismatch → 404 |
| S10 | **Finalized clinical work is changed only through the controlled correction path, by the owning Doctor** (Q-CLN-1); no other actor, no Manager, no reopening | Handlers refuse everything else; G1 blocks in the database unless the correction flag **and** a matching `visit_corrections` row of the Visit's own Doctor exist |
| S12 | **Doctor arrival queue** shows only the principal's appointments; no Doctor id in the request | Query by principal |
| S13 | **My Leads / My Follow-Ups** filter on the principal's `sales_employee_id`; the Manager uses the All Leads family | Same pattern as S3 |
| S11 | **Doctor contact access is impossible by role** | One role per account + seeding guard |

Doctors view all patients' clinical history (CONFIRMED); S1–S11 add no "which patients may this Doctor open" rule and no approval workflow. The Manager reaches the Doctor clinical write endpoints to edit **non-finalized** records only (no Manager-initiated visit workflow exists); the Manager can never overwrite a finalized clinical record or a ledger row (money changes only through new correction rows).

## 7. Sensitive-data boundary

| Class | Contents | Doctor | Sales | Manager |
|---|---|---|---|---|
| C-Identity | name, patient code, **DOB** (Sales sees DOB, Q-PRV-1) or recorded age | ✔ | ✔ | ✔ |
| C-Contact | phone, WhatsApp, email, **address**, CRM/contact notes, **Original Sales Rep / acquisition source (Walk-In or Lead)**, lead data, appointment `sales_notes`, follow-up notes and contact attempts | **✘** | ✔ | ✔ |
| C-MedicalIntake *(v1.1)* | the structured Patient Medical History Intake only: Allergy, Hypertension, Viral Hepatitis, Diabetes, Kidney Problems, Bleeding / Clotting Problems, Heart Problems, Rheumatic Diseases, Smoker, Previous Surgical Operations, Taking Medications, Last Dental Visit, Pregnant, Breastfeeding (each Yes / No / Not answered) | ✔ read only | ✔ read + edit | ✔ |
| C-Clinical | chief complaint, clinical notes, steps, measurements, implant details, prescriptions and every other Doctor-authored clinical record | ✔ | **✘** | ✔ |
| C-Operational (whitelist) | procedure name, treating Doctor, treatment status, **referral destination / type, referral reason and follow-up instructions (Q-PRV-1)**, appointment, arrival, price, discount, total, paid, remaining, **Patient Credit**, ortho installments | ✔ (finance: own work; arrival queue) | ✔ | ✔ |
| C-DoctorFinance | Doctor percentage, Doctor Share, Clinic Share, lab, other Doctors' work | own only | **✘** | ✔ |
| C-Management | clinic revenue, profit, expenses, management reports | ✘ | ✘ | ✔ |
| C-Compensation | payroll, commission | own payout | own only | ✔ |
| C-Security | audit log, users, permissions, settings | ✘ | ✘ | ✔ |

**Doctor privacy (CONFIRMED):** a Doctor never receives phone, WhatsApp, email, address, CRM/contact notes or Original Sales Representative. Doctors search by Patient ID or name only.

**Sales whitelist (CONFIRMED, mandatory):** Sales / Reception may access the explicitly whitelisted structured Patient Medical History Intake that they collect (class C-MedicalIntake). They do not receive detailed clinical records or Doctor-authored clinical information. Sales MUST NOT see clinical notes, root canal measurements, detailed procedure notes, or prescription medication details. Sales sees procedure name, treating Doctor, status, **DOB, referral destination / type, referral reason**, follow-up instructions, price, discount (and may set it), paid, remaining, Patient Credit, ortho installment info. Sales never sees Doctor clinical notes, **Doctor referral notes**, root-canal measurements, treatment technical details or prescription medication details. Sales never sees Doctor percentages, Doctor Share, Clinic Share, lab or fixed-lab data.

**Ortho visibility:** installment schedule and payment status visible to Doctor, Sales and Manager (ORT-5); the Doctor-facing DTO includes percentages/shares/lab only for the Doctor's own work.

### Defense in depth for Doctor privacy

| # | Control |
|---|---|
| D1 | Storage separation: contact data in `patients.patient_contacts` and `crm`; `patients.patients` has no contact columns |
| D2 | Contract separation: `IPatientClinicalReader` and `IPatientContactReader` are separate; `Features/Doctor` cannot reference the contact interface |
| D3 | DTO projection by `Select` from clinical tables; no contact field exists to serialize |
| D4 | Contact types tagged `[ContactData]`; architecture test fails if any type under `Features/Doctor` references one |
| D5 | Doctor search handler queries `patient_code` and `full_name` only; a phone typed in the name box finds nothing |
| D6 | Canary tests: unique canary values in every contact field; every Doctor endpoint, PDF, CSV, notification and log is scanned |
| D7 | Optional DB defense-in-depth (recommended before go-live): Doctor read endpoints use a read-only role `clinic_doctor_read` with no SELECT on `patients.patient_contacts` or `crm` |

**Doctor-visible DTO rules.** `DoctorAppointmentDto` excludes `sales_notes`. Referral DTO excludes Sales follow-up work. Follow-up timeline entries: type, due date, status only. The Doctor queue DTO carries patient name, age display, reason, appointment and arrival time — no contact data and no Original Sales Employee. Sales receives the referral destination, **reason** and follow-up instructions unconditionally (Q-PRV-1; no visibility setting) but **`crm.referrals.notes` — the Doctor's referral notes — exist in no Sales projection**. Age/DOB per the age display rule; `doctor_dob_visible` default visible, nothing restricted today.

**Sales code never reads clinical tables.** `Clinical.Contracts` exposes only `ISalesClinicalSummary` and the Finance-facing `ISalesPricingLines` (`PricingLineSalesDto`). Sales entering feedback or an expense receives only the created record echo. The Sales timeline has no prescription entries and no visit content. Sales sees no teeth, details, measurements, steps or notes.

**Sales-authored Doctor-visible text** (appointment `reason`) rejects phone/email patterns with a message pointing to `sales_notes`. This is a heuristic: it catches obvious numbers and addresses, not every form of personal information.

**Manager.** Holds every permission marked ✔ above. No operation can update or delete a ledger row because the database refuses it. Authority operates through reversals, revisions and adjustments. Every confirmed audited Manager action is audited with before/after values.

## 8. Employee deactivation (CONFIRMED: Active/Inactive, login disabled, history preserved)

One Staff transaction: (1) staff row `is_active = false`, `deactivated_at`; (2) `identity.users` `is_active = false`, lockout maximum, **security stamp rotated** (next request from any session fails with 401); (3) audit entry A+; (4) nothing deleted. **Sales employee:** in the same transaction the employee's open Leads and open Follow-Ups (`status <> 'FollowedUp'`) are **redistributed evenly among Active Sales employees** with `responsibility_history` rows; Original Sales attribution is untouched; the employee receives no commission for allocations after `deactivated_at` and no one receives it in their place; if **no** Active Sales employee exists the open items become **unassigned** (NULL responsible) and are surfaced to the Manager (Unassigned Leads / Follow-Ups) instead of being lost; deactivation is not refused and nothing is transferred. **Doctor:** deactivation changes nothing financial — the Doctor remains entitled to collections on previously performed work and payroll continues (Q-FIN-9); no hold is created. The last active Manager and the user's own account cannot be deactivated. Notification fan-out sends only to active users. Reactivation is audited, rotates the stamp, and requires the Manager to set a new password. Background jobs run as a system principal with a system marker in audit.

## 9. Audit security (CONFIRMED audit list, TECHNICAL mechanism)

| Control | Detail |
|---|---|
| Atomic with the change | Audit row written in the same transaction; if it fails the business action fails (fail closed) |
| Insert-only | `clinic_app` has INSERT and SELECT on `audit.audit_log`; UPDATE, DELETE, TRUNCATE revoked |
| Read access | `Audit.Read` Manager-only (CONFIRMED); reading is itself audited |
| Record access | Doctor opens, Sales contact views and Manager views each write an access event |
| Never stored | Passwords, password hashes, stamps, feedback tokens (interceptor deny-list). Setting a password is audited (actor, target, time) without any password material. **Therefore a password change can never be reconstructed from audit.** |
| Skipped tables | The audit interceptor skips `infra.data_protection_keys` |
| Medical History (v1.1) | Creation and every update write an audit row with actor, timestamp, patient id and the **names** of the changed fields; **values are never written to the audit log, normal application logs, notification payloads or exports** (a canary-value test enforces this) |
| Contact values | Contact edits store previous/new values flagged `is_restricted`; only the Manager reads the log |
| Failed login | IP, user agent, truncated username |
| Tamper resistance | Insert-only grants plus off-site backups; hash-chaining not adopted (over-engineering for one clinic) |
| Honest limit | The G1 flag (`SET LOCAL clinic.audited_correction`) guards against accidents, not a compromised application; set in exactly one code class (architecture-tested); the Doctor-correction path always writes its `visit_corrections` row and audit row, and G1 also requires that row |
| Expenses | Every Manager edit writes before / after of the changed fields (who, when) |
| Money corrections | Discount entries (amount, actor, role, time), discount / price corrections, refunds, reversals, reallocations, Ortho term edits and schedule changes (amount, due-date revision, manual addition, retirement), fixed-lab revisions, Assistant manual Base Pay: all **A+** with reason |
| Staff operations | Attendance create / edit (before / after), overtime entry record / void, overtime-rate and salary changes, Sales-entered expenses and feedback (actor, time): **A+** |

## 10. Export, download and PDF security

CONFIRMED: Doctor exports contain no contact information; exports are audited; prescriptions never print patient phone, WhatsApp, email or other contact details. TECHNICAL: each export is its own permission (only the prescription PDF for Doctor/Manager and Manager reports `Reports.Export` are implied; no Sales/Doctor data export exists); every export is built from an audience DTO with an explicit column whitelist; streamed, not stored; `Content-Disposition: attachment`, `no-store`; file names never contain patient names or phones; CSV cells starting with `=`, `+`, `-`, `@` are neutralized; every export writes an audit entry (user, type, filter, row count).

**Prescription PDF.** Built only from `PrescriptionPrintModel`: clinic name, logo, clinic contact (the clinic's own), patient name, age (DOB-derived or recorded-age form), Doctor name, specialty, date, medication, dose, frequency, duration, instructions, signature/stamp area. The model has no patient-contact field; the generator never receives a patient entity; PDF metadata has no patient identifiers; no QR/barcode with patient data. Sales cannot print (medication detail). A test extracts PDF text and asserts no canary contact value appears. **No printed receipt, invoice or consent form exists in V1 (Q-DOC-1/2); no print endpoint exists for them.** Application-generated printable text is Arabic.

## 11. Logging and scrubbing

No request/response bodies on patient, contact, finance or payroll endpoints; Serilog policy drops `[ContactData]` properties; an enricher masks Egyptian mobile numbers, `+20` forms and emails in all messages and exception text; patient search is a **POST body**, never a query string; proxy access logs exclude query strings; `EnableSensitiveDataLogging` off in staging/production (startup check fails if on); Hangfire job arguments hold ids only; notification payloads carry no patient name or phone; error-tracking events pass through the same scrubber with request bodies disabled; validation errors name the field, never the value; duplicate-phone checks run only in Sales/Manager flows; application logs retained as a ceiling of 30 days (DEFAULT), bounded by size on the host.

## 12. Shared workstation and browser cache

`Cache-Control: no-store` on every authenticated response; frontend query cache cleared on logout and on any 401; no localStorage/IndexedDB persistence of data, no tokens in browser storage; `Clear-Site-Data` on logout; 20-minute idle warning then end; URLs hold only opaque ids and patient codes; sensitive inputs `autocomplete="off"`; no service worker; print stylesheet shows only the print model; React route guards are usability only.

## 13. XSS and SQL injection (CONFIRMED requirements)

XSS: React default escaping + ESLint ban on `dangerouslySetInnerHTML`; all free text is plain text (no HTML/Markdown rendering); CSP blocks inline scripts; PDF/print templates encode values; CSV neutralized; E2E test stores `<script>` / `<img onerror>` payloads in every free-text field. SQLi: EF Core parameterized LINQ; raw SQL only via parameterized `FromSql`; analyzer fails the build on concatenated SQL; per-endpoint sort/filter whitelists; escaped `ILIKE`/trigram wildcards; JSON queries parameterized with whitelisted keys; FluentValidation on every request.

## 14. Secrets management (CONFIRMED)

No secrets in the repository (gitleaks in CI), in images, or in CI logs. Runtime: Docker secrets mounted as files (`*_FILE` convention) from a root-owned 600 directory on the VM; separate values per environment. Database passwords: one per role, rotated at least yearly (DEFAULT) and on any staff change with access. GitHub holds only deploy credentials (SSH key, host, pinned host key). Dependabot + `dotnet list package --vulnerable` + `npm audit`. Hangfire dashboard not exposed to the internet (SSH tunnel only).

**Data Protection keys.** Stored in `infra.data_protection_keys`, encrypted with a certificate delivered as a Docker secret **mounted only in the `api` container**. `clinic_app`: **SELECT and INSERT only** (no UPDATE/DELETE/TRUNCATE; if the integration test shows another privilege is required, only that one is added, narrowly). `clinic_worker`: **no access**; its composition root does not register persisted Data Protection; the Worker container does not receive `dp_cert`. `clinic_doctor_read`, PUBLIC and `clinic_backup`: no access. Keys are never deleted by the application and never audited. Lost keys only invalidate sessions.

## 15. Least-privilege database access (CONFIRMED)

| Role | Used by | Grants |
|---|---|---|
| `clinic_migrator` | migrator container / CI step only | DDL and owner of all tables and views. Not usable by the running app. |
| `clinic_app` | API process | SELECT and INSERT on business tables. UPDATE only on **L** and **M** tables, restricted by column. **No UPDATE on H tables, no DELETE, no TRUNCATE.** INSERT/SELECT only on `audit.audit_log`. SELECT on `infra.__EFMigrationsHistory`. SELECT and INSERT on `infra.data_protection_keys`. No DDL, no superuser, no ownership. |
| `clinic_worker` | Worker process | **No access to `staff.assistants`, `staff.attendance_records`, `staff.assistant_overtime_entries`, `payroll.assistant_payrolls` or `payroll.assistant_payroll_overtime_items`.** `identity.v_active_user_roles` SELECT; **no** access to `identity.users`/`user_roles`/`roles`. `staff.v_sales_employee_activity` SELECT only; **no** access to `staff.sales_employees`/`staff.doctors` (no salary, commission, percentages, profile or contact data). `notifications.notifications` SELECT, INSERT and UPDATE on `status`, `sent_at`, `retry_count`, `last_error` only. SELECT on `scheduling`, `orthodontics`, `feedback`; `crm.follow_ups` SELECT excluding `notes`; `configuration.settings` SELECT; Hangfire schema; SELECT on `infra.__EFMigrationsHistory`. **None** on `patients.patient_contacts`, clinical schemas, `prescriptions`, `finance`, `infra.data_protection_keys`. |
| `clinic_backup` | backup container only | **No `pg_read_all_data`.** Explicit grants: USAGE on each business schema, `identity`, `audit`, `configuration`, `hangfire`; SELECT on every table in those schemas and on their sequences; SELECT on `infra.__EFMigrationsHistory`. **`infra.data_protection_keys`: no grant (SELECT fails).** Anything else in `infra` and every write privilege anywhere: none. Connection from the VM address only, TLS required, `CONNECTION LIMIT 2`. |
| `clinic_doctor_read` | optional (D7) | SELECT on clinical and operational schemas; no `patients.patient_contacts`, no `crm` |

Tables and views are owned by `clinic_migrator` (owners bypass grants). `search_path` pinned; TLS `verify-full`; managed-database firewall allows only the application host. Column-level grants plus the G1–G8 guards protect frozen columns twice. Every new table needs a grant decision **and** a backup-grant decision (CI fails otherwise). A test connects as each role and proves each forbidden statement fails.

## 16. Security testing checklist

| # | Test | Type |
|---|---|---|
| 1 | Canary scan: every Doctor endpoint, PDF, CSV and notification contains no contact canary | CI |
| 2 | Doctor search by exact canary phone returns zero results | CI |
| 3 | Sales responses (timeline, notifications included) contain no clinical canary | CI |
| 4 | Generated role matrix: every mapped endpoint × role → expected 200/403 | CI |
| 5 | Startup test: every mapped route has a key assigned to at least one role / `[AuthenticatedOnly]` / anonymous allow-list | CI |
| 6 | Scope tests: Doctor A requesting Doctor B's work item → 404; Sales cannot reach another Sales user's payroll | CI |
| 7 | Doctor case view with two Doctors' charges shows only own items | CI |
| 8 | Doctor and Sales cannot reach Manager reports, audit, expenses or settings | CI |
| 9 | Architecture tests: no contact type under `Features/Doctor`; no `[ContactData]` in Doctor DTOs; no `doctorId` on own routes | CI |
| 10 | CSRF: state-changing request without token fails; cookie flags correct | CI |
| 11 | Deactivated user's active session fails on next request | CI |
| 12 | Lockout and per-IP limit trigger; unknown-user and wrong-password responses identical | CI |
| 13 | Manager-set password: the new password works and the old fails; sessions of that user end; lockout cleared; the audit row holds no password material; no stored value reveals the old password; no `/api/auth/password-reset/*` route is mapped and no reset-token column exists | CI |
| 14 | Password hasher parameters meet the minimum | CI |
| 15 | SQLi payloads in search/sort/filter have no effect | CI |
| 16 | XSS payloads in every free-text field render as text | E2E |
| 17 | `no-store` and security headers present | CI |
| 18 | Log scrubbing: no canary in captured logs; no sensitive EF logging in production config | CI |
| 19 | DB grants: `clinic_app` cannot UPDATE payments or audit, cannot DELETE, cannot ALTER | CI |
| 20 | Audit written in the same transaction; failure rolls the business action back | CI |
| 21 | Phone-pattern check rejects phone numbers in Sales-authored Doctor-visible fields | CI |
| 22 | Secret scanning and dependency vulnerability scans | CI |
| 23 | OWASP ZAP baseline against staging | pre-release |
| 24 | Manual penetration checklist before go-live: role switching, IDOR on every GUID route, export abuse | go-live gate |
| 25 | A Doctor account can never obtain `Patients.Contact.Read` or any contact-bearing key (seeded mapping, effective permissions of every Doctor test user, every contact endpoint → 403) | CI |
| 26 | Roles cannot be combined: a second `user_roles` row for one user fails at the database; role-change refuses a mismatched staff link; no code path computes a permission union | CI |
| 27 | Doctor A can view Doctor B's previous treatment, visits, steps, measurements | CI |
| 28 | Doctor A cannot modify Doctor B's historical treatment (write endpoints 404; direct `clinic_app` writes on finalized visits blocked by G1) | CI |
| 29 | Doctor A can view and print Doctor B's prescription | CI |
| 30 | Doctor A cannot modify Doctor B's prescription (404) | CI |
| 31 | Doctor A cannot modify their own finalized visit, prescription or treatment through ordinary endpoints (S10); the controlled correction path succeeds only for the owning Doctor, writes a `visit_corrections` row and an audit entry, leaves the Visit Finalized, and is denied for Doctor B, Sales and Manager | CI |
| 32 | Doctor A cannot enter or edit a price on Doctor B's work item; **the Manager cannot enter an initial price (403 on `Clinical.WorkItems.EnterPrice` and on the consultation fee) and can only correct through `Finance.Charges.Adjust`** | CI |
| 33 | Fixed Lab: the Doctor enters the initial amount once through own work (`Finance.FixedLab.SetInitial`); the Manager gets 403 on SetInitial; a second Doctor cannot create another; the Manager's `Finance.FixedLab.Revise` creates a new revision; the Doctor gets 403 on Revise | CI |
| 34 | `Orthodontics.Terms.SetInitial`: **Doctor (own case) only** (Manager and Sales 403); `Orthodontics.Terms.Edit` and `Orthodontics.Installments.Edit`: Manager only (Doctor and Sales 403); `Orthodontics.Installments.ApplyPayment`: Sales and Manager only | CI |
| 35 | `Finance.Discount.Set`: Doctor (own pricing line), Sales / Reception and Manager before finalization; a percentage discount is rejected; after the Charge exists Doctor and Sales get 403 / 409; `Finance.Payments.Correct` and `Finance.Charges.Adjust` are Manager only (Doctor and Sales 403 on every refund / reversal / correction / adjustment route) | CI |
| 36 | A Doctor account has no notification endpoint access (403); Sales and Manager read only their own | CI |
| 37 | Registration with only one of `Patients.Identity.Write` / `Patients.Contact.Write` → 403 | CI |
| 38 | `clinic_worker` cannot SELECT `identity.users`, `user_roles`, `roles`, `patients.patient_contacts`, `staff.sales_employees`, `staff.assistants`, `staff.doctors` or `infra.data_protection_keys` | CI |
| 39 | `clinic_app` has only SELECT/INSERT on `infra.data_protection_keys`; a cookie issued by instance A validates on instance B | CI |
| 40 | `clinic_backup` SELECT on `infra.data_protection_keys` fails; it has no write privilege anywhere; it is not a member of `pg_read_all_data` | CI |
| 41 | No placeholder or `/api/unassigned/*` route is mapped; every mapped route's key is assigned to at least one role | CI |
| 42 *(v1.1)* | No `ASSISTANT` role exists; no `Assistant` principal, account, login or `user_id` column exists; the three application roles are the only seeded roles | CI |
| 43 *(v1.1)* | `Staff.Manage`, `Staff.AssistantSalary.Configure`, `Staff.AssistantOvertimeRate.Configure`, `Staff.AssistantOvertime.Manage`, `Staff.Attendance.Manage` and `Payroll.Assistant.ManageAll` are in the Manager's effective list only; Doctor and Sales get 403 on every Assistant route, and no Assistant data appears in any `/api/doctor/*` or `/api/sales/*` response | CI |
| 44 *(v1.1)* | A caller holding `Staff.Manage` but not `Staff.AssistantSalary.Configure` cannot change an Assistant salary; a caller without `Payroll.Assistant.ManageAll` cannot read or change Assistant payroll; `Payroll.Assistant.ManageAll` alone cannot change a salary | CI |
| 45 *(v1.1)* | Doctor and Sales each get 403 on every Assistant staff / attendance / overtime / payroll route (list, read, create, update, deactivate, reactivate, salary, overtime rate, overtime entries, drafts, finalize, mark-paid); the Manager succeeds | CI |
| 46 *(v1.1)* | Creating an Assistant creates **no** `identity.users` or `user_roles` row; `identity.roles` contains exactly MANAGER, DOCTOR and SALES_RECEPTION | CI |
| 47 *(v1.1)* | `clinic_worker` cannot SELECT `staff.assistants` or `payroll.assistant_payrolls`; `clinic_backup` can SELECT both and write neither | CI |
| 48 | No `Scheduling.Appointments.CheckIn` key and no Arrived / Waiting status exist anywhere; `Scheduling.Arrival.Record` is held by Sales only, `Scheduling.Queue.ReadOwn` by Doctor only; recording arrival leaves `appointments.status` unchanged | CI |
| 49 *(v1.1)* | `Patients.MedicalHistory.Read` is held by Doctor, Sales and Manager; `Patients.MedicalHistory.EditIntake` by Sales and Manager only. A Doctor account holds no `EditIntake` and no intake write route is reachable (403). Neither key is contact-bearing, and a Doctor still holds no contact key (test 25 unchanged) | CI |
| 50 *(v1.1)* | Sales / Reception can create and update the intake; Sales has no `Patients.Clinical.Read`: holding the intake key gives no access to clinical notes, measurements, prescriptions or Doctor percentage / shares / lab (403 on those routes; those fields absent from the Sales intake DTO) | CI |
| 51 *(v1.1)* | Canary medical values (for example a unique last-dental-visit text and a flagged condition) never appear in application logs, the audit log (names only), notification payloads, exports or any Manager financial report | CI |
| 52 *(v1.1)* | The Assistant has no access: there is no Assistant principal, so no intake route or permission applies to one | CI |
| 53 | Sales and Manager can Confirm / Cancel / No Show; Sales can Reschedule; Doctor 403 on all | CI |
| 54 | Sales can read DOB, referral destination and referral reason; Sales DTOs still contain no clinical note, measurement, prescription detail, Doctor percentage / share, Clinic Share or lab value (canary scan) | CI |
| 55 | The public feedback GET / POST work for a valid token with no session; return no patient identifier; the token is not derivable from patient / visit ids; no expiry; second submission refused | CI |
| 56 | Creating a Sales employee without a commission percentage is rejected | CI |
| 57 | The Doctor queue endpoint returns only the principal's arrived patients and contains no contact data; Sales cannot call it | CI |
| 58 | My Leads returns only the principal's leads; All Leads and reassignment are Manager only | CI |
| 59 | No receipt, invoice, consent, expense-attachment, Daily Closing, Inventory, X-ray or branch route exists | CI |
| 60 | Sales / Reception sets a Discount on a pricing line through `PricingLineSalesDto`; the response and the OpenAPI schema contain no clinical note, tooth / detail, measurement, step, lab or Doctor-percentage field (canary scan); Doctor can set it on own lines only (404 on another Doctor's); Manager on any line | CI |
| 61 | Attendance routes (Assistants and Sales): Manager only (`Staff.Attendance.Manage`); Doctor and Sales 403; no Assistant principal or check-in route exists; attendance data appears in no `/api/doctor/*` or `/api/sales/*` response | CI |
| 62 | Overtime rate and overtime entry routes: Manager only; no overtime route accepts a Sales employee; overtime is never created from attendance times | CI |
| 63 | `Expenses.CreateOperational`: Sales and Manager can POST a Materials / Petty expense; Sales gets 403 on list / read / edit / category / report routes and on categories without `sales_can_record`; the create response echoes only the created record; the audit row names the Sales user | CI |
| 64 | Referral canary: a unique Doctor referral-note canary never appears in any Sales response, OpenAPI document, export, notification or log, while the reason, destination / type and follow-up instructions do | CI |
| 65 | Feedback: Sales and Manager can record `SalesEntered` feedback (source and actor stored, never written to a clinical table); Doctor and Sales get 403 on `Feedback.Results.Read`; Manager reads; the public token POST creates `PatientWeb` only | CI |
| 66 | Unassigned Leads / Follow-Ups (no Active Sales left) are visible and assignable by the Manager only; a Sales employee cannot read another Sales employee's payroll or commission | CI |
| 67 | **Final-correction permission matrix test:** the effective permissions of every role equal the table in §4 for `Clinical.WorkItems.EnterPrice` (Doctor only), `Finance.FixedLab.SetInitial` (Doctor only), `Finance.FixedLab.Revise` (Manager only), `Orthodontics.Terms.SetInitial` (Doctor only), `Orthodontics.Terms.Edit` (Manager only), `Staff.SalesCommission.Configure` (Manager only), `Expenses.Categories.ReadAllowed` (Sales and Manager); no key named `Finance.FixedLab.Enter` / `.Edit` exists | CI |
| 68 | `PUT /sales-employees/{id}/commission` (future collections only): Manager 200; Doctor and Sales 403; the audit row holds old and new rate; a Sales employee cannot read the rate history | CI |
| 69 | `GET /api/expenses/allowed-categories`: Sales and Manager 200 with only active `sales_can_record` categories (id, name); Doctor 403; Sales gets 403 on category create / edit / deactivate, on `GET /expenses`, on expense analytics and on `PUT /expenses/{id}`; a Manager edit leaves `created_by_user_id` and `created_at` unchanged | CI |
| 70 *(ortho freeze)* | Generic adjustment on an ortho charge: the Manager's `POST /charges/{id}/adjustments` for an Orthodontic charge → 422 `ortho.use_terms_edit` (holding `Finance.Charges.Adjust` does not bypass `Orthodontics.Terms.Edit` validation); Doctor and Sales 403 on the route; no route accepts `OrthoTermsChange` from a client | CI |
| 71 *(ortho freeze)* | Sales (`Orthodontics.Installments.ApplyPayment`) can apply a payment to installments but gets 403 on `PUT /orthodontic-cases/{id}/terms` and `PUT /orthodontic-cases/{id}/installments`; the Doctor gets 403 on both after the initial entry; the Manager gets 403 on `SetInitial` | CI |
| 72 *(ortho schedule)* | `PUT /orthodontic-cases/{id}/installments` (amount, due date, add, retire) is Manager only: the Doctor and Sales get 403 on every operation, including `add` and `retire`; no role has a DELETE grant on `ortho_installments` / `ortho_installment_revisions`; the audit row of each revision holds actor, role, time, previous and new amount / due date and the reason |

## 17. Post-restore account and password security (CONFIRMED security requirement, TECHNICAL mechanism)

The audit log never holds passwords or hashes, so **a password change after the restore point cannot be reconstructed from it**. A restore returns the old hash, so an old password would work again. After ANY restore to an earlier point, the operator command `post-restore-lockdown` runs inside the `api` container before the system is opened:

1. Rotate every security stamp (signs everyone out).
2. **Passwords.** Lock every non-Manager staff account (lockout end set to the maximum). The operator runs the local `set-manager-password` command for the Manager; the **Manager then sets a new password for each employee** through the normal Set Password action, which clears that account's lockout. There are no reset tokens or delivery channels. Narrowed action, only with reliable evidence: if the pre-restore audit log is intact, only the users whose password was set after the restore point (events, never values) need new passwords; otherwise every non-Manager account does.
3. Re-apply deactivations and role changes after the restore point **where the old audit gives reliable evidence**; anything unverifiable is unknown and the Manager reviews the staff list before reopening.
4. Re-run the integrity queries and Finance invariants.
5. Notifications: dedupe keys prevent duplicates; reminders sent after the restore point may be regenerated for Sales (minor, expected).

No new clinic business rule is added.

## 18. Backup encryption and key custody

Backups are encrypted client-side with `age` to **two independent recovery recipients**. With normal `age` multi-recipient encryption **either private key alone decrypts the backup**: this is redundancy, **not two-person approval**. **CONFIRMED (OPS-1, partially decided): the Clinic Manager, as a person, holds both backup recovery private keys.** **DEFERRED:** the exact physical / storage locations are decided at deployment / go-live and are not invented here; they do not block implementation. Holding a key is **not** an application permission: the Manager role has no application right that touches a key, and no application role is tied to backup custody. **A recovery private key is never stored on the production VM**; losing both makes the off-site backups unreadable. Public keys are deployment configuration.

## 19. Security observations (design review)

No blocking security inconsistency is identified in this design review (see the Final Consistency Gate in DESIGN_FREEZE.md; implementation is not proven). For transparency: (1) Doctors may open every patient's full clinical history (CONFIRMED), a broad path by design; compensating controls are the confirmed ones (record-access auditing and the contact-data boundary). (2) The registration split follows from the mandatory Sales restriction. (3) Sales-authored text that Doctors read is a free-text leak path reduced only by a heuristic. (4) Accounts have a single role, so a person who needs both Doctor and Sales functions would need two accounts or an explicitly approved role definition. (5) Sales may set discounts and create Materials / Petty expenses: both are narrow, create-or-set-only paths with actor and role stored, a pricing view with no clinical data, no list or report access, and Manager-only correction afterwards, so the added exposure is bounded and audited. (6) The Manager's price, Fixed Lab and Ortho-term authority is limited to audited **corrections and revisions**; the Doctor owns the initial entries, so the Manager cannot silently create the numbers that drive the Doctor's own entitlement.

## 20. Technical defaults that may be vetoed

Session timeouts 20 min / 10 h; SameSite Strict; password policy 12 chars + deny-list + no expiry; lockout 5 failures / 15 min; auth rate limit 10/min/IP; permission mapping changes via migration only; one operational role per account; log ceiling 30 days.

Remaining non-final items touching security: **OPS-1** (storage locations of the two backup keys, deferred) and **Q-ORT-3b** (decrease of an Orthodontic total price, DEFERRED / OPEN FOR FUTURE VERSION; V1 rejects it with `ortho.total_decrease_not_supported`; no permission depends on it). Everything else in this document reflects Owner decisions. No Inventory and no X-ray appear anywhere.