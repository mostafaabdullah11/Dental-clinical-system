# SECURITY_DESIGN.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL
**Source:** Step 8 as approved plus its corrections (one role per account, Doctor write scoping, Doctor notification inbox removed, `Orthodontics.Terms.Set` unassigned, reset delivery as deployment configuration), the Step 10/11 security amendments (Worker views, composite registration policy, no unresolved-action routes, two technical Manager keys), the Step 12–13 Data Protection key grants, and the Step 14 backup, restore and key-custody corrections.

Labels: **CONFIRMED** = a business rule the user stated; enforced literally, nothing added. **TECHNICAL** = my security implementation of a confirmed rule; adds no business behavior. **DEFAULT** = a technical value that may be vetoed. **OPEN** = undecided; nothing is implemented.

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

**Login flow.** `GET /api/auth/csrf`; `POST /api/auth/login` (per-IP limit first); ASP.NET Core Identity verifies the password; unknown users go through a dummy hash verification so time and message never reveal whether a username exists (always "Invalid credentials"); `is_active = false` and `password_hash IS NULL` / `password_reset_required` fail with the same generic message and are audited; on success the server issues the session cookie and returns role and permission keys (menu use only, never an authority). Login success and failed attempts are audited (CONFIRMED); the attempted username is truncated to 64 characters; the password is never stored.

| Item | Decision |
|---|---|
| Hashing (CONFIRMED: secure hashing) | ASP.NET Core Identity PBKDF2-HMAC-SHA512, per-user salt, ≥ 210,000 iterations (Argon2id custom hasher acceptable) |
| Policy (DEFAULT) | Minimum 12 characters, deny-list of common/breached passwords, no composition rules, no forced periodic expiry |
| Initial Manager | Created by a one-time deployment command with a must-change-password flag; no default account in any environment |
| New accounts | Created with a creator-supplied initial password and a must-change-on-first-login flag (a mechanism; how it is communicated is not defined) |
| MFA | Not required by any confirmed rule; Identity supports TOTP; recommended for the Manager as later hardening, not V1 scope unless requested |

**Password reset (CONFIRMED: secure password reset).**
- Token: single-use, random, 60-minute expiry (DEFAULT), generated via Identity token providers. Using it rotates the security stamp (ends all sessions).
- Abuse protection: rate-limited endpoints; the same generic response whether or not the account exists.
- **Delivery is a deployment/configuration detail** through the notification-channel abstraction, selected per environment. Staff email/mobile identity and the clinic's account-recovery workflow are **OPEN**; no delivery workflow is defined or assumed. "Manager delivers the link" is withdrawn.
- Account-recovery initiation is **not exposed** as an endpoint until the workflow is confirmed; token issuance exists as an Identity service usable by the operator command.
- Lost Manager password: recovered by an operator command on the server (operational control, not a clinic workflow).

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

**Anonymous endpoints (the complete allow-list: six endpoints).** `GET /api/auth/csrf`, `POST /api/auth/login`, `POST /api/auth/password-reset/complete`, `GET /health/live`, `GET /health/ready`, and `POST /api/public/feedback/{token}` (present only if the token-based feedback option is chosen: 256-bit random token, hashed, single-use, rate-limited, returns no identity, CORS only for the clinic website origin on that route). `/health/ready` is not routed publicly (internal network only). **Authenticated without a permission key** (declared `[AuthenticatedOnly]`): `GET /api/me`, `POST /api/auth/logout`, `POST /api/auth/change-password`.

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
| `Scheduling.Appointments.UpdateStatus` (Cancel, Confirm, No Show) | — | — | ✔ | **T – technical seed** |

"Doctor: book general appointment: LIMITED" is implemented as `CreateFollowUp` only. `UpdateStatus` is Manager-only as the **current seed, not a clinic rule**; whether Sales may also cancel / mark no-show is unresolved (one-line seed change once confirmed).

**Clinical, prescriptions, referrals, CRM**

| Key | Doctor | Sales | Manager | Resource scope |
|---|---|---|---|---|
| `Clinical.Visits.Read` | ✔ all | — | ✔ | none |
| `Clinical.Visits.Write` | own visit | — | ✔ | `visit.doctor_id` = principal and Draft/InProgress |
| `Clinical.Visits.Finalize` | own visit | — | ✔ | `visit.doctor_id` = principal |
| `Clinical.Treatments.Write` | own visit | — | ✔ | Work through the Doctor's own editable visit and own work item |
| `Clinical.SalesSummary.Read` (whitelist) | — | ✔ | ✔ | none |
| `Clinical.WorkItems.EnterPrice` | own work item | — | ✔ | `performing_doctor_id` = principal and no charge exists |
| `Finance.FixedLab.Enter` | own initial entry | — | ✔ | case has no fixed lab yet; entry via own work on the case |
| `Finance.FixedLab.Edit` | — | — | ✔ | none |
| `Prescriptions.Read` / `.Print` | ✔ all | — | ✔ | none |
| `Prescriptions.Write` | own editable visit | — | ✔ | prescription's visit is the Doctor's and not finalized |
| `Crm.Referrals.Create` | own visit | — | ✔ | |
| `Crm.Referrals.Read.Clinical` | ✔ | — | ✔ | T |
| `Crm.Referrals.Read.Sales`, `.UpdateStatus` | — | ✔ | ✔ | C |
| `Crm.Leads.Manage`, `Crm.FollowUps.Manage` | — | ✔ | ✔ | C |

**Orthodontics and finance**

| Key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Orthodontics.Cases.Read` (schedule and payment status) | ✔ | ✔ | ✔ | C |
| `Orthodontics.Cases.Create` (area, appliance; own visit) | ✔ | — | ✔ | C |
| **`Orthodontics.Terms.Set`** (total, deposit, months) | **—** | **—** | **—** | **OPEN – assigned to no role, no mapped route** |
| `Finance.Payments.Record` | — | ✔ | ✔ | C |
| `Finance.PatientBalance.Read` | — | ✔ | ✔ | C |
| `Finance.OwnWork.Read` | own | — | n/a | C |
| **`Finance.Discount.Set`** | **—** | **—** | **—** | **OPEN (Q-FIN-3) – assigned to no role, no mapped route** |
| `Finance.Adjustments.Create` | — | — | ✔ | C – key reserved; **no endpoint** (Q-PAY-1) |
| `Finance.LabPercentage.Configure` | — | — | ✔ | C |
| `Finance.Management.Read`, `Reports.Financial` | — | — | ✔ | C |
| `Reports.Export` | — | — | ✔ | T – technical seed (each export has its own key) |
| `Expenses.Read` / `.Manage` | — | — | ✔ | C |

**Compensation, staff, reports, system**

| Key | Doctor | Sales | Manager | Basis |
|---|---|---|---|---|
| `Staff.DoctorPercentage.Configure`, `Staff.SalesCommission.Configure` | — | — | ✔ | C |
| `Staff.Manage` (covers the whole **Assistant staff lifecycle** plus Doctor and Sales records: create, edit name / basic information, activate, deactivate) | — | — | ✔ | T |
| **`Staff.AssistantSalary.Configure`** *(v1.1)* — change an Assistant's current salary (audited) | — | — | ✔ | T – Manager-only technical key; **not** a business role |
| `Payroll.Doctor.ReadOwn` | own | — | — | C |
| `Payroll.Doctor.ManageAll`, `Payroll.Sales.ManageAll` | — | — | ✔ | C |
| **`Payroll.Assistant.ManageAll`** *(v1.1)* — view Assistant payroll, create drafts, finalize, mark paid | — | — | ✔ | T – Manager-only technical key |
| `Payroll.Sales.ReadOwn` | — | own | — | C |
| `Reports.Dashboard.Sales` | — | ✔ | — | C |
| `Reports.Dashboard.Manager` | — | — | ✔ | C |
| `Feedback.Queue.Manage` | — | ✔ | ✔ | C |
| `Feedback.Results.Read` | — | — | ✔ | T |
| **`Notifications.Read.Own`** | **— (denied)** | ✔ | ✔ | T – **Doctors have no notification inbox in V1** |
| `Audit.Read` | — | — | ✔ | C |
| `Configuration.Manage`, `Identity.Users.Manage` | — | — | ✔ | C / T |

Notes: (1) **Registration split:** Sales MUST NOT see detailed medical history, so Sales cannot enter or read it; the Sales registration form contains only name, DOB or recorded age, and contact fields; medical history, chronic diseases, surgeries and chief complaint are written by Doctor or Manager. (2) Post-finalization prescription edits are governed by Q-CLN-1 (OPEN); G1 blocks them for everyone, including the Manager. (3) Not confirmed, hence denied: a Doctor dashboard, Doctor access to feedback results, Doctor notifications, any Sales view of Doctor Share/Clinic Share/lab/percentages. (4) The Manager keeps the key `Notifications.Read.Own` as technical administrative visibility; no confirmed notification type targets the Manager, so the inbox is empty. (5) Unresolved business actions (`Orthodontics.Terms.Set`, `Finance.Discount.Set`) may exist as internal keys and handlers, but **no route maps them**. A mapped route whose key has no role fails the build.

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
| S2 | **Doctor own-work finance** computed only from charges where `performing_doctor_id` is the Doctor; earnings by `attributed_doctor_id` | Query joins from the Doctor's own work items; never a case/patient total |
| S3 | **Sales own compensation** filtered on principal's `sales_employee_id` | Same pattern; Manager has a separate endpoint family |
| S4 | **Doctor payroll own** | Same pattern |
| S5 | **Own notifications** `recipient_user_id = principal` | Sales and Manager only |
| S6 | **Doctor writes are tied to own, editable work.** A Draft/InProgress visit may be modified only if `visit.doctor_id` is theirs | Command-handler check, backed by G1 and column-level grants |
| S7 | **Doctor reschedule** only a follow-up the Doctor created | Check on `created_by_user_id` and `kind` |
| S8 | **404 instead of 403** for scoped resources | |
| S9 | **Read breadth does not imply write rights.** Doctor A can read Doctor B's visits, treatments, steps, measurements and prescriptions; no write endpoint accepts them | Handlers load the target and compare its owning Doctor to the principal; mismatch → 404 |
| S10 | **Finalized and historical clinical work is not editable by a Doctor**, including their own | Handlers refuse; G1 blocks in the database for everyone |
| S11 | **Doctor contact access is impossible by role** | One role per account + seeding guard |

Doctors view all patients' clinical history (CONFIRMED); S1–S11 add no "which patients may this Doctor open" rule and no approval workflow. The Manager reaches the Doctor clinical write endpoints to edit **non-finalized** records only (no Manager-initiated visit workflow exists); the Manager can never overwrite a finalized or ledger record.

## 7. Sensitive-data boundary

| Class | Contents | Doctor | Sales | Manager |
|---|---|---|---|---|
| C-Identity | name, patient code, DOB or recorded age | ✔ | ✔ | ✔ |
| C-Contact | phone, WhatsApp, email, **address**, CRM/contact notes, **Original Sales Rep**, lead data, appointment `sales_notes`, follow-up notes and contact attempts | **✘** | ✔ | ✔ |
| C-Clinical | medical history, chronic diseases, surgeries, chief complaint, clinical notes, steps, measurements, implant details, prescriptions | ✔ | **✘** | ✔ |
| C-Operational (whitelist) | procedure name, treating Doctor, treatment status, referral destination, appointment and follow-up instructions, price, paid, remaining, ortho installments | ✔ (finance: own work) | ✔ | ✔ |
| C-DoctorFinance | Doctor percentage, Doctor Share, Clinic Share, lab, other Doctors' work | own only | **✘** | ✔ |
| C-Management | clinic revenue, profit, expenses, management reports | ✘ | ✘ | ✔ |
| C-Compensation | payroll, commission | own payout | own only | ✔ |
| C-Security | audit log, users, permissions, settings | ✘ | ✘ | ✔ |

**Doctor privacy (CONFIRMED):** a Doctor never receives phone, WhatsApp, email, address, CRM/contact notes or Original Sales Representative. Doctors search by Patient ID or name only.

**Sales whitelist (CONFIRMED, mandatory):** Sales MUST NOT see detailed medical history, clinical notes, root canal measurements, detailed treatment measurements, or prescription medication details. Sales sees procedure name, treating Doctor, status, referral destination, follow-up instructions, price, paid, remaining, ortho installment info. Sales never sees percentages, shares, lab or Clinic Share.

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

**Doctor-visible DTO rules.** `DoctorAppointmentDto` excludes `sales_notes`. Referral DTO excludes Sales follow-up work. Follow-up timeline entries: type, due date, status only. Referral reason to Sales is governed by `referral_reason_visible_to_sales` (unset = hidden). Age/DOB per the age display rule; `doctor_dob_visible` default visible, nothing restricted today.

**Sales code never reads clinical tables.** `Clinical.Contracts` exposes only `ISalesClinicalSummary`. The Sales timeline has no prescription entries and no visit content. Sales sees no teeth, details, measurements, steps or notes.

**Sales-authored Doctor-visible text** (appointment `reason`) rejects phone/email patterns with a message pointing to `sales_notes`. This is a heuristic: it catches obvious numbers and addresses, not every form of personal information.

**Manager.** Holds every permission marked ✔ above. No operation can update or delete a ledger row because the database refuses it. Authority operates through reversals, revisions and adjustments. Every confirmed audited Manager action is audited with before/after values.

## 8. Employee deactivation (CONFIRMED: Active/Inactive, login disabled, history preserved)

One Staff transaction: (1) staff row `is_active = false`, `deactivated_at`; (2) `identity.users` `is_active = false`, lockout maximum, **security stamp rotated** (next request from any session fails with 401); (3) audit entry A+; (4) nothing deleted, reassigned or recalculated. The last active Manager and the user's own account cannot be deactivated. Notification fan-out sends only to active users. Open leads/follow-ups of a departed Sales employee are not moved automatically (shown as "owner inactive"; who inherits them is **Q-EMP-1, OPEN**). A deactivated Doctor with outstanding charges raises `DoctorDeactivated` (Finance attribution holds, **Q-FIN-9 OPEN**). Reactivation is audited, rotates the stamp, and requires a new password. Background jobs run as a system principal with a system marker in audit.

## 9. Audit security (CONFIRMED audit list, TECHNICAL mechanism)

| Control | Detail |
|---|---|
| Atomic with the change | Audit row written in the same transaction; if it fails the business action fails (fail closed) |
| Insert-only | `clinic_app` has INSERT and SELECT on `audit.audit_log`; UPDATE, DELETE, TRUNCATE revoked |
| Read access | `Audit.Read` Manager-only (CONFIRMED); reading is itself audited |
| Record access | Doctor opens, Sales contact views and Manager views each write an access event |
| Never stored | Password hashes, stamps, reset tokens, feedback tokens (interceptor deny-list). **Therefore a password change can never be reconstructed from audit.** |
| Skipped tables | The audit interceptor skips `infra.data_protection_keys` |
| Contact values | Contact edits store previous/new values flagged `is_restricted`; only the Manager reads the log |
| Failed login | IP, user agent, truncated username |
| Tamper resistance | Insert-only grants plus off-site backups; hash-chaining not adopted (over-engineering for one clinic) |
| Honest limit | The G1 flag (`SET LOCAL clinic.audited_correction`) guards against accidents, not a compromised application; set in exactly one code class (architecture-tested); the correction path always writes its audit row |

## 10. Export, download and PDF security

CONFIRMED: Doctor exports contain no contact information; exports are audited; prescriptions never print patient phone, WhatsApp, email or other contact details. TECHNICAL: each export is its own permission (only the prescription PDF for Doctor/Manager and Manager reports `Reports.Export` are implied; no Sales/Doctor data export exists); every export is built from an audience DTO with an explicit column whitelist; streamed, not stored; `Content-Disposition: attachment`, `no-store`; file names never contain patient names or phones; CSV cells starting with `=`, `+`, `-`, `@` are neutralized; every export writes an audit entry (user, type, filter, row count).

**Prescription PDF.** Built only from `PrescriptionPrintModel`: clinic name, logo, clinic contact (the clinic's own), patient name, age (DOB-derived or recorded-age form), Doctor name, specialty, date, medication, dose, frequency, duration, instructions, signature/stamp area. The model has no patient-contact field; the generator never receives a patient entity; PDF metadata has no patient identifiers; no QR/barcode with patient data. Sales cannot print (medication detail). A test extracts PDF text and asserts no canary contact value appears. A printable receipt/invoice is **undefined (Q-DOC-1)**; no print endpoint exists for it.

## 11. Logging and scrubbing

No request/response bodies on patient, contact, finance or payroll endpoints; Serilog policy drops `[ContactData]` properties; an enricher masks Egyptian mobile numbers, `+20` forms and emails in all messages and exception text; patient search is a **POST body**, never a query string; proxy access logs exclude query strings; `EnableSensitiveDataLogging` off in staging/production (startup check fails if on); Hangfire job arguments hold ids only; notification payloads carry no patient name or phone; error-tracking events pass through the same scrubber with request bodies disabled; validation errors name the field, never the value; duplicate-phone checks run only in Sales/Manager flows; application logs retained as a ceiling of 30 days (DEFAULT), bounded by size on the host.

## 12. Shared workstation and browser cache

`Cache-Control: no-store` on every authenticated response; frontend query cache cleared on logout and on any 401; no localStorage/IndexedDB persistence of data, no tokens in browser storage; `Clear-Site-Data` on logout; 20-minute idle warning then end; URLs hold only opaque ids and patient codes; sensitive inputs `autocomplete="off"`; no service worker; print stylesheet shows only the print model; React route guards are usability only.

## 13. XSS and SQL injection (CONFIRMED requirements)

XSS: React default escaping + ESLint ban on `dangerouslySetInnerHTML`; all free text is plain text (no HTML/Markdown rendering); CSP blocks inline scripts; PDF/print templates encode values; CSV neutralized; E2E test stores `<script>` / `<img onerror>` payloads in every free-text field. SQLi: EF Core parameterized LINQ; raw SQL only via parameterized `FromSql`; analyzer fails the build on concatenated SQL; per-endpoint sort/filter whitelists; escaped `ILIKE`/trigram wildcards; JSON queries parameterized with whitelisted keys; FluentValidation on every request.

## 14. Secrets management (CONFIRMED)

No secrets in the repository (gitleaks in CI), in images, or in CI logs. Runtime: Docker secrets mounted as files (`*_FILE` convention) from a root-owned 600 directory on the VM; separate values per environment. Database passwords: one per role, rotated at least yearly (DEFAULT) and on any staff change with access. GitHub holds only deploy credentials (SSH key, host, pinned host key). Dependabot + `dotnet list package --vulnerable` + `npm audit`. Hangfire dashboard not exposed to the internet (SSH tunnel only).

**Data Protection keys.** Stored in `infra.data_protection_keys`, encrypted with a certificate delivered as a Docker secret **mounted only in the `api` container**. `clinic_app`: **SELECT and INSERT only** (no UPDATE/DELETE/TRUNCATE; if the integration test shows another privilege is required, only that one is added, narrowly). `clinic_worker`: **no access**; its composition root does not register persisted Data Protection; the Worker container does not receive `dp_cert`. `clinic_doctor_read`, PUBLIC and `clinic_backup`: no access. Keys are never deleted by the application and never audited. Lost keys only invalidate sessions and reset tokens.

## 15. Least-privilege database access (CONFIRMED)

| Role | Used by | Grants |
|---|---|---|
| `clinic_migrator` | migrator container / CI step only | DDL and owner of all tables and views. Not usable by the running app. |
| `clinic_app` | API process | SELECT and INSERT on business tables. UPDATE only on **L** and **M** tables, restricted by column. **No UPDATE on H tables, no DELETE, no TRUNCATE.** INSERT/SELECT only on `audit.audit_log`. SELECT on `infra.__EFMigrationsHistory`. SELECT and INSERT on `infra.data_protection_keys`. No DDL, no superuser, no ownership. |
| `clinic_worker` | Worker process | **No access to `staff.assistants` or `payroll.assistant_payrolls` (v1.1).** `identity.v_active_user_roles` SELECT; **no** access to `identity.users`/`user_roles`/`roles`. `staff.v_sales_employee_activity` SELECT only; **no** access to `staff.sales_employees`/`staff.doctors` (no salary, commission, percentages, profile or contact data). `notifications.notifications` SELECT, INSERT and UPDATE on `status`, `sent_at`, `retry_count`, `last_error` only. SELECT on `scheduling`, `orthodontics`, `feedback`; `crm.follow_ups` SELECT excluding `notes`; `configuration.settings` SELECT; Hangfire schema; SELECT on `infra.__EFMigrationsHistory`. **None** on `patients.patient_contacts`, clinical schemas, `prescriptions`, `finance`, `infra.data_protection_keys`. |
| `clinic_backup` | backup container only | **No `pg_read_all_data`.** Explicit grants: USAGE on each business schema, `identity`, `audit`, `configuration`, `hangfire`; SELECT on every table in those schemas and on their sequences; SELECT on `infra.__EFMigrationsHistory`. **`infra.data_protection_keys`: no grant (SELECT fails).** Anything else in `infra` and every write privilege anywhere: none. Connection from the VM address only, TLS required, `CONNECTION LIMIT 2`. |
| `clinic_doctor_read` | optional (D7) | SELECT on clinical and operational schemas; no `patients.patient_contacts`, no `crm` |

Tables and views are owned by `clinic_migrator` (owners bypass grants). `search_path` pinned; TLS `verify-full`; managed-database firewall allows only the application host. Column-level grants plus G1–G7 triggers protect frozen columns twice. Every new table needs a grant decision **and** a backup-grant decision (CI fails otherwise). A test connects as each role and proves each forbidden statement fails.

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
| 13 | Reset token single-use, expires, rotates stamp | CI |
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
| 31 | Doctor A cannot modify their own finalized visit, prescription or treatment (S10) | CI |
| 32 | Doctor A cannot enter or edit a price on Doctor B's work item | CI |
| 33 | Fixed lab: Doctor enters initial amount once through own work; a second Doctor cannot create another or edit; Manager edit creates a new revision | CI |
| 34 | `Orthodontics.Terms.Set` is in no role's effective list and **no route is mapped** to it | CI |
| 35 | `Finance.Discount.Set` likewise: assigned to no role, no route mapped, a non-zero discount cannot be set | CI |
| 36 | A Doctor account has no notification endpoint access (403); Sales and Manager read only their own | CI |
| 37 | Registration with only one of `Patients.Identity.Write` / `Patients.Contact.Write` → 403 | CI |
| 38 | `clinic_worker` cannot SELECT `identity.users`, `user_roles`, `roles`, `patients.patient_contacts`, `staff.sales_employees`, `staff.assistants`, `staff.doctors` or `infra.data_protection_keys` | CI |
| 39 | `clinic_app` has only SELECT/INSERT on `infra.data_protection_keys`; a cookie issued by instance A validates on instance B | CI |
| 40 | `clinic_backup` SELECT on `infra.data_protection_keys` fails; it has no write privilege anywhere; it is not a member of `pg_read_all_data` | CI |
| 41 | No `/api/unassigned/*` route is mapped in the production configuration | CI |
| 42 *(v1.1)* | No `ASSISTANT` role exists; no `Assistant` principal, account, login or `user_id` column exists; the three application roles are the only seeded roles | CI |
| 43 *(v1.1)* | `Staff.Manage`, `Staff.AssistantSalary.Configure` and `Payroll.Assistant.ManageAll` are in the Manager's effective list only; Doctor and Sales get 403 on every Assistant route, and no Assistant data appears in any `/api/doctor/*` or `/api/sales/*` response | CI |
| 44 *(v1.1)* | A caller holding `Staff.Manage` but not `Staff.AssistantSalary.Configure` cannot change an Assistant salary; a caller without `Payroll.Assistant.ManageAll` cannot read or change Assistant payroll; `Payroll.Assistant.ManageAll` alone cannot change a salary | CI |
| 45 *(v1.1)* | Doctor and Sales each get 403 on every Assistant staff route and every Assistant payroll route (list, read, create, update, deactivate, reactivate, salary, drafts, finalize, mark-paid); the Manager succeeds | CI |
| 46 *(v1.1)* | Creating an Assistant creates **no** `identity.users` or `user_roles` row; `identity.roles` contains exactly MANAGER, DOCTOR and SALES_RECEPTION | CI |
| 47 *(v1.1)* | `clinic_worker` cannot SELECT `staff.assistants` or `payroll.assistant_payrolls`; `clinic_backup` can SELECT both and write neither | CI |
| 48 *(v1.1)* | No `Scheduling.Appointments.CheckIn` permission key exists in the catalog, any role's effective list, the route metadata or the RBAC matrix; no check-in route is mapped | CI |

## 17. Post-restore account and password security (CONFIRMED security requirement, TECHNICAL mechanism)

The audit log never holds passwords, hashes or reset tokens, so **a password change after the restore point cannot be reconstructed from it**. A restore returns the old hash, so Password A would work again. After ANY restore to an earlier point, the operator command `post-restore-lockdown` runs inside the `api` container before the system is opened:

1. Rotate every security stamp (signs everyone out; invalidates outstanding reset tokens).
2. **Passwords.** Default conservative action: set `password_hash = NULL` and `password_reset_required = true` for **all staff accounts**. Narrowed action, only with reliable evidence: if the pre-restore instance's audit log is intact, password-change/reset **events** (never values) after the restore point identify the affected users, and only they are forced to reset; if the audit log is unavailable or incomplete, the default applies.
3. Re-apply deactivations and role changes after the restore point **where the old audit gives reliable evidence**; anything unverifiable is unknown and the Manager reviews the staff list before reopening.
4. Re-run the integrity queries and Finance invariants.
5. Notifications: dedupe keys prevent duplicates; reminders sent after the restore point may be regenerated for Sales (minor, expected).

Delivery of the new reset tokens remains the open account-recovery configuration. Lost Manager access uses the existing operator recovery command. No new clinic business rule is added.

## 18. Backup encryption and key custody

Backups are encrypted client-side with `age` to **two independent recovery recipients**. With normal `age` multi-recipient encryption **either private key alone decrypts the backup**: this is redundancy, **not two-person approval**. Two-person control would need a different key-management design and is not part of V1. **Custody is OPEN (OPS-1) and is not tied to any application role**: the Manager application role does not automatically hold a backup key, because a backup contains password hashes, the audit log and every clinical and financial record, far more than the application exposes. Neither private key is ever on the VM; losing both makes the off-site backups unreadable. Public keys are deployment configuration.

## 19. Serious-issue check

None found. For transparency: (1) Doctors may open every patient's full clinical history (CONFIRMED), a broad path by design; compensating controls are the confirmed ones (record-access auditing and the contact-data boundary). (2) The registration split follows from the mandatory Sales restriction. (3) Sales-authored text that Doctors read is a free-text leak path reduced only by a heuristic. (4) Accounts have a single role, so a person who needs both Doctor and Sales functions would need two accounts or an explicitly approved role definition.

## 20. Technical defaults that may be vetoed

Session timeouts 20 min / 10 h; SameSite Strict; password policy 12 chars + deny-list + no expiry; lockout 5 failures / 15 min; auth rate limit 10/min/IP; reset token single-use 60 min (delivery = deployment configuration); permission mapping changes via migration only; one operational role per account; log ceiling 30 days.

Still OPEN and untouched by this document: Q-FIN-1, Q-FIN-2, Q-FIN-3, Q-FIN-6 (redefined), Q-FIN-9, Q-CLN-1, Q-EMP-1, account-recovery delivery, backup-key custody. No Inventory and no X-ray appear anywhere.
