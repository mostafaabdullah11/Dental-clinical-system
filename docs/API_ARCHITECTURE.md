# API_ARCHITECTURE.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL
**Source:** Step 11 as approved with its corrections (Q-ORT-2 not resolved through the API; continuing a case is not a new work item; no automatic Original Sales Employee; composite registration policy; no `/api/unassigned/*`; appointment-status wording), plus Step 10 (Notifications and background jobs) as approved with its three corrections and the Step 14 J7 correction, since the jobs and the inbox are API-visible behavior. Database objects are defined in DATABASE_DESIGN.md; authorization in SECURITY_DESIGN.md; ledger behavior in FINANCE_DESIGN.md.

Labels: **CONFIRMED** = user's rule. **TECHNICAL** = engineering implementation. **DEFAULT** = veto-able value. **OPEN** = undecided; no endpoint exists.

---

## PART A: API (Step 11)

### A1. Conventions (TECHNICAL)

| Topic | Decision |
|---|---|
| Style | JSON over HTTPS. Resource routes for data; a `POST` sub-resource for each state transition (e.g. `/visits/{id}/finalize`). |
| Audience prefixes | `/api/auth`, `/api/me`, `/api/doctor`, `/api/sales`, `/api/manager`, `/api/public`, `/health`. A prefix is organization only; **the permission key is the check**. |
| Manager access | The Manager holds the keys of both audiences, so may call `/api/doctor/*` and `/api/sales/*` endpoints and receives **that audience's DTO**, never a wider one. Full Manager views live under `/api/manager/*`. "Own" endpoints (`/me/work-items`, `/me/payroll`) need a staff identity, so the Manager gets 404. |
| Identifiers | GUIDs in routes; `patient_code` is for display and search only |
| Versioning | No version segment in V1 (SPA and API deploy together) |
| JSON | camelCase; enums as strings; **unknown request members are rejected (400)** |
| **Money** | **Strings with two decimals** (`"1000.00"`), parsed to `decimal` on the server and a decimal library in the SPA. No float arithmetic anywhere. |
| Dates/times | Instants ISO 8601 UTC; calendar dates `YYYY-MM-DD` in clinic-local time; the clinic timezone comes from `GET /api/me` |
| Content type | `application/json` only; PDFs/CSVs are downloads; request limit 1 MB (no uploads exist in V1) |
| OpenAPI | **One document per audience** (`doctor`, `sales`, `manager`, `me/auth`); TypeScript types generated per audience in CI, so the Doctor client has no type that can hold a contact field. Swagger UI on in Development and Staging, **off in Production**. |
| Headers | `Cache-Control: no-store` on every authenticated response plus the security headers |

### A2. Authentication and authorization on the API

Four layers apply to every request (session, permission key, resource scope, audience DTO).

**Anonymous endpoints (the complete allow-list, six endpoints, enforced by the startup test):**

| Endpoint | Protection |
|---|---|
| `GET /api/auth/csrf` | Rate limited |
| `POST /api/auth/login` | CSRF, per-IP limit, lockout |
| `POST /api/auth/password-reset/complete` | Single-use token, rate limited, generic responses |
| `GET /health/live`, `GET /health/ready` | No detail in body. `ready` checks database connectivity and **schema compatibility** (required − applied migrations; see DEPLOYMENT_ARCHITECTURE.md). `ready` is not routed publicly. |
| `POST /api/public/feedback/{token}` | 256-bit token, hashed, single-use, rate limited, CORS only for the clinic website. **Present only if the token-based feedback option is chosen (OPEN, Q-FDB-2).** |

**Authenticated without a permission key** (`[AuthenticatedOnly]`): `GET /api/me`, `POST /api/auth/logout`, `POST /api/auth/change-password`.

**Account recovery initiation is not exposed** until the account-recovery workflow is confirmed (OPEN). Reset-token issuance exists as an Identity service usable by the operator command.

**Unresolved actions have no mapped route.** There is no `/api/unassigned/*`. The keys `Orthodontics.Terms.Set` and `Finance.Discount.Set` and their handlers may exist internally, but no route maps them; a route is created only when the actor is confirmed. **Startup test:** every mapped route must carry a key assigned to at least one role, or be `[AuthenticatedOnly]` or anonymous; a mapped route whose key has no role fails the build.

### A3. DTO design (TECHNICAL)

Rules: a DTO name carries its audience (`DoctorPatientDto`, `SalesPatientDto`, `ManagerPatientDto`); every response is a `Select` projection or purpose-written mapper (**entities are never serialized**); every request is a dedicated command record (binding to an entity or a dictionary is forbidden by analyzer); contact-bearing properties carry `[ContactData]`; architecture tests scan every Doctor DTO **and the generated Doctor OpenAPI document** for `phone`, `whatsapp`, `email`, `address`, `crmNotes`, `salesNotes`, `originalSalesEmployee`.

| DTO | Fields | Deliberately absent |
|---|---|---|
| `DoctorPatientListItem` | id, patientCode, fullName, ageDisplay | everything else |
| `DoctorPatientDto` | id, patientCode, fullName, ageDisplay (DOB only while `doctor_dob_visible` is on), medicalHistory, chronicDiseases, previousSurgeries, chiefComplaint, isActive, registeredAt, currentDoctor {id, name} | phone, whatsapp, email, address, CRM notes, original Sales employee |
| `DoctorAppointmentDto` | id, patient {id, code, name}, startAt, durationMinutes, status, reason, kind, linkedCase {procedureName, status} | `sales_notes`, contact data |
| `DoctorReferralDto` | id, referralType, reason, notes, status, createdAt | Sales follow-up work, original Sales employee |
| `DoctorOwnWorkDto` (one per work item) | workItemId, caseId, procedureName, finalPrice, collected, remaining, doctorPercentage, doctorShare, clinicShare, lab, `completionState` | other Doctors' items; any case, patient or clinic total. Held allocations: a "share pending" marker with no amount. |
| `SalesPatientDto` | id, patientCode, fullName, ageDisplay, phone, whatsapp, email, address, crmNotes, originalSalesEmployee {id, name} (nullable), isActive, registeredAt, currentDoctor {id, name} | medical history, chronic diseases, surgeries, chief complaint |
| `SalesClinicalSummaryDto` | per case: procedureName, treatingDoctor {id, name}, status, referralDestination, followUpInstructions, price, paid, remaining, ortho installments | notes, teeth/details, steps, measurements, medications, Doctor percentage, shares, lab |
| `SalesAppointmentDto` | as Doctor DTO plus salesNotes and patient contact | clinical content |
| `SalesReferralDto` | id, patient, referralType, status, followUpInstructions; `reason` only if `referral_reason_visible_to_sales` is on | notes, clinical content |
| `SalesDoctorLookupItem` | id, fullName, specialty | **current percentage** and every other staff column |
| `PaymentConfirmationDto` | paymentId, amount, method, paidAt, allocations, resulting outstanding | **Data only.** A printable receipt is undefined (Q-DOC-1); no print endpoint exists. |
| `AuditEntryDto` | Manager only | |

### A4. Validation (TECHNICAL)

Order: (1) authorization first (failures return before body validation); (2) request validation (FluentValidation: types, lengths, ranges, required fields); (3) domain validation in the handler, including **catalog options** (`details` validated against `procedure_types.option_definition`): rejects `composite_zone` on a GIC filling or missing on Composite; an extraction type on a Pediatric Extraction; Basic/High Quality on anything but Laser Bleaching/Whitening; a missing appliance type when the ortho area is Appliance; canal number outside 1–5; healing period outside 1–6 months; Sutures and Bone Removal (those fields do not exist). Money must be positive with at most two decimals. A discount is a fixed EGP amount; percentage discounts are not accepted. Validation errors name the field and a code, never the value.

### A5. Lists (TECHNICAL)

`?page=1&pageSize=25` (default 25, max 100; patient search max 50), response `{ items, page, pageSize, totalCount }`. Audit and notifications use cursor pagination. Sorting: `?sort=field|-field` against a per-endpoint whitelist with a stable `id` tie-breaker; unknown fields 400. Filtering: explicit typed parameters; no filter expression language. **Any search involving names or phone numbers is a POST with a body.** Date ranges are clinic-local inclusive dates converted to half-open UTC ranges.

### A6. Errors (TECHNICAL)

Every error is `application/problem+json` (RFC 7807) with `type`, `title`, `status`, a stable machine `code`, `traceId`, and for validation `errors: { field: [codes] }`. Messages are generic and never echo input, SQL or stack traces.

| Status | Used for |
|---|---|
| 400 | Malformed JSON, unknown members, validation failures |
| 401 | No/expired session, deactivated user, rotated stamp |
| 403 | Role lacks the permission key |
| 404 | A scoped resource that is not the caller's, or does not exist |
| 409 | Duplicate/conflicting state: double-booked slot, idempotency payload mismatch, second fixed lab on a case, appointment already rescheduled |
| 412 / 428 | `If-Match` mismatch / missing |
| 422 | Domain rule refuses: visit already finalized, charge already exists for the work item, allocation exceeds outstanding, sign mismatch |
| 429 | Rate limit with `Retry-After` |
| 500 | Unexpected; only `traceId` returned |

Representative codes: `visit.finalized`, `appointment.slot_taken`, `payment.idempotency_mismatch`, `payment.exceeds_outstanding`, `fixed_lab.already_exists`, `workitem.charge_exists`, `catalog.option_invalid`, `concurrency.stale`. The SPA branches on `code`, never on message text.

### A7. Concurrency and idempotency (TECHNICAL, CONFIRMED requirements)

- **Optimistic concurrency with ETag / If-Match**, derived from `xmin`. `PUT`, `PATCH` and state-changing `POST` sub-resources on editable resources **require** `If-Match` (patient identity/clinical profile/contact; visits; prescriptions; work-item price; leads; follow-ups; availability weeks; appointment transitions; referral status; settings).
- State transitions are idempotent when already in the target state. Visit finalize is idempotent.
- The database is the final guard: partial unique index (double booking), exclusion constraint (overlapping availability), unique constraints (payments, fixed lab, work-item charges). Violations map to 409 with specific codes.
- **`POST /api/sales/payments` requires an `Idempotency-Key` header (GUID)**, stored in `payments.idempotency_key` (unique). Replay with the same payload returns the original response; **same key with a different payload returns 409** (compared against stored rows; no hash column). Appointment booking/rescheduling rely on natural-key uniqueness plus `If-Match`; `previous_appointment_id` uniqueness means two reschedules of one appointment cannot both succeed. No generic idempotency-key table exists.
- Financial writes use the locking order in FINANCE_DESIGN.md (charge rows `FOR UPDATE` in id order).

### A8. Endpoint catalog (TECHNICAL)

#### Auth, me, public

| Route | Key | Notes |
|---|---|---|
| `GET /api/me` | authenticated | displayName, role, permission keys (menu only), clinic timezone, mustChangePassword |
| `GET /api/me/notifications`, `…/unread-count`, `PATCH …/{id}/read`, `POST …/read-all` | `Notifications.Read.Own` | **Sales and Manager only; a Doctor gets 403.** `recipient_user_id = principal` |
| `GET /api/{doctor,sales,manager}/lookups` | any key of that audience | Role-specific reference data |

#### Doctor (`/api/doctor`)

| Route | Key | Scope / notes |
|---|---|---|
| `POST /patients/search` | `Patients.Search.Clinical` | ID or name only; no phone matching |
| `GET /patients/{id}` | `Patients.Clinical.Read` | All patients; **audited as record access** |
| `GET /patients/{id}/timeline` | `Patients.Timeline.Clinical` | Payment entries limited to the Doctor's own work |
| `PUT /patients/{id}/clinical-profile` | `Patients.Clinical.Write` | Doctor has an editable visit for this patient; `If-Match` |
| `GET /patients/{id}/visits`, `GET /visits/{id}`, `GET /treatment-cases/{id}` | `Clinical.Visits.Read` | Any Doctor's history readable |
| `GET /appointments` | `Scheduling.Appointments.View.Own` | `doctor_id = principal`. **No `/queue` route** (removed in v1.1; Doctor queue belongs to Q-APP-4) |
| `GET /schedule?weekStart=` | `Scheduling.Availability.ViewOwn` | Published weeks only |
| `POST /appointments/follow-up` | `Scheduling.Appointments.CreateFollowUp` | visitId, startAt, reason; own visit; booked **with the authoring Doctor** (DEFAULT) |
| `POST /appointments/{id}/reschedule` | `Scheduling.Appointments.RescheduleOwnFollowUp` | Only a follow-up the Doctor created; `If-Match` |
| `POST /visits` | `Clinical.Visits.Write` | patientId, optional appointmentId; Doctor = principal; a linked Scheduled or Confirmed appointment moves directly to In Treatment (no arrival step) |
| `PUT /visits/{id}` | `Clinical.Visits.Write` | Own, Draft/InProgress; `If-Match` |
| `POST /visits/{id}/finalize` | `Clinical.Visits.Finalize` | Own; idempotent; linked appointment → Completed; creates the feedback request |
| `POST /visits/{visitId}/treatments` | `Clinical.Treatments.Write` | See "Treatments and work items" below |
| `PUT /visits/{visitId}/treatments/{recordId}` | `Clinical.Treatments.Write` | Own editable visit and own work item |
| `POST /visits/{visitId}/cases/{caseId}/steps/{stepCode}/complete` | `Clinical.Treatments.Write` | Root canal steps; persists across visits |
| `PUT /visits/{visitId}/cases/{caseId}/canal-measurements` | `Clinical.Treatments.Write` | Canals 1–5, mm |
| `PUT /visits/{visitId}/cases/{caseId}/implant` | `Clinical.Treatments.Write` | Graft, membrane, procedure date, healing 1–6 months |
| `PUT /work-items/{id}/price` | `Clinical.WorkItems.EnterPrice` | Own work item; 422 once a charge exists; `If-Match` |
| `POST /fixed-lab/{caseId}` | `Finance.FixedLab.Enter` | **Initial** amount only; 409 if the case already has one |
| `POST /visits/{visitId}/orthodontic-cases` | `Orthodontics.Cases.Create` | Area, appliance type if Appliance. Total, deposit and months are **not accepted** (OPEN). Creates the case's one work item; no charge. |
| `GET /orthodontic-cases/{id}` | `Orthodontics.Cases.Read` | Schedule/payment status for all Doctors; shares/percentages only on own work |
| `GET /patients/{id}/prescriptions`, `GET /prescriptions/{id}` | `Prescriptions.Read` | All prescriptions readable |
| `GET /prescriptions/{id}/print` | `Prescriptions.Print` | PDF from `PrescriptionPrintModel`; audited |
| `POST /visits/{visitId}/prescriptions` | `Prescriptions.Write` | Own editable visit |
| `PUT /prescriptions/{id}` | `Prescriptions.Write` | Owning visit is the Doctor's and not finalized, else 404; items soft-removed; `If-Match` |
| `POST /visits/{visitId}/referrals`, `GET /patients/{id}/referrals` | `Crm.Referrals.Create`, `Crm.Referrals.Read.Clinical` | Own visit for creating |
| `GET /me/work-items`, `GET /me/work-items/{id}` | `Finance.OwnWork.Read` | `performing_doctor_id = principal` |
| `GET /me/payroll` | `Payroll.Doctor.ReadOwn` | Own rows only |

**Treatments and work items.** `POST /visits/{visitId}/treatments` (own editable visit). Body: `caseId` (absent when starting a new case), `procedureTypeId` (new cases only), teeth, details, `separatelyPricedWork` (boolean, default `false`).

| Situation | Result |
|---|---|
| Starting a new case | The case, a treatment record, and the case's first work item for the signed-in Doctor, with the percentage snapshot read at that moment |
| Continuing an existing case (`separatelyPricedWork = false`) | A treatment record only. **No work item, snapshot or charge is created.** |
| Continuing with `separatelyPricedWork = true` | A new work item for the signed-in Doctor with a fresh snapshot (how Dr B's separately priced work joins Dr A's case). **Refused with 422 on an orthodontic case.** |

A Doctor continuing another Doctor's case without declaring separately priced work gets **no financial ownership**; whether such work should share revenue is not defined, so nothing is created. The price is entered later through `PUT /work-items/{id}/price`.

#### Sales (`/api/sales`)

| Route | Key | Notes |
|---|---|---|
| `POST /patients/search` | `Patients.Search.Contact` | ID, name or phone |
| **`POST /patients`** | **`Patients.Identity.Write` AND `Patients.Contact.Write`** | Name, DOB **or** recorded age, contact fields. **No medical fields. No Original Sales Employee field** (unknown members → 400). |
| `GET /patients/{id}`, `PUT /patients/{id}/identity`, `PUT /patients/{id}/contact` | `Patients.Contact.Read`; `Patients.Identity.Write`; `Patients.Contact.Write` | `If-Match` on writes; contact changes audited with restricted values |
| `GET /patients/{id}/clinical-summary` | `Clinical.SalesSummary.Read` | Whitelist only |
| `GET /patients/{id}/timeline` | `Patients.Timeline.Operational` | No prescription or visit content |
| `GET /patients/{id}/balance`, `GET /patients/{id}/payments` | `Finance.PatientBalance.Read` | Charges, Billed, Collected, Outstanding. No shares/percentages/lab. |
| `GET /outstanding-balances` | `Finance.PatientBalance.Read` | Feeds dashboard and follow-ups |
| `GET /doctors`, `GET /availability/slots?doctorId&from&to` | `Scheduling.Availability.View` | `SalesDoctorLookupItem`; published weeks only |
| `GET /appointments`, `POST /appointments` | `Scheduling.Appointments.View.All`, `.Book` | 409 on a taken slot |
| `POST /appointments/{id}/reschedule` | `Scheduling.Appointments.Reschedule` | New appointment, old one Rescheduled, reason stored; `If-Match` |
| `GET/POST /leads`, `GET/PUT /leads/{id}`, `POST /leads/{id}/convert` | `Crm.Leads.Manage` | **Conversion preserves the lead's Original Sales Employee** |
| `GET /referrals`, `GET /referrals/{id}`, `PATCH /referrals/{id}/status` | `Crm.Referrals.Read.Sales`, `.UpdateStatus` | `SalesReferralDto` |
| `GET/POST /follow-ups`, `PUT /follow-ups/{id}`, `POST /follow-ups/{id}/contact-attempts` | `Crm.FollowUps.Manage` | Attempts append-only |
| **`POST /payments`** | `Finance.Payments.Record` | **`Idempotency-Key` required.** Explicit allocations to **charges and amounts only**, for ortho charges as for any other. **No installment is named or consumed.** |
| `GET /orthodontic-cases/{id}` | `Orthodontics.Cases.Read` | See "Ortho reads" below |
| `GET /feedback-requests?status=` | `Feedback.Queue.Manage` | The Sales queue |
| `POST /feedback-requests/{id}/link`, `POST /feedback-requests/{id}/mark-sent` | `Feedback.Queue.Manage` | V1 manual flow. `link` issues a fresh token and returns the URL once (earlier token stops working); `mark-sent` records channel, sender, date. |
| `GET /dashboard` | `Reports.Dashboard.Sales` | The eight confirmed dashboard items (the former "waiting patients" item was removed in v1.1) |
| `GET /me/payroll` | `Payroll.Sales.ReadOwn` | `sales_employee_id = principal` |

**Ortho reads (all audiences).** Each installment shows due date and amount due. The response adds charge-level Collected and Outstanding and an "unapplied ortho collections" figure. Per-installment amount paid and status (Partially Paid, Paid, Overdue) are **not computed**; each installment carries `applicationState: "Pending"`. **Q-ORT-2 is OPEN and is not resolved through the API: no `installment_payments` row is created by any route, job or handler.** The confirmed installment fields (ORT-3) and statuses (ORT-4) cannot be fully populated until the rule exists; nothing is invented to fill the gap.

#### Manager (`/api/manager`)

| Group | Routes | Keys |
|---|---|---|
| Dashboard | `GET /dashboard?date=` (defaults to today, clinic-local; no manual generation) | `Reports.Dashboard.Manager` |
| Patients | `POST /patients/search`; `GET /patients/{id}` (`ManagerPatientDto`, full); `GET …/timeline`; `GET …/assignments`; `POST …/reassign` (patient, new Doctor, reason; audited; `If-Match`); **`POST /patients`** | `Patients.Search.*`, `Patients.Clinical.Read`, `Patients.Contact.Read`, `Patients.Reassign`; registration needs **both** `Patients.Identity.Write` and `Patients.Contact.Write` |
| Scheduling | `GET/PUT /doctors/{id}/availability/{weekStart}` (draft), `POST …/publish`, `GET /appointments`, `POST /appointments/{id}/reschedule`, **`POST /appointments/{id}/status`** | `Scheduling.Availability.Publish`, `.View`, `Scheduling.Appointments.View.All`, `.Reschedule`, `Scheduling.Appointments.UpdateStatus` |
| Finance | `GET /finance/summary?from&to` (Billed, Collected, Outstanding, by method, discounts, consultation fees, Doctor Revenue Share, lab, Clinic Gross Revenue, **Pending completion**, **Pending Doctor Attribution**, estimated result and notices), `GET /finance/pending-completion`, `GET /finance/pending-attribution`, `GET /finance/charges`, `GET /finance/payments` | `Finance.Management.Read` |
| Lab | `GET/PUT /settings/lab-percentage` (inserts a history row), `PUT /fixed-lab/{caseId}` (new revision, **reason required**, audited) | `Finance.LabPercentage.Configure`, `Finance.FixedLab.Edit` |
| Payments | The Manager records payments through `POST /api/sales/payments` | `Finance.Payments.Record` |
| Staff | `GET/POST /staff/doctors`, `GET/POST /staff/sales-employees`, `PUT /doctors/{id}/percentage`, `PUT /sales-employees/{id}/commission`, `PUT /sales-employees/{id}/base-salary`, `POST /staff/{id}/deactivate`, `POST /staff/{id}/reactivate` | `Staff.Manage`, `Staff.DoctorPercentage.Configure`, `Staff.SalesCommission.Configure`, `Payroll.Sales.ManageAll` |
| Assistants *(v1.1; all under `/api/manager`, so the full paths are `/api/manager/staff/assistants…` and `/api/manager/payroll/assistants…`; Manager only; no Assistant account or login; no Assistant audience, no OpenAPI document of their own)* | `GET/POST /staff/assistants`, `PUT /staff/assistants/{id}` (full name / basic information; `If-Match`), `POST /staff/assistants/{id}/deactivate`, `POST …/reactivate` | `Staff.Manage` |
| Assistant salary *(v1.1)* | `PUT /staff/assistants/{id}/salary` (new current salary, reason; audited; `If-Match`; never alters an existing payroll snapshot) | `Staff.AssistantSalary.Configure` |
| Users | `PUT /users/{id}/role`, `POST /users/{id}/force-sign-out` | `Identity.Users.Manage` |
| Expenses | `GET/POST /expenses`, `PUT /expenses/{id}`, `POST /expenses/{id}/adjustments`, `GET/POST/PUT /expense-categories` | `Expenses.Read`, `Expenses.Manage` |
| Assistant payroll *(v1.1)* | `GET /payroll/assistants` (list, per-Assistant history, filters), `GET /payroll/assistants/{id}`, `POST /payroll/assistants/drafts` (assistant, period; amount comes from the current salary, never from the request), `POST /payroll/assistants/{id}/finalize`, `POST …/{id}/mark-paid`. No body field can set `final_amount`; no adjustments route exists. Finalize is refused for a payroll that needs partial-period calculation (`payroll.assistant_partial_period_unsupported`, Q-PAY-ASST-2). Overlap → 409 `payroll.assistant_period_overlap`. | `Payroll.Assistant.ManageAll` |
| Payroll | `GET /payroll/doctors`, `POST /payroll/doctors/drafts` (doctor, period), `POST …/{id}/adjustments`, `POST …/{id}/finalize`, `POST …/{id}/mark-paid`, and the same set for `/payroll/sales` | `Payroll.Doctor.ManageAll`, `Payroll.Sales.ManageAll` |
| Reports | `GET /reports/financial` (v1.1: shows **Assistant Salaries** as its own line, per-period total, payroll split, and the PENDING / INCOMPLETE state under Q-PAY-ASST-1), `/doctor-performance`, `/sales-performance`, `/orthodontic-receivables`, `/expenses` (daily, weekly, monthly or custom `from`/`to`), each with `GET …/export` (CSV/PDF) | `Reports.Financial`, `Reports.Export` |
| Feedback | `GET /feedback` | `Feedback.Results.Read` |
| Audit | `GET /audit?…` (cursor), `GET /audit/{id}` | `Audit.Read` (reading is audited) |
| Settings | `GET/PUT /settings` (timezone, referral-reason visibility, DOB visibility, feedback expiry, default appointment length, clinic header details), `GET/PUT /payment-methods` | `Configuration.Manage` |

**Registration and the Original Sales Employee.** `POST /api/sales/patients` takes no Original Sales Employee field. `POST /api/manager/patients` accepts an **optional** `originalSalesEmployeeId` (never required), accepted only because the Manager already manages commission configuration; who may assign or change it later for a walk-in is **OPEN** and no change route exists. Direct registration leaves `original_sales_employee_id` NULL; lead conversion carries the lead's value (CONFIRMED). Reports show a count of **patients without an Original Sales Employee**. Authorization is the composite `[HasAllPermissions(...)]` policy; the generated role-matrix test understands composite policies.

**Manager clinical editing.** The Manager reaches Doctor clinical write endpoints (the "own" requirement is waived per SECURITY_DESIGN.md) to edit **non-finalized** records only; no Manager-initiated visit workflow exists. The Manager can never overwrite a finalized or ledger record (G1 and insert-only grants).

**Appointment statuses.** Cancel, Confirm and No Show changes go through `POST /manager/appointments/{id}/status` with `Scheduling.Appointments.UpdateStatus`. **Manager-only is the current seed, not a clinic rule.** Whether Sales may also perform these status changes is unresolved; until defined, Sales cannot cancel or mark a no-show. In Treatment (the Doctor starts the linked visit from a Scheduled or Confirmed appointment), Completed (visit finalize) and Rescheduled follow confirmed actions. There is no check-in route and no arrival field in any request or response (no `arrivedAt`, no `checkedInBy`), and the appointment status enum has exactly seven values (Scheduled, Confirmed, InTreatment, Completed, Cancelled, NoShow, Rescheduled).

### A9. Endpoints deliberately NOT created

| Not created | Why |
|---|---|
| Refunds, payment reversals, charge adjustments | Workflow undefined (Q-PAY-1); internal tested primitives only |
| Charge establishment and consultation charge creation | W1 (OPEN) |
| Discount setting; ortho terms setting | Actor unassigned; no route is mapped |
| Installment application and installment selection | Q-ORT-2 (OPEN) |
| Fixed-lab / general-lab Lab-Clinic completion triggers | Q-FIN-1 and W2 (OPEN); completions are produced only inside payment recording |
| Doctor attribution resolution for held allocations | Q-FIN-9 (OPEN) |
| Sales commission calculation and Sales payroll finalization | Q-FIN-2 (OPEN). Draft creation, base salary, bonus and deductions work; the finalize action stays disabled behind a configuration flag. |
| Expense finalization | What triggers it is undefined, so `finalized_at` is never set; expenses stay audited-editable; adjustments available |
| Daily closing, printable receipts/invoices, consent forms | Undefined or open (Q-DOC-1, Q-DOC-2) |
| Doctor notifications and dashboard | Not confirmed |
| Prescription post-finalization edit | Q-CLN-1 (OPEN); G1 blocks it for everyone |
| Manager override of a full schedule; overlap-based booking | Q-APP-2, Q-APP-1 (OPEN) |
| Lead reassignment from an inactive owner | Q-EMP-1 (OPEN) |
| Walk-in Original Sales Employee assignment or change route | Not defined |
| Account-recovery initiation endpoint | Not confirmed |
| Any `/api/unassigned/*` route | Removed; unresolved actions have no mapped route |
| Inventory, X-ray | Out of scope |

### A10. Cross-cutting behavior

Rate limits: auth 10/min/IP, general 300/min/user, patient search 60/min/user (DEFAULT). Audit written in the same transaction; record access, exports and PDF prints write their own entries. Downloads: `Content-Disposition: attachment`, no patient names in file names, CSV formula neutralization, no server-side storage. No request/response bodies logged on patient, contact, finance or payroll routes; route templates are logged, never raw URLs.

### A11. API tests (in addition to the security and finance tests)

*(v1.1 adds tests 30–32 at the end of this list.)*

| # | Test |
|---|---|
| 1 | Generated role matrix over every **mapped** route and role, including composite policies |
| 2 | Startup test: every mapped route has a key assigned to at least one role, `[AuthenticatedOnly]`, or is on the six-item anonymous list |
| 3 | The generated Doctor OpenAPI document contains no contact property name; the Sales document contains no clinical property name |
| 4 | Canary scans over **every** Doctor and Sales response, including error bodies |
| 5 | Unknown request members → 400; no route binds to an entity |
| 6 | Own routes accept no `doctorId`/`salesEmployeeId`; Doctor A requesting Doctor B's work item → 404 |
| 7 | `If-Match` required on every editable resource (428 without, 412 stale); concurrent edits produce one success |
| 8 | Payments idempotency matrix: same key + payload → original response; same key + different payload → 409; concurrent same-key submissions create one payment; missing key → 400 |
| 9 | Double-booking: two concurrent bookings → one 201, one 409; two concurrent reschedules → one success |
| 10 | Visit finalize repeated is idempotent; finalized edits return 422 (own) or 404 (another Doctor's) |
| 11 | Catalog validation: every invalid combination in A4 → `catalog.option_invalid` |
| 12 | Money: amounts are strings, three decimals rejected, percentage-style discount rejected |
| 13 | Pagination bounds, sort whitelist (unknown field 400), search endpoints reject GET with PII |
| 14 | Error shape: every error is problem+json, no echoed input, no stack trace |
| 15 | Manager blocked on `/me/*` own routes (404); may call permitted `/api/doctor/*` routes and receives Doctor-shaped DTOs |
| 16 | Doctor and Sales get 403 on every `/api/manager/*` route; a Doctor gets 403 on `/api/me/notifications` |
| 17 | Sales payroll finalize disabled while the Q-FIN-2 flag is off; Doctor payroll draft/finalize works for fixed-lab earnings |
| 18 | An export writes an audit entry; a PDF text extraction contains no contact canary |
| 19 | Contract drift: generated TypeScript clients in CI match the OpenAPI documents |
| 20 | An ortho payment can be allocated to the ortho charge; Collected and Outstanding update |
| 21 | **No installment application is created** by any route, job or handler while Q-ORT-2 is open; `installment_payments` stays empty; installment reads show `applicationState: "Pending"` |
| 22 | Continuing the same multi-visit case adds treatment records but creates no second work item, snapshot or charge |
| 23 | `separatelyPricedWork = true` by another Doctor creates a second work item with that Doctor's own snapshot; refused on an ortho case |
| 24 | Direct registration by Sales leaves `original_sales_employee_id` null; the Sales body rejects the field with 400 |
| 25 | `POST /manager/patients` stores the owner only when supplied; omitting it is accepted |
| 26 | Lead conversion preserves the lead's Original Sales Employee |
| 27 | Registration with only one of the two keys → 403 |
| 28 | In the Production configuration **no `/api/unassigned/*` route is mapped** |
| 29 | J2 reminds installments only while the ortho charge has Outstanding > 0 |
| 30 *(v1.1)* | OpenAPI expectation: the Manager OpenAPI document contains the Assistant staff and payroll paths and the Assistant Payroll Expense fields of `/reports/financial`; there is no Assistant OpenAPI document. Assistant routes: Doctor and Sales get 403 on every one; `/api/doctor/*` and `/api/sales/*` OpenAPI documents contain no Assistant path or schema; no Assistant authentication, notification, dashboard, `/api/me` or `/api/public` route exists |
| 31 *(v1.1)* | `PUT …/salary` needs `Staff.AssistantSalary.Configure` (Staff.Manage alone → 403); the change is audited; a payroll created earlier is unchanged and a payroll drafted afterwards uses the new salary; a request body carrying `final_amount` or `salary_snapshot` is rejected with 400 |
| 32 *(v1.1)* | Assistant payroll: draft → finalize → mark-paid with `If-Match`; a second draft for an overlapping period → 409; finalized payroll cannot be changed; each transition is audited; finalizing a partial-period payroll is refused (Q-PAY-ASST-2) |
| 33 *(v1.1)* | The appointment API rejects `Arrived` and `Waiting` as a status (400, `validation.*`) on every route that accepts a status |
| 34 *(v1.1)* | No check-in route is mapped in any audience, and no `/api/doctor/queue` route exists while Q-APP-4 is OPEN |
| 35 *(v1.1)* | A Doctor starting a linked visit moves an eligible Scheduled or Confirmed appointment directly to In Treatment; an appointment in any other status is not changed by visit start |
| 36 *(v1.1)* | Every generated OpenAPI document contains no `Arrived` / `Waiting` appointment enum value, no `arrivedAt` / `checkedInBy` property and no check-in or queue path |

### A12. Small technical defaults (veto-able)

Money as strings; no URL version segment; strict unknown-member rejection; patient search 60/min; Doctor follow-up booked with the authoring Doctor (if a Doctor must book with a different Doctor, one optional field is added); Sales payroll finalize flag off until Q-FIN-2; account-recovery initiation not exposed. **Consequence to know:** consultation fees cannot be recorded until W1 is answered, because the source of a consultation charge is undefined; the money model is ready and only the call site is missing.

---

## PART B: Notifications and background jobs (Step 10)

### B1. Confirmed requirements implemented

A reusable Notification module whose initial recipients are internal clinic users, especially Sales. Seven types: Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral, Outstanding Payment Follow-Up, Pending Follow-Up, Feedback Request Pending. Record fields: type, recipient user and role, related patient, entity type and ID, scheduled/sent/read time, status, retry count. Processing is idempotent, retry-safe, timezone-aware and duplicate-resistant; the clinic timezone is configurable. Appointments store reminder scheduled-at, sent-at and status (on `notifications`). **Doctors have no notification inbox in V1; no notification targets a Doctor.** Feedback links are sent manually by Sales in V1; external channels (WhatsApp, SMS, email) are future work behind an abstraction. No Inventory jobs, no X-ray jobs.

### B2. Module design (TECHNICAL)

`Notifications.Contracts` (Level 2): `EnqueueForRole`, `EnqueueForUser`, `INotificationSourceValidator`, `INotificationRenderer`, `INotificationChannel`. Planner jobs live in the **owning module** (Scheduling, Orthodontics, Crm, Feedback). Source modules implement the Notifications-defined ports and register them in DI; the dispatcher finds them at runtime (no upward compile-time dependency). `InAppChannel` is the only V1 channel ("delivered" = the row becomes visible in the inbox). Password-reset messages use a separate system-message sender and are **never** stored in `notifications`.

### B3. Recipients (TECHNICAL, literal reading)

| Type | Recipients |
|---|---|
| Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral, Feedback Request Pending | All active Sales / Reception users |
| Pending Follow-Up, Outstanding Payment Follow-Up | The follow-up's assigned Sales employee |

One row per recipient (read-state per person). The Manager has read access but no confirmed type targets the Manager. **Inactive owner:** nothing is sent; who inherits their follow-ups is Q-EMP-1 (OPEN). The same `staff.v_sales_employee_activity` directory answers this in J3, the dispatcher and J7.

### B4. Lifecycle (TECHNICAL)

`Pending → Sent` (read_at set on open; status stays Sent); `Pending → Cancelled` (source no longer applicable, or recipient inactive); `Failed` (delivery error) `→ Pending` via retry while `retry_count < 5`. Inbox shows `Sent` rows for the signed-in user only. Already-sent reminders are never rewritten; the inbox renders them live from the source entity. Rendering is at display time under the viewer's permissions by the source module's `INotificationRenderer`, returning only the Sales whitelist.

### B5. Idempotency, timezone (TECHNICAL)

Dedupe key (unique): `{type}:{entity_id}:{clinic-local target date}:{recipient_user_id}`; inserts use `ON CONFLICT (dedupe_key) DO NOTHING`; planners use `DisableConcurrentExecution`. A rescheduled appointment has a new id, hence a new key; a new Sales user gets tomorrow's reminders on the next run; a follow-up due-date change notifies again. Time goes through one `IClinicClock` reading `configuration.settings.clinic_timezone` (IANA tz via NodaTime; DEFAULT `Africa/Cairo`; missing/invalid → jobs fail loudly and guess nothing). "Tomorrow" is the clinic-local calendar date; appointment queries use a half-open UTC range built from the local day (23/25-hour days work); installments compare clinic-local dates. A timezone change is audited.

### B6. Jobs (TECHNICAL)

All jobs run only in the Worker (Hangfire, PostgreSQL storage). Arguments are ids only. Every job is safe to re-run.

| Job | Frequency | Selects / behavior |
|---|---|---|
| **J1 Appointment reminders** (Scheduling) | 15 min | Appointments on tomorrow's clinic-local date, status not Cancelled/Rescheduled; `kind = Standard` → Appointment Tomorrow; `ImplantFollowUp` → Implant Follow-Up Tomorrow |
| **J2 Ortho installment reminders** (Orthodontics) | 15 min | `due_date = tomorrow` **and the installment's ortho charge has Outstanding > 0** (a fully paid ortho charge gets no reminder; a patient who prepaid some installments may still be reminded, and the notification shows the charge-level balance) |
| **J3 Follow-up due** (Crm) | 15 min | `due_date ≤ today` and the follow-up is open; `follow_up_type = RemainingBalance` → Outstanding Payment Follow-Up; others → Pending Follow-Up. Nothing sent when the owner is inactive. Binds to the follow-up open predicate once the status list is confirmed (placeholder). |
| **J4 Dispatcher** (Notifications) | every minute | See below |
| **J5 Retry** (Notifications) | 5 min | `UPDATE notifications SET status='Pending' WHERE status='Failed' AND retry_count < 5`, in batches. `retry_count` counts **failed attempts** and is incremented by the dispatcher at failure time. After the 5th failure it stays Failed and the exhausted-retries alert fires. Same row, same `dedupe_key`; fully re-validated through the dispatcher path. |
| **J6 Feedback expiry** (Feedback) | hourly | Pending/Sent requests with `expires_at ≤ now` → Expired. **Inert while `expires_at` is unset (Q-FDB-1 OPEN).** |
| **J7 Operations health** (Notifications, Worker) | 5 min | Job freshness/Worker heartbeat; dispatcher lag; exhausted retries; Hangfire failed-job count; count of follow-ups assigned to an inactive Sales employee (produced by **Crm** through a Notifications-defined probe port using `Staff.Contracts.IActiveSalesEmployeeDirectory`; a count with no names or ids, shown only in the weekly digest because Q-EMP-1 is OPEN). Pings success, or failure with a fixed message and counts; never names, ids or text. |

**Reminder timing rule (calendar-date semantics).** "Tomorrow" is a clinic-local calendar date, not an exact 24-hour deadline. On every planner run, for each active recipient, enqueue a reminder for every item that (1) falls on tomorrow's clinic-local date, (2) is still reminder-eligible, and (3) has no notification yet for that recipient (the dedupe key decides). When the item was created or rescheduled is irrelevant.

| Situation | Result |
|---|---|
| Appointment created today for tomorrow | Reminded on the next planner run (within 15 min) |
| Appointment created on its own date | Never "tomorrow" after creation; no reminder |
| Rescheduled today to tomorrow | New appointment, new key; reminded on the next run; the old pending one is Cancelled |
| Doctor-created follow-up for tomorrow | Reminded on the next run |
| Ortho schedule generated today with an installment due tomorrow | Reminded on the next J2 run; one generated on the due date is not |

`scheduled_for` = the later of the reminder-availability instant (start of the previous clinic-local day, DEFAULT) and the insert moment. Edge: an appointment for tomorrow created at 23:55 is still tomorrow until the local date changes; if the next run happens after midnight it is "today" and gets no reminder (window ≤ one planner interval; no special path).

**J4 Dispatcher algorithm** (every minute, one instance via `DisableConcurrentExecution`):
1. Candidate query (no lock): up to 100 Pending ids with `scheduled_for ≤ now`, oldest first.
2. **For each id, in its own transaction:** `SELECT … WHERE id = @id AND status='Pending' FOR UPDATE SKIP LOCKED` (no row → skip); validate through the source module's `INotificationSourceValidator` and confirm the recipient is still active (set `Cancelled` and commit if not applicable); deliver through `INotificationChannel` with the notification id as idempotency key (V1 `InAppChannel`: set `Sent`, `sent_at`); commit.
3. If step 2 throws for an item: roll back that item's transaction only; open a **new short transaction** setting `status='Failed'`, `retry_count = retry_count + 1`, `last_error = <safe text>` (conditional on the row still being Pending) and **commit**.
4. The loop continues. **One poison notification never blocks or rolls back the others.**

`last_error` is exception type plus short message through the scrubber, truncated to 500 characters, no stack trace, SQL, names or contact data. Crash mid-item leaves the row Pending for the next run. Hangfire-level retries cover only infrastructure failures.

**Event-driven (no polling):** New Doctor Referral (Crm enqueues in the same transaction as the referral) and Feedback Request Pending (Feedback enqueues in the same transaction as the request). Both dispatch within a minute through J4.

**Not built:** the daily report preparation job (the Manager dashboard is live); automatic creation of Remaining Balance or installment follow-up rows (a Crm decision; J3 only notifies for follow-ups that exist). **Outage tolerance:** a Worker outage shorter than the remaining local day self-heals because planners select by date; if the whole reminder day is missed no late reminder is invented (the Sales dashboard still shows tomorrow's appointments and installments live).

### B7. Worker role resolution and grants (TECHNICAL)

The Worker resolves active Sales users through `Identity.Contracts.IActiveUserDirectory.GetActiveUserIds("SALES_RECEPTION")`, backed by the view `identity.v_active_user_roles (user_id, role_name)` (owned by `clinic_migrator`, not `security_invoker`). It resolves Sales-owner activity through `Staff.Contracts.IActiveSalesEmployeeDirectory` backed by `staff.v_sales_employee_activity (sales_employee_id, is_active)`. No role UUID is hardcoded. Grants are listed in SECURITY_DESIGN.md section 15: the Worker cannot read password hashes, stamps, contact data, clinical notes, follow-up notes, salaries, commission or any staff profile column.

### B8. Inbox API

`GET /api/me/notifications` (paged, Sent rows only), `GET …/unread-count` (polled every 60 s; no push channel in V1), `PATCH …/{id}/read`, `POST …/read-all` (CSRF-protected, idempotent; another user's id → 404). Sales and Manager only; Doctor → 403.

### B9. Notification tests

| # | Test |
|---|---|
| 1 | Appointment reminder job run twice creates no duplicate; concurrent runs create one row per recipient |
| 2 | Ortho installment reminder does not duplicate |
| 3 | A Doctor-created next appointment appears in the Sales schedule and generates its reminder |
| 4 | An implant follow-up yields exactly one reminder of type Implant Follow-Up Tomorrow |
| 5 | A rescheduled appointment gets a new reminder; the old one is Cancelled if Pending and renders as rescheduled if Sent |
| 6 | Timezone: tomorrow's range is correct on 23/25-hour days; dedupe key uses the clinic-local date; a changed timezone is respected |
| 7 | Installments of a fully paid ortho charge get no reminder; a cancelled appointment gets none |
| 8 | A new Sales user receives tomorrow's reminders on the next run; a deactivated user's Pending rows are Cancelled |
| 9 | Follow-ups produce one notification per due date to the assigned employee only; none when the owner is inactive |
| 10 | Concurrent dispatchers never process the same row (`SKIP LOCKED`); a crash mid-item leaves the row Pending |
| 11 | A failed delivery commits Failed with incremented `retry_count`; after 5 failures it stays Failed and alerts |
| 12 | A referral and its notifications commit atomically; a rolled-back referral leaves none |
| 13 | Only Sales and Manager read notifications; a Doctor gets 403; a user cannot read or mark another user's row |
| 14 | Canary scan finds no contact or clinical canary in notification rows, rendered DTOs or job logs |
| 15 | Worker grants: cannot read contacts, password hashes, security stamps, clinical notes or follow-up notes |
| 16 | A simulated Worker outage inside the reminder window self-heals; the heartbeat alert fires |
| 17 | Appointment created today for tomorrow receives a reminder on the next planner run |
| 18 | Appointment created on its own date gets no reminder |
| 19 | Appointment rescheduled today to tomorrow receives the new reminder; the old pending one is Cancelled |
| 20 | One failing notification in a batch does not roll back the others (the other 99 end Sent) |
| 21 | `Failed`, `retry_count`, `last_error` survive the dispatcher and are present on a fresh connection; `last_error` contains no canary data |
| 22 | Retry succeeds without creating a duplicate (row count and `dedupe_key` count unchanged) |
| 23 | The Worker resolves active SALES_RECEPTION users through `v_active_user_roles`; a direct SELECT on `identity.users`, `user_roles`, `roles` or `patient_contacts` fails |
| 24 | An ortho schedule generated today with an installment due tomorrow is reminded on the next J2 run; one generated on the due date is not |
| 25 | J7 reports inactive-owner follow-up counts using only the staff view; the Worker cannot SELECT salary, commission or any column of `staff.sales_employees` / `staff.doctors` |
| 26 | J3 sends nothing for a follow-up whose owner is inactive; the dispatcher and J7 use the same directory |

### B10. Open and neutral items

Q-EMP-1 (no delivery for inactive owners; visible by metric and dashboards); follow-up status list and open predicate (placeholder); Remaining Balance and installment follow-up creation (not designed); Q-FDB-1 (J6 inert); no notification is added for any finance question (including Pending Doctor Attribution). Defaults to veto: 15-minute planners, reminder availability from the start of the previous local day, retry cap 5, no reminder for fully paid ortho charges, `Africa/Cairo` default timezone. No Inventory and no X-ray appear anywhere.
