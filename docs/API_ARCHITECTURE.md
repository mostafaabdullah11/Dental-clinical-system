# API_ARCHITECTURE.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Source:** Step 11 as approved, Step 10 (Notifications and background jobs) with its corrections, **synchronized with the Owner Review decisions and the final Owner business changes** (OPEN_QUESTIONS.md §2, OWNER_REVIEW_NOTES.md): overlapping appointments with manual duration, Sales / Manager status actions, patient arrival and Doctor queue, charges at Visit finalization with Doctor-or-Sales-or-Manager discount and Doctor consultation fee, Patient Credit, Manager-only corrections, Lab-First distribution, Walk-In / Direct classification with zero commission, Ortho Doctor terms / Manager edits / manual application, controlled Doctor corrections, Manager-set passwords, Manager-maintained Assistant and Sales attendance, Assistant overtime, Sales-recorded Materials / Petty expenses, Sales-entered feedback, referral-notes privacy, editable audited expenses and public non-expiring feedback. Database objects are defined in DATABASE_DESIGN.md; authorization in SECURITY_DESIGN.md; ledger behavior in FINANCE_DESIGN.md.

Labels: **CONFIRMED** = Owner rule. **TECHNICAL** = engineering implementation. **DEFAULT** = veto-able value. The UI is **Arabic only, right-to-left**; the API is language-neutral (stable `code`s, no UI strings; the SPA maps codes to Arabic messages).

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
| `GET /health/live`, `GET /health/ready` | No detail in body. `ready` checks database connectivity and **schema compatibility** (required − applied migrations; see DEPLOYMENT_ARCHITECTURE.md). `ready` is not routed publicly. |
| `GET /api/public/feedback/{token}` | 256-bit token (hash looked up), rate limited per IP and per token hash, generic response for unknown tokens, no expiry. Returns only the Arabic form definition and the Doctor's display name — **no patient identifier**. **If the request is already Submitted (a `PatientWeb` or a `SalesEntered` entry exists for it) the route returns the neutral `feedback.already_submitted` state instead of the form** and nothing from the entry. CORS only for the clinic website. |
| `POST /api/public/feedback/{token}` | Same protection plus CSRF-exempt token binding; body `{ rating, comment }`; **at most one accepted result per request, whichever source arrives first** (a second submission, or a submission after Sales entered the feedback for the same Visit, returns the neutral `feedback.already_submitted`; a simultaneous race is resolved by the unique `feedback_request_id` and the loser gets the same answer); the submission is stored as a `PatientWeb` `feedback_entries` row, and the request's **Submitted** state is derived from the existence of an entry (never a stored second status). **No login, no patient typing of identifying data** (Q-FDB-2). |

**Authenticated without a permission key** (`[AuthenticatedOnly]`): `GET /api/me`, `POST /api/auth/logout`, `POST /api/auth/change-password`.

**There is no password-reset or account-recovery endpoint (Q-ACC-1).** Accounts are created by the Manager (`POST /api/manager/users` with username and initial password) and a forgotten password is replaced by the Manager (`POST /api/manager/users/{id}/password`). Passwords are write-only: no response ever contains a password or hash.

**Route / key rule.** There is no `/api/unassigned/*` route: unassigned Leads and Follow-Ups are read and assigned by the Manager through the ordinary Manager lead and follow-up routes (`Crm.Responsibility.Assign`). **Startup test:** every mapped route must carry a key assigned to at least one role, or be `[AuthenticatedOnly]` or anonymous; a mapped route whose key has no role fails the build. No route exists for printed receipts / invoices, consent forms, expense attachments, Daily Closing, Inventory, X-ray, Assistant login, Assistant self-check-in or a Walk-In pool.

### A3. DTO design (TECHNICAL)

Rules: a DTO name carries its audience (`DoctorPatientDto`, `SalesPatientDto`, `ManagerPatientDto`); every response is a `Select` projection or purpose-written mapper (**entities are never serialized**); every request is a dedicated command record (binding to an entity or a dictionary is forbidden by analyzer); contact-bearing properties carry `[ContactData]`; architecture tests scan every Doctor DTO **and the generated Doctor OpenAPI document** for `phone`, `whatsapp`, `email`, `address`, `crmNotes`, `salesNotes`, `originalSalesEmployee`.

| DTO | Fields | Deliberately absent |
|---|---|---|
| `DoctorPatientListItem` | id, patientCode, fullName, ageDisplay | everything else |
| `DoctorPatientDto` | id, patientCode, fullName, ageDisplay (DOB only while `doctor_dob_visible` is on), chiefComplaint, isActive, registeredAt, currentDoctor {id, name} | phone, whatsapp, email, address, CRM notes, original Sales employee |
| `DoctorPatientMedicalHistoryDto` *(v1.1)* | patientId, hasAllergy, hasHypertension, hasViralHepatitis, hasDiabetes, hasKidneyProblems, hasBleedingOrClottingProblem, hasHeartProblems, hasRheumaticDisease, isSmoker, hasPreviousSurgicalOperations, isTakingMedications, lastDentalVisit, isPregnant, isBreastfeeding, updatedAt (every flag is `true`, `false` or `null` = not answered / not applicable) | phone, whatsapp, email, address, CRM notes, original Sales employee, etag (no write) |
| `DoctorAppointmentDto` | id, patient {id, code, name}, startAt, durationMinutes, status, reason, kind, linkedCase {procedureName, status}, arrivedAt (nullable; informational) | `sales_notes`, contact data |
| `DoctorQueueItemDto` *(Q-APP-4)* | appointmentId, patient {id, code, name, ageDisplay}, reason, startAt, durationMinutes, arrivedAt | contact data, `sales_notes`, financial data |
| `DoctorReferralDto` | id, referralType, reason, **notes** (the referring Doctor's notes; Doctor audience only), status, createdAt | Sales follow-up work, original Sales employee / patient source |
| `DoctorOwnWorkDto` (one per work item) | workItemId, caseId, visitId, procedureName, originalPrice, discount, finalPrice, collected, remaining, doctorPercentage, **entitled**, **recognized**, **pendingEntitlement**, clinicShare, lab (for fixed-lab work also `labCoveredSoFar`) | other Doctors' items; any case, patient or clinic total. Money stays with the performing Doctor after patient reassignment. |
| `SalesPatientDto` | id, patientCode, fullName, **dateOfBirth** or recorded age, ageDisplay, phone, whatsapp, email, address, crmNotes, **patientSource** (`WalkIn` / `Lead`; Direct patients are `WalkIn`), originalSalesEmployee {id, name} (present only for `Lead`), isActive, registeredAt, currentDoctor {id, name}, **patientCreditBalance** | chief complaint and every Doctor-authored clinical field (the structured intake has its own DTO) |
| `PatientMedicalHistoryIntakeDto` *(v1.1, Sales / Reception)* | patientId, hasAllergy, hasHypertension, hasViralHepatitis, hasDiabetes, hasKidneyProblems, hasBleedingOrClottingProblem, hasHeartProblems, hasRheumaticDisease, isSmoker, hasPreviousSurgicalOperations, isTakingMedications, lastDentalVisit, isPregnant, isBreastfeeding, updatedAt, etag (flags are `true` / `false` / `null`; `null` is never returned as `false`) | clinical notes, prescription details, clinical measurements, Doctor percentage, shares, lab, any Doctor clinical DTO field |
| `SalesClinicalSummaryDto` | per case: procedureName, treatingDoctor {id, name}, status, referralDestination, **referralReason**, followUpInstructions, price, discount, total, paid, remaining, ortho installments | notes, teeth/details, steps, measurements, medications, Doctor percentage, shares, lab |
| `PricingLineSalesDto` *(final Owner change: Sales discount)* | pricingLineId, kind (`WorkItem` / `Consultation`), patient {id, code, name}, procedureName, treatingDoctor {id, name}, price, discount, total, `entrySeq` (the optimistic-concurrency token), lastDiscountSetBy {role, at} | clinical notes, teeth / details, steps, measurements, prescriptions, lab, Doctor percentage, entitlement, shares, commission, Doctor compensation, any other Doctor's data (the Sales audience sees the numbers a patient is quoted, nothing else) |
| `SalesAppointmentDto` | as Doctor DTO plus salesNotes, patient contact, `arrivedAt`, `arrivalRecordedBy` | clinical content |
| `SalesReferralDto` | id, patient, **referralType (destination)**, **reason**, status, followUpInstructions | **`notes` — the property does not exist on this DTO, in the Sales OpenAPI document, or in any Sales error body**; clinical content (Q-PRV-1: reason and destination are visible to Sales; the Doctor's notes never are). A Sales request that sends `notes` is rejected as an unknown member (400). |
| `SalesDoctorLookupItem` | id, fullName, specialty | **current percentage** and every other staff column |
| `PaymentConfirmationDto` | paymentId, amount, method, paidAt, allocations, **creditAdded**, resulting outstanding, resulting credit balance | **Data only.** No printed receipt or invoice exists in V1 (Q-DOC-1); no print endpoint. |
| `ExpenseCreatedDto` *(Sales)* | id, category, amount, expenseDate | list, totals, other users' expenses, audit data (the response echoes only the record just created) |
| `FeedbackEntryCreatedDto` *(Sales)* | id, recordedAt | rating history, results, other entries |
| `AuditEntryDto` | Manager only | |

### A4. Validation (TECHNICAL)

Order: (1) authorization first (failures return before body validation); (2) request validation (FluentValidation: types, lengths, ranges, required fields); (3) domain validation in the handler, including **catalog options** (`details` validated against `procedure_types.option_definition`): rejects `composite_zone` on a GIC filling or missing on Composite; an extraction type on a Pediatric Extraction; Basic/High Quality on anything but Laser Bleaching/Whitening; a missing appliance type when the ortho area is Appliance; canal number outside 1–5; healing period outside 1–6 months; Sutures and Bone Removal (those fields do not exist). Money must be positive with at most two decimals. A discount is a fixed EGP amount, never above the line's price; percentage discounts are not accepted. Validation errors name the field and a code, never the value.

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
| 409 | Duplicate/conflicting state: idempotency payload mismatch, second fixed lab on a case, appointment already rescheduled, feedback already submitted, stale discount `entrySeq`. **Overlapping appointments are never a conflict (Q-APP-1/2).** |
| 412 / 428 | `If-Match` mismatch / missing |
| 422 | Domain rule refuses: visit already finalized, charge already exists for the work item, allocation exceeds outstanding, credit insufficient, an installment batch leaves the schedule unbalanced or goes below a paid amount, a generic adjustment targets an ortho charge, an Ortho Total decrease, sign mismatch, discount on a finalized Visit, overtime for a voided or missing Assistant, expense category not open to Sales |
| 429 | Rate limit with `Retry-After` |
| 500 | Unexpected; only `traceId` returned |

Representative codes: `visit.finalized`, `payment.idempotency_mismatch`, `payment.exceeds_outstanding`, `credit.insufficient`, `fixed_lab.already_exists`, `fixed_lab.initial_required`, `workitem.charge_exists`, `ortho.schedule_unbalanced` (a schedule batch that does not end with Deposit + Σ active installments = Effective Ortho Total), `ortho.month_plan_mismatch` (a schedule batch that does not end with the number of active regular installments equal to the current Number of Months), `ortho.installment_below_paid_amount` (an amount below what was already applied), `ortho.installment_has_payment_history` (retiring or re-dating an installment that has payment history), `ortho.months_conflict_with_paid_history` (a Months value lower than the number of active regular installments that already have payments), `ortho.deposit_exceeds_effective_total` (Deposit above Gross − Discount; fails the Visit finalize or a Deposit edit, nothing written), `ortho.charge_not_established` (a terms or schedule edit before the Visit is finalized), `ortho.application_exceeds_allocation`, `ortho.total_decrease_not_supported` (V1 does not support a decrease of the Total Price; Q-ORT-3b is DEFERRED / OPEN FOR FUTURE VERSION), `ortho.use_terms_edit` (a generic price / discount adjustment aimed at an Orthodontic charge), `discount.stale_entry`, `discount.exceeds_price`, `feedback.already_submitted`, `catalog.option_invalid`, `concurrency.stale`. The SPA branches on `code` and shows the Arabic message, never the English title.

### A7. Concurrency and idempotency (TECHNICAL, CONFIRMED requirements)

- **Optimistic concurrency with ETag / If-Match**, derived from `xmin`. `PUT`, `PATCH` and state-changing `POST` sub-resources on editable resources **require** `If-Match` (patient identity/clinical profile/contact; visits; prescriptions; work-item pricing; leads; attendance records; follow-ups; availability weeks; appointment transitions; referral status; ortho terms and installments; expenses; settings).
- State transitions are idempotent when already in the target state. Visit finalize is idempotent.
- The database is the final guard: exclusion constraint (overlapping **availability ranges** only — appointments may overlap), unique constraints (payments, fixed lab, work-item charges, live arrival per appointment), deferred constraint triggers G8 / G8b. Violations map to 409 with specific codes.
- **`POST /api/sales/payments` requires an `Idempotency-Key` header (GUID)**, stored in `payments.idempotency_key` (unique). Replay with the same payload returns the original response; **same key with a different payload returns 409** (compared against stored rows; no hash column). Appointment booking is not constrained by time overlap; rescheduling relies on `previous_appointment_id` uniqueness plus `If-Match`, so two reschedules of one appointment cannot both succeed. `POST /patients/{id}/credit/apply` and correction routes take an `Idempotency-Key` as well. No generic idempotency-key table exists.
- Financial writes use the locking order in FINANCE_DESIGN.md (charge rows `FOR UPDATE` in id order).

### A8. Endpoint catalog (TECHNICAL)

#### Auth, me, public

| Route | Key | Notes |
|---|---|---|
| `GET /api/me` | authenticated | displayName, role, permission keys (menu only), clinic timezone, locale `ar` / `rtl` |
| `GET /api/me/notifications`, `…/unread-count`, `PATCH …/{id}/read`, `POST …/read-all` | `Notifications.Read.Own` | **Sales and Manager only; a Doctor gets 403.** `recipient_user_id = principal` |
| `GET /api/{doctor,sales,manager}/lookups` | any key of that audience | Role-specific reference data |

#### Doctor (`/api/doctor`)

| Route | Key | Scope / notes |
|---|---|---|
| `POST /patients/search` | `Patients.Search.Clinical` | ID or name only; no phone matching |
| `GET /patients/{id}` | `Patients.Clinical.Read` | All patients; **audited as record access** |
| `GET /patients/{id}/timeline` | `Patients.Timeline.Clinical` | Payment entries limited to the Doctor's own work |
| `PUT /patients/{id}/clinical-profile` | `Patients.Clinical.Write` | Chief complaint only (v1.1); Doctor has an editable visit for this patient; `If-Match` |
| `GET /patients/{id}/medical-history` *(v1.1)* | `Patients.MedicalHistory.Read` | Read-only structured intake (`DoctorPatientMedicalHistoryDto`, no contact fields); shown when the Doctor opens the patient clinical profile and before or during a visit. **There is no Doctor write route.** Record access is audited. |
| `GET /patients/{id}/visits`, `GET /visits/{id}`, `GET /treatment-cases/{id}` | `Clinical.Visits.Read` | Any Doctor's history readable |
| `GET /appointments` | `Scheduling.Appointments.View.Own` | `doctor_id = principal` |
| **`GET /queue`** | `Scheduling.Queue.ReadOwn` | **The Doctor's arrival queue** (Q-APP-4): own appointments with a live arrival today, status Scheduled / Confirmed, no Visit started, ordered by `arrivedAt`. `DoctorQueueItemDto`. Derived on read; leaves the queue when the Visit starts. |
| `GET /schedule?weekStart=` | `Scheduling.Availability.ViewOwn` | Published weeks only |
| `POST /appointments/follow-up` | `Scheduling.Appointments.CreateFollowUp` | visitId, startAt, reason; own visit; booked **with the authoring Doctor** (DEFAULT) |
| `POST /appointments/{id}/reschedule` | `Scheduling.Appointments.RescheduleOwnFollowUp` | Only a follow-up the Doctor created; `If-Match` |
| `POST /visits` | `Clinical.Visits.Write` | patientId, optional appointmentId; Doctor = principal; a linked Scheduled or Confirmed appointment moves to **In Treatment** (arrival is not required) and drops out of the queue |
| `PUT /visits/{id}` | `Clinical.Visits.Write` | Own, Draft/InProgress; `If-Match`. Carries the **consultation fee** (`Clinical.Visits.EnterConsultationFee`, **Doctor only**; the Manager cannot enter it, a later change is a Manager `POST /charges/{id}/adjustments` correction) |
| `POST /visits/{id}/finalize` | `Clinical.Visits.Finalize` | Own; idempotent; **one transaction**: visit Finalized → **Finance creates the Treatment charge(s) and the Consultation charge** (Q-FIN-W1; for an Orthodontic line `original_price` = Gross, `discount_amount` = the snapshot, `final_price` = Effective Ortho Total, and the Orthodontics handler of `ChargeEstablished` validates the Deposit and generates the initial installments in the same transaction) → linked appointment Completed and `visit_ended_at` set (releases unused booked time) → feedback request created |
| `POST /visits/{visitId}/treatments` | `Clinical.Treatments.Write` | See "Treatments and work items" below |
| `PUT /visits/{visitId}/treatments/{recordId}` | `Clinical.Treatments.Write` | Own editable visit and own work item |
| `POST /visits/{visitId}/cases/{caseId}/steps/{stepCode}/complete` | `Clinical.Treatments.Write` | Root canal steps; persists across visits |
| `PUT /visits/{visitId}/cases/{caseId}/canal-measurements` | `Clinical.Treatments.Write` | Canals 1–5, mm |
| `PUT /visits/{visitId}/cases/{caseId}/implant` | `Clinical.Treatments.Write` | Graft, membrane, procedure date, healing 1–6 months |
| `PUT /work-items/{id}/pricing` | `Clinical.WorkItems.EnterPrice` | **Doctor only, own work item** (the initial price; the Manager gets 403 — the Manager has no initial-price authority); body `{ price }` only; 422 once a charge exists (after that the Manager corrects a non-ortho price through `POST /charges/{id}/adjustments`, `PriceCorrection`; an Orthodontic Total is never corrected there, see `PUT /orthodontic-cases/{id}/terms`); 422 if the new price is below the line's current discount; `If-Match` |
| `PUT /pricing-lines/{lineId}/discount` | `Finance.Discount.Set` | **Doctor's own** pricing line (work item or consultation) before finalization; body `{ discount, expectedEntrySeq }` (fixed EGP; **percentage rejected**; ≤ price); appends one `pricing_discount_entries` row; 409 `discount.stale_entry` if `expectedEntrySeq` is not current; 422 once the Visit is Finalized. Same command as the Sales and Manager routes. |
| `POST /visits/{visitId}/corrections` | `Clinical.Visits.CorrectFinalized` | **Controlled correction of one's own finalized Visit** (record type, record id, changed fields, optional reason). The Visit stays Finalized; writes `visit_corrections` + audit. 404 for another Doctor's visit. No price / discount / fee fields are accepted |
| `POST /prescriptions/{id}/corrections` | `Prescriptions.CorrectFinalized` | Same for one's own prescription and its items |
| `POST /fixed-lab/{caseId}` | `Finance.FixedLab.SetInitial` | **Doctor only** (the Doctor performing priced work on the case; the Manager gets 403). **Initial** amount only, explicit (an entered 0 means "no lab cost"; an absent Fixed Lab is **never** read as 0); 409 `fixed_lab.already_exists` if the case already has one. It must exist before the Visit that establishes the case's first Fixed-lab Treatment charge can be finalized: otherwise `POST /visits/{id}/finalize` fails with 422 `fixed_lab.initial_required` and nothing is established or distributed |
| `POST /visits/{visitId}/orthodontic-cases` | `Orthodontics.Cases.Create` + `Orthodontics.Terms.SetInitial` | Area, appliance type if Appliance, and **Gross Total Price, Deposit, Number of Months** (Q-ORT-4). Creates the case's one work item (price = Gross) and **stores** revision 1 of the terms **during the editable Visit**: no charge and no installment exists yet. The fixed-EGP Discount is set on the line before finalization (`PUT /pricing-lines/{lineId}/discount`). **At `POST /visits/{id}/finalize`** (one transaction) the Discount is snapshotted, Effective Ortho Total = Gross − Discount, the Deposit is validated (`0 ≤ Deposit ≤ Effective`, else 422 `ortho.deposit_exceeds_effective_total` and the finalize fails with nothing written), the ortho charge is established and the equal installment schedule (`Months` installments from `Effective − Deposit`, last absorbs rounding) is generated **once**. Terms can be set once here by the **Doctor only** (`Orthodontics.Terms.SetInitial`; Manager and Sales get 403 — Sales cannot set terms); later changes are Manager-only (`PUT /orthodontic-cases/{id}/terms`, the equivalent of the shorthand `PUT /orthodontics/{id}/terms`). |
| `GET /orthodontic-cases/{id}` | `Orthodontics.Cases.Read` | Schedule/payment status for all Doctors; shares/percentages only on own work |
| `GET /patients/{id}/prescriptions`, `GET /prescriptions/{id}` | `Prescriptions.Read` | All prescriptions readable |
| `GET /prescriptions/{id}/print` | `Prescriptions.Print` | PDF from `PrescriptionPrintModel`; audited |
| `POST /visits/{visitId}/prescriptions` | `Prescriptions.Write` | Own editable visit |
| `PUT /prescriptions/{id}` | `Prescriptions.Write` | Owning visit is the Doctor's and **not finalized**, else 404 (finalized → use the correction route); items soft-removed; `If-Match` |
| `POST /visits/{visitId}/referrals`, `GET /patients/{id}/referrals` | `Crm.Referrals.Create`, `Crm.Referrals.Read.Clinical` | Own visit for creating |
| `GET /me/work-items`, `GET /me/work-items/{id}` | `Finance.OwnWork.Read` | `performing_doctor_id = principal` |
| `GET /me/payroll` | `Payroll.Doctor.ReadOwn` | Own rows only |

**Treatments and work items (Q-CLN-2).** `POST /visits/{visitId}/treatments` (own editable visit). Body: `caseId` (absent when starting a new case), `procedureTypeId` (new cases only), teeth, details. **There is no `separatelyPricedWork` field.**

| Situation | Result |
|---|---|
| Starting a new case | The case and a treatment record |
| Continuing an existing case (any Doctor) | A treatment record for the signed-in Doctor |
| The Doctor prices the work they performed | `PUT /work-items/{id}/pricing` creates / updates **that Doctor's own work item** for the record (snapshot of that Doctor's percentage at creation). At finalization it becomes that Doctor's charge. |
| A Doctor does not price a visit | No work item, no charge for that visit |
| Orthodontic case | A single work item and charge (the terms entry); a later Visit cannot add a price (422) |

Every Doctor therefore owns exactly the work they priced: Dr. A 3,000 (Visit 1) and Dr. B 2,000 (Visit 2) are two charges, each earning only for its own Doctor, with no revenue sharing. Total is always `price − discount`.


#### Sales (`/api/sales`)

| Route | Key | Notes |
|---|---|---|
| `POST /patients/search` | `Patients.Search.Contact` | ID, name or phone |
| **`POST /patients`** | **`Patients.Identity.Write` AND `Patients.Contact.Write`** | Name, DOB **or** recorded age, contact fields. The patient is classified **`WalkIn`** automatically (no Original Sales Employee is stored or accepted; the registering user is not recorded as one). **No medical fields.** Unknown members → 400. |
| `GET /patients/{id}`, `PUT /patients/{id}/identity`, `PUT /patients/{id}/contact` | `Patients.Contact.Read`; `Patients.Identity.Write`; `Patients.Contact.Write` | `If-Match` on writes; contact changes audited with restricted values; DOB visible to Sales |
| `GET /patients/{id}/clinical-summary` | `Clinical.SalesSummary.Read` | Whitelist only |
| `GET /patients/{id}/medical-history` *(v1.1)* | `Patients.MedicalHistory.Read` | `PatientMedicalHistoryIntakeDto`; returns the ETag; 404 when no intake exists yet |
| `PUT /patients/{id}/medical-history` *(v1.1)* | `Patients.MedicalHistory.EditIntake` | Creates the intake when none exists (no `If-Match`), otherwise replaces the answers with `If-Match` (412 on a stale ETag). Dedicated command record; flags accept `true`, `false` or `null`; unknown members are rejected; `lastDentalVisit` is text ≤ 200. Audited (changed field names only). No dependency on any appointment state. |
| `GET /patients/{id}/timeline` | `Patients.Timeline.Operational` | No prescription or visit content |
| `GET /patients/{id}/balance`, `GET /patients/{id}/payments` | `Finance.PatientBalance.Read` | Charges (price, discount, total), Billed, Collected, Outstanding, **Patient Credit balance and entries**. No shares / percentages / lab. |
| `GET /outstanding-balances` | `Finance.PatientBalance.Read` | Feeds dashboard and follow-ups |
| `GET /doctors`, `GET /availability/slots?doctorId&from&to` | `Scheduling.Availability.View` | `SalesDoctorLookupItem`; published weeks only |
| `GET /appointments`, `POST /appointments` | `Scheduling.Appointments.View.All`, `.Book` | Body requires **`durationMinutes`** (manual, 1–480). **Overlapping appointments for the same Doctor are accepted**; no conflict response. |
| **`POST /appointments/{id}/status`** | `Scheduling.Appointments.UpdateStatus` | **Confirm, Cancel, No Show** (Sales and Manager); `If-Match`; reason on Cancel. `Arrived` / `Waiting` are rejected. |
| **`POST /appointments/{id}/arrival`**, `POST /appointments/{id}/arrival/void` | `Scheduling.Arrival.Record`, `Scheduling.Arrival.Void` | **Sales only.** Records the arrival event / timestamp (Scheduled or Confirmed appointment of today). **Does not change the Appointment status.** The patient appears in the Doctor's queue. |
| `POST /appointments/{id}/reschedule` | `Scheduling.Appointments.Reschedule` | New appointment, old one Rescheduled, reason stored; `If-Match` |
| `GET /leads/my`, `POST /leads`, `GET/PUT /leads/{id}`, `POST /leads/{id}/convert` | `Crm.Leads.ViewOwn`, `Crm.Leads.Manage` | **My Leads** = leads where the principal is responsible. **Conversion preserves the lead's Original Sales Employee** and classifies the patient `Lead`. |
| `GET /referrals`, `GET /referrals/{id}`, `PATCH /referrals/{id}/status` | `Crm.Referrals.Read.Sales`, `.UpdateStatus` | `SalesReferralDto` |
| `GET/POST /follow-ups`, `PUT /follow-ups/{id}`, `POST /follow-ups/{id}/contact-attempts` | `Crm.FollowUps.Manage` | Own responsibility (Follow-Ups with no responsible Sales employee are **not** visible here; the Manager assigns them); status ∈ جديد / تم التواصل / لم يرد / تمت المتابعة (`New` / `Contacted` / `NoResponse` / `FollowedUp`); open = anything but `FollowedUp`. Attempts append-only. |
| **`GET /pricing-lines?patientId=`** *(final Owner change)* | `Finance.PricingLines.Read` | **Priced, not-yet-finalized lines** of the patient's open Visits as `PricingLineSalesDto` (price, discount, total, `entrySeq`). Nothing clinical, no Doctor percentage, entitlement, lab or commission. |
| **`PUT /pricing-lines/{lineId}/discount`** *(final Owner change)* | `Finance.Discount.Set` | Sales / Reception sets the fixed EGP discount before finalization; body `{ discount, expectedEntrySeq }`; appends an entry (actor, role `SALES_RECEPTION`, time); 409 `discount.stale_entry` on a stale token; 422 after finalization or above price. Once the Charge exists a discount change is a Manager `DiscountCorrection` (403 for Sales; not available for an ortho charge: 422 `ortho.use_terms_edit`). |
| **`POST /expenses`** *(final Owner change)* | `Expenses.CreateOperational` | **Create only:** a Materials / Medical Supplies or Petty / Miscellaneous expense in a category flagged `sales_can_record`; returns `ExpenseCreatedDto`. Sales has **no** list, read, edit, delete, category-management or report route for expenses. The expense records `created_by_user_id`, `created_by_role = SALES_RECEPTION` and `created_at`. |
| **`GET /expenses/allowed-categories`** *(final correction pass)* | `Expenses.Categories.ReadAllowed` | **Read-only lookup for the Sales expense form:** returns `[{ id, name }]` of the **active** categories with `sales_can_record = true` and nothing else (no flag, no totals, no expenses). Sales and Manager only; Doctor 403. It is the only expense-category route Sales has. |
| **`POST /feedback-entries`** *(final Owner change; request linkage corrected in the final correction pass)* | `Feedback.Entries.Record` | Sales-entered Patient Feedback about a Doctor: `{ patientId, doctorId, visitId?, rating (1–5), comment }`; stored as `source = SalesEntered` with the actor (`recorded_by_user_id`); never a clinical note. **With `visitId`:** the Visit must be Finalized and belong to that Patient and Doctor; the entry **links the Visit's existing feedback request**, which thereby becomes **Submitted** (derived), so the public link stops accepting submissions; if the request already has an accepted entry (either source) → 409 `feedback.already_submitted`. **Without `visitId`:** general feedback with no Visit and no request. Returns `FeedbackEntryCreatedDto`. Sales cannot read results. |
| **`POST /payments`** | `Finance.Payments.Record` | **`Idempotency-Key` required.** Explicit allocations to charges and amounts; **any remainder (or the whole amount when no charge exists) becomes Patient Credit automatically** (`creditAdded`). Allocation to a charge cannot exceed its Outstanding. |
| **`POST /patients/{id}/credit/apply`** | `Finance.PatientCredit.Apply` | `Idempotency-Key`; applies existing credit to a charge with Outstanding (oldest credit first). Runs the same distribution as a payment. |
| **`POST /orthodontic-cases/{id}/installment-payments`** | `Orthodontics.Installments.ApplyPayment` | **Manual application (Q-ORT-2):** body lists `{ allocationId, installmentId, amount }` rows (one→one, one→many, partial, early, future); capped by the allocation (G5) and the installment's amount. No automatic oldest-first. Sales and Manager. |
| `GET /orthodontic-cases/{id}` | `Orthodontics.Cases.Read` | See "Ortho reads" below |
| `GET /feedback-requests?status=` | `Feedback.Queue.Manage` | The Sales queue (Pending / Sent / Submitted, derived). No rating or comment is returned. |
| `POST /feedback-requests/{id}/link`, `POST /feedback-requests/{id}/mark-sent` | `Feedback.Queue.Manage` | `link` issues a fresh unique token and returns the website URL once (an earlier token stops working); `mark-sent` records channel, sender, date (status **Sent**). The patient then submits through the public routes (the derived state becomes **Submitted**). No expiry. A second, independent path is `POST /feedback-entries` above. |
| `GET /dashboard` | `Reports.Dashboard.Sales` | The eight confirmed dashboard items (no attendance, no overtime); **Pending Follow-Ups** use the open predicate (`status <> 'FollowedUp'`); arrived-today patients appear in the appointment list, not as a status |
| `GET /me/payroll` | `Payroll.Sales.ReadOwn` | `sales_employee_id = principal` |

**Ortho reads (all audiences).** Each installment shows due date, **current amount** (latest revision), **applied amount** (`amountAlreadyPaid`), remaining and **status (Upcoming / Due / Partially Paid / Paid / Overdue) derived from the manual application rows**. The response adds charge-level Collected and Outstanding, deposit paid, the unapplied ortho collections figure and `grossTotal`, `discount`, `effectiveOrthoTotal` (= Charge Billed), and the derived validity fields `scheduleDifference` (Effective − Deposit − Σ active installments), `monthPlanDifference` (Number of Months − active regular installments) and `requiresScheduleAdjustment` (true while either difference is not 0; a warning, not a status). Retired installments are returned only in the history section (not payable); each installment shows `origin` (`Generated` / `ManagerAdded` / `PriceChange`), current due date and `isActive`. Percentages / shares only for the Doctor's own work. There is no `applicationState: "Pending"` field.

#### Manager (`/api/manager`)

| Group | Routes | Keys |
|---|---|---|
| Dashboard | `GET /dashboard?date=` (defaults to today, clinic-local; no manual generation) | `Reports.Dashboard.Manager` |
| Patients | `POST /patients/search`; `GET /patients/{id}` (`ManagerPatientDto`, full); `GET …/timeline`; `GET …/assignments`; `POST …/reassign` (patient, new Doctor, reason; audited; `If-Match`; **does not move any financial ownership**); **`POST /patients`** (Walk-In) | `Patients.Search.*`, `Patients.Clinical.Read`, `Patients.Contact.Read`, `Patients.Reassign`; registration needs **both** `Patients.Identity.Write` and `Patients.Contact.Write` |
| Pricing lines | The Manager may set a pre-finalization discount through `PUT /api/sales/pricing-lines/{lineId}/discount` (Manager holds the Sales keys) and read `GET /api/sales/pricing-lines`; after establishment `POST /charges/{id}/adjustments` | `Finance.Discount.Set`, `Finance.PricingLines.Read`, `Finance.Charges.Adjust` |
| Scheduling | `GET/PUT /doctors/{id}/availability/{weekStart}` (draft), `POST …/publish`, `GET /appointments`, `POST /appointments` (overlap allowed, **no override concept**), `POST /appointments/{id}/reschedule`, **`POST /appointments/{id}/status`** (Confirm, Cancel, No Show) | `Scheduling.Availability.Publish`, `.View`, `Scheduling.Appointments.View.All`, `.Book`, `.Reschedule`, `Scheduling.Appointments.UpdateStatus` |
| Finance | `GET /finance/summary?from&to` (Billed, Collected, Outstanding, cash received by method, discounts (by setter role), consultation fees, Doctor recognized share, **pending Doctor entitlement**, Lab, Clinic Treatment Revenue Share, Clinic Gross Revenue, **Patient Credit**, refunds / corrections, estimated result), `GET /finance/pending-entitlements`, `GET /finance/charges`, `GET /finance/payments`, `GET /finance/credit` | `Finance.Management.Read` |
| **Corrections (Manager only, Q-PAY-1)** | `POST /payments/{id}/reversal`, `POST /payments/{id}/refund`, `POST /payments/{id}/reallocate`, `POST /payments/{id}/replace`, `POST /charges/{id}/adjustments` (audited price / discount correction of **non-Orthodontic** charges; for an ortho charge it returns 422 `ortho.use_terms_edit`, and `OrthoTermsChange` cannot be sent by a client); all `Idempotency-Key`, reason required, new ledger rows only | `Finance.Payments.Correct`, `Finance.Charges.Adjust` |
| **Orthodontics (Manager)** | `PUT /orthodontic-cases/{id}/terms` (**the only route that changes an established Gross Total / Deposit / Months**; reason required; only after the Charge exists; a Gross increase adds exactly one `PriceChange` difference installment and raises Billed by the same amount, the Discount snapshot unchanged; a decrease → 422 `ortho.total_decrease_not_supported`; a Deposit above the Effective Total → 422 `ortho.deposit_exceeds_effective_total`; a Months value below the paid regular installments → 422 `ortho.months_conflict_with_paid_history`; a Deposit or Months change writes the revision only, **regenerates, adds, removes and redistributes nothing**, and returns `requiresScheduleAdjustment: true` with `scheduleDifference` / `monthPlanDifference` when the schedule no longer qualifies), `PUT /orthodontic-cases/{id}/installments` (**the only schedule-edit route**: one atomic, audited batch `{ reason, changes: [ { installmentId, amount?, dueDate?, retire? }, { add: { dueDate, amount } } ] }`; amounts ≥ `amountAlreadyPaid` else 422 `ortho.installment_below_paid_amount`; due-date change and retirement only for installments without payment history else 422 `ortho.installment_has_payment_history`; an added installment is `ManagerAdded` with a due date ≥ today and no payments; retirement never deletes (no DELETE route exists); at the end Deposit + Σ active = Effective Ortho Total else 422 `ortho.schedule_unbalanced`, and active regular installments = Number of Months else 422 `ortho.month_plan_mismatch`; any failure writes nothing; `If-Match`) | `Orthodontics.Terms.Edit`, `Orthodontics.Installments.Edit` |
| Lab | `GET/PUT /settings/lab-percentage` (inserts a history row; validated so Doctor % + Lab % ≤ 100), `PUT /fixed-lab/{caseId}` (**Manager revises an existing Fixed Lab only**: a new revision, **reason required**, audited; triggers the reconciliation delta rows; 409 `fixed_lab.initial_required` when no initial Doctor entry exists — the Manager can never create the initial amount) | `Finance.LabPercentage.Configure`, `Finance.FixedLab.Revise` |
| Payments | The Manager records payments through `POST /api/sales/payments` | `Finance.Payments.Record` |
| Staff | `GET/POST /staff/doctors`, `GET/POST /staff/sales-employees` (**commission percentage is required on creation**), `PUT /doctors/{id}/percentage`, `PUT /sales-employees/{id}/commission` (**FUTURE COLLECTIONS ONLY** — Manager only; appends an audited rate-history revision effective at the moment of saving; the rate in force at each collection's `allocated_at` applies; old collections, Draft items and finalized payroll are never recalculated; Walk-In / Direct stays 0), `GET /sales-employees/{id}/commission-history` (Manager), `PUT /sales-employees/{id}/base-salary`, `POST /staff/{id}/deactivate` (a Sales deactivation redistributes open Leads / Follow-Ups evenly among Active Sales; with no Active Sales they become unassigned and the deactivation still succeeds), `POST /staff/{id}/reactivate` | `Staff.Manage`, `Staff.DoctorPercentage.Configure`, `Staff.SalesCommission.Configure`, `Payroll.Sales.ManageAll` |
| Assistants *(v1.1; all under `/api/manager`, so the full paths are `/api/manager/staff/assistants…` and `/api/manager/payroll/assistants…`; Manager only; no Assistant account or login; no Assistant audience, no OpenAPI document of their own)* | `GET/POST /staff/assistants`, `PUT /staff/assistants/{id}` (full name / basic information; `If-Match`), `POST /staff/assistants/{id}/deactivate`, `POST …/reactivate` | `Staff.Manage` |
| Assistant salary *(v1.1)* | `PUT /staff/assistants/{id}/salary` (new current salary, reason; audited; `If-Match`; never alters an existing payroll snapshot) | `Staff.AssistantSalary.Configure` |
| **Assistant overtime rate** *(final Owner change)* | `PUT /staff/assistants/{id}/overtime-rate` (Manager-configured hourly rate, ≥ 0, reason; audited; `If-Match`; never alters an existing overtime entry or payroll item) | `Staff.AssistantOvertimeRate.Configure` |
| **Assistant overtime** *(final Owner change)* | `GET /staff/assistants/{id}/overtime?month`, `POST /staff/assistants/{id}/overtime` (`workDate`, `approvedHours` > 0 and ≤ 24, optional `attendanceRecordId`, notes; the **hourly-rate snapshot and amount are computed by the server**; one live entry per Assistant per day), `POST /staff/assistants/overtime/{entryId}/void` (reason; the only lifecycle change; entries are never edited or deleted). Overtime is **never created automatically from attendance times.** | `Staff.AssistantOvertime.Manage` |
| **Attendance** *(final Owner change; Assistants and Sales / Reception; Manager only)* | `GET /staff/attendance?employeeKind&employeeId&from&to`, `POST /staff/attendance` (`assistantId` **or** `salesEmployeeId`, `date`, `status` Present / Late / Absent, optional check-in / check-out time, notes), `PUT /staff/attendance/{id}` (`If-Match`; audited before / after). One record per person per day (409 `attendance.duplicate_day`). No self-check-in, no clock-in route, no Assistant or Sales write route, no Doctor attendance. | `Staff.Attendance.Manage` |
| Users | `POST /users` (**create an account: username + initial password**, role, staff link), `POST /users/{id}/password` (**set a new password**; write-only), `PUT /users/{id}/role`, `POST /users/{id}/force-sign-out` | `Identity.Users.Manage`, `Identity.Users.SetPassword` |
| Leads and Follow-Ups | `GET /leads` (**All Leads** incl. Responsible Sales Member; filter `unassigned=true`), `POST /leads/{id}/reassign`; `GET /follow-ups?unassigned=true`, `POST /follow-ups/{id}/assign` (assign or reassign, including items left unassigned when no Active Sales employee existed; writes `responsibility_history`) | `Crm.Leads.ViewAll`, `Crm.Responsibility.Assign` |
| Expenses | `GET/POST /expenses`, `PUT /expenses/{id}` (direct edit, `If-Match`, every edit audited with before / after; the edit sets only `updatedBy` / `updatedAt` and never overwrites the creator, creator role or `createdAt`), `GET/POST/PUT /expense-categories` (including the `salesCanRecord` flag; **Manager only**). Entries show who created them and in which role. **No adjustment route, no finalize route, no attachment route.** | `Expenses.ReadAll`, `Expenses.Manage`, `ExpenseCategories.Manage` |
| Assistant payroll *(v1.1)* | `GET /payroll/assistants` (list, per-Assistant history, filters), `GET /payroll/assistants/{id}`, `POST /payroll/assistants/drafts` (assistant, calendar month; **Base Pay defaults to the salary snapshot** and the month's unvoided, not-yet-paid overtime entries are attached as overtime items), **`PUT /payroll/assistants/{id}/base-pay`** (Manager-entered Base Pay with a mandatory `partialPeriodReason` of `JoinedMidMonth` / `LeftMidMonth` / `SalaryChangedInMonth` / `OtherPartialMonth`; audited; Draft only; overtime is not editable here), `POST /payroll/assistants/{id}/finalize` (after the month ends; `final_amount = base pay + overtime`), `POST …/{id}/mark-paid`. The response shows **Base Pay, Overtime, Final** separately. A second payroll for one Assistant-month → 409 `payroll.assistant_period_exists`. An overtime entry belongs to at most one payroll. No proration is ever computed. | `Payroll.Assistant.ManageAll` |
| Payroll | Monthly, calendar-month periods. `GET /payroll/doctors`, `POST /payroll/doctors/drafts` (doctor, month; includes inactive Doctors with unpaid earnings), `POST …/{id}/adjustments`, `POST …/{id}/finalize` (only after month end), `POST …/{id}/mark-paid`; `/payroll/sales` likewise (commission lines only; **Walk-In / Direct work earns no commission and there is no pool**) | `Payroll.Doctor.ManageAll`, `Payroll.Sales.ManageAll` |
| Reports | `GET /reports/financial` (daily, weekly, monthly or custom `from`/`to`; Assistant and Sales payroll months appear only when fully inside the range; Assistant payroll shows Base Pay and Overtime as separate lines; Materials / Medical Supplies and Petty / Miscellaneous expenses are separate lines; no proration, no pending / incomplete state; no Daily Closing), `/doctor-performance`, `/sales-performance` (commission on collected money after discount; Walk-In / Direct collections shown with zero commission), `/orthodontic-receivables`, `/expenses`, `/attendance` (Assistants and Sales attendance and approved overtime, by month), each with `GET …/export` (CSV/PDF, Arabic) | `Reports.Financial`, `Reports.Attendance`, `Reports.Export` |
| Feedback | `GET /feedback?doctorId&from&to&source` (ratings and comments of both sources, with the `source` shown; **Manager only**; Doctors and Sales never receive results) | `Feedback.Results.Read` |
| Audit | `GET /audit?…` (cursor), `GET /audit/{id}` | `Audit.Read` (reading is audited) |
| Settings | `GET/PUT /settings` (timezone, DOB visibility for Doctors, clinic header details in Arabic), `GET/PUT /payment-methods` | `Configuration.Manage` |

**Registration and Walk-In classification (Q-PAT-1).** `POST /api/sales/patients` and `POST /api/manager/patients` create a **Walk-In** patient: no Original Sales Employee is stored, accepted or implied; neither body has such a field (unknown members → 400). Lead conversion creates a `Lead` patient carrying the lead's Original Sales Employee (CONFIRMED). Reports show Walk-In versus Lead-sourced collections; Walk-In / Direct work earns **zero** Sales commission and feeds no pool (Q-PAT-1b is withdrawn). Authorization is the composite `[HasAllPermissions(...)]` policy.

**Manager clinical editing.** The Manager reaches Doctor clinical write endpoints (the "own" requirement is waived per SECURITY_DESIGN.md) to edit **non-finalized** records only; no Manager-initiated visit workflow exists. The Manager can never overwrite a finalized or ledger record (G1 and insert-only grants).

**Appointment statuses and arrival.** Confirm, Cancel and No Show go through `POST /appointments/{id}/status` for **Sales and Manager**; Reschedule for Sales and Manager. In Treatment (the Doctor starts the linked visit), Completed (visit finalize) and Rescheduled follow confirmed actions. The enum has exactly seven values (Scheduled, Confirmed, InTreatment, Completed, Cancelled, NoShow, Rescheduled). **Patient arrival is a separate event**: `POST /appointments/{id}/arrival` (Sales) stores `arrivedAt`; the Appointment status is unchanged. The Doctor reads `GET /api/doctor/queue`. There is no `CheckIn` route and no `Arrived` / `Waiting` value.

### A9. Endpoints deliberately NOT created

| Not created | Why |
|---|---|
| Password-reset / account-recovery endpoints, email / SMS reset | Q-ACC-1: the Manager sets passwords |
| Printed receipts / invoices, tax invoices | Q-DOC-1: none in V1 |
| Digital consent forms, e-signature, consent storage | Q-DOC-2 |
| Expense attachments, expense finalize / adjustment routes | Q-EXP-1, Q-EXP-2 |
| Daily Closing / Close Day | Q-FIN-DC |
| Feedback expiry / expired state | Q-FDB-1 |
| Check-in route, Arrived / Waiting statuses | Arrival is a separate event (Q-APP-4); only `arrival` and `queue` exist |
| Manager schedule override, conflict-on-overlap | Q-APP-1/2: overlap is allowed |
| Attribution hold, pending Doctor attribution, reassignment-triggered finance routes | Q-FIN-9: ownership stays with the performing Doctor |
| `separatelyPricedWork` flag | Q-CLN-2: each Doctor prices their own work |
| Automatic oldest-first installment allocation | Q-ORT-2: manual application |
| Reopen-visit route, Manager approval of corrections | Q-CLN-1 |
| Doctor notifications and dashboard | Not confirmed |
| Branch / multi-branch routes | Q-BRN-1 |
| Any `/api/unassigned/*` route | Removed; unassigned work is a filter on the Manager lead and follow-up routes |
| Walk-In pool generation / finalization / report routes | Owner decision: Walk-In / Direct work earns zero commission; no pool exists |
| Assistant login, Assistant self-check-in, Sales self-check-in, clock-in routes, Doctor attendance | Attendance is maintained by the Manager only |
| Automatic overtime from attendance times, overtime edit / delete routes | Overtime is a Manager-entered, void-only record |
| Expense list / edit / delete for Sales; Sales category management; Sales access to unrestricted expense analytics or reports | Sales may only create Materials / Petty expenses and read the allowed-categories lookup |
| Manager route that creates an initial work price, an initial Fixed Lab, or initial Ortho terms | The Doctor owns the initial entries; the Manager only corrects / revises (audited) |
| Recalculation of old Sales commission after a rate change | The rate at collection time applies; rate changes are future-only |
| A second feedback result for one request; a stored feedback-request status | At most one accepted entry per request; state is derived |
| Sales access to Feedback results; Doctor access to Feedback | Results are Manager only |
| Referral `notes` in any Sales response | Referral notes are for Doctors and the Manager |
| `PUT …/final-amount` for Assistant payroll | Replaced by Base Pay entry plus overtime items |
| Percentage-discount routes, Sales/Doctor discount after finalization | Fixed EGP only; later corrections are Manager adjustments |
| Ortho total-price decrease route; any route or job that regenerates, adds, removes or redistributes installments automatically; a negative installment; a route that physically deletes an installment | V1 does not support them (decrease: Q-ORT-3b DEFERRED, rejected with `ortho.total_decrease_not_supported`); schedule changes are only the Manager's explicit batch, and retirement is a revision, never a delete |
| Inventory, X-ray | Out of scope |

### A10. Cross-cutting behavior

Rate limits: auth 10/min/IP, general 300/min/user, patient search 60/min/user (DEFAULT). Audit written in the same transaction; record access, exports and PDF prints write their own entries. Downloads: `Content-Disposition: attachment`, no patient names in file names, CSV formula neutralization, no server-side storage. No request/response bodies logged on patient, contact, finance or payroll routes; route templates are logged, never raw URLs.

### A11. API tests (in addition to the security and finance tests)

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
| 9 | **Overlap allowed (Q-APP-1/2):** two concurrent bookings for the same Doctor and time both succeed (201/201), for Sales and for Manager; `durationMinutes` missing → 400; two concurrent reschedules of one appointment → one success |
| 10 | Early Visit finalize sets `visit_ended_at`; the unused future time is no longer reported booked while the Appointment and its history remain |
| 11 | Sales records arrival → `arrivedAt` stored, **status unchanged**; Sales only (Doctor 403, Manager 403 by design); Scheduled / Confirmed today only |
| 12 | `GET /api/doctor/queue` returns the principal's arrived, not-started appointments ordered by arrival; no contact data; Sales gets 403 |
| 13 | Starting the linked Visit sets In Treatment and the entry disappears from the queue; an appointment in any other status is not changed by visit start |
| 14 | Sales and Manager can Confirm / Cancel / No Show; Sales can Reschedule; Doctor 403; `Arrived` / `Waiting` values are rejected (400) |
| 15 | Follow-up open predicate: only `New`, `Contacted`, `NoResponse` count as open for pending lists, J3, J7, dashboard counts and redistribution; `FollowedUp` is closed; any other status is rejected |
| 16 | Visit finalize creates the Treatment charge(s) and the Consultation charge in the same transaction; none exist before; Total = Price − Discount; a percentage discount → 400; the discount snapshot records setter, role and time; Sales and Manager may set it before finalization, never after (403 / 422) |
| 17 | Different Doctors price their own work in the same case → two charges, each owned by its Doctor; no `separatelyPricedWork` member is accepted; a second price on an ortho case → 422 |
| 18 | `POST /payments`: overpayment returns `creditAdded`; a payment with no charge is entirely credit; `credit/apply` consumes credit; an allocation above Outstanding → 422 |
| 19 | Corrections (reversal, refund, reallocation, replacement, charge adjustment) are Manager only (Doctor and Sales 403), need a reason and an Idempotency-Key, and never update rows (new rows only) |
| 20 | Manager `POST /users` stores a hash only; `POST /users/{id}/password` ends that user's sessions; no response, log or audit entry contains a password; no `/password-reset` route exists |
| 21 | Sales employee creation without a commission percentage → 400/422; with one → 201 |
| 22 | Sales deactivation redistributes open Leads and open Follow-Ups evenly (counts differ by at most one) among Active Sales; `responsibility_history` written; **with no Active Sales the deactivation still succeeds and the items become unassigned** (visible to the Manager, absent from every My Leads / own Follow-Up list, assignable by the Manager); My Leads vs All Leads visibility |
| 23 | Walk-In / Direct registration stores no Original Sales Employee and rejects the field; Lead conversion preserves the lead's value; `patientSource` is the only source property |
| 24 | Ortho: Doctor sets initial terms; Manager edits terms; only Manager edits the schedule (one atomic batch that must end with Deposit + Σ active installments = Effective Ortho Total, active regular installments = Months and no amount below its paid amount, else 422); Sales / Manager manually apply payments (one→many, partial, early, future; over-application → 422); a Total increase adds the difference installment and leaves others unchanged; a Total decrease is rejected with `ortho.total_decrease_not_supported` (V1; Q-ORT-3b deferred) |
| 25 | Doctor finalized-record correction: owning Doctor succeeds; other Doctor 404; Manager and Sales denied; Visit stays Finalized; a `visit_corrections` row and audit entry exist; price / discount fields rejected |
| 26 | Assistant payroll: Base Pay defaults to the snapshot; `PUT …/base-pay` requires a reason code and is audited; Overtime is separate and read-only on the payroll; `final = base + overtime`; no proration anywhere; no `final-amount` route; Assistant routes stay Manager-only; no Assistant OpenAPI document |
| 27 | Expense `PUT` edits directly with `If-Match` and writes an audit entry with before / after; no finalize, adjustment or attachment route exists |
| 28 | Public feedback `GET`/`POST` work with a valid token and no session, return no patient identifier, work at any later date (no expiry), and refuse a second submission with `feedback.already_submitted`; unknown token → generic 404 |
| 29 | No receipt, invoice, consent, Daily Closing, branch, Inventory or X-ray route exists in any OpenAPI document |
| 30 | Sales can read DOB, referral destination and referral reason; **no Sales response, error body or OpenAPI schema contains referral `notes`**; the Sales documents still contain no clinical, Doctor-percentage, share, entitlement or lab properties |
| 31 | Monthly payroll: periods are calendar months; finalize before month end → 422; sealed after finalization |
| 32 | Money: amounts are strings, three decimals rejected |
| 33 | Pagination bounds, sort whitelist (unknown field 400), search endpoints reject GET with PII |
| 34 | Error shape: every error is problem+json, no echoed input, no stack trace |
| 35 | Manager blocked on `/me/*` own routes (404); may call permitted `/api/doctor/*` routes and receives Doctor-shaped DTOs |
| 36 | An export writes an audit entry; a PDF text extraction contains no contact canary; printable output is Arabic |
| 37 | Contract drift: generated TypeScript clients in CI match the OpenAPI documents; the SPA root has `dir="rtl"` and `lang="ar"` (RTL contract) |
| 38 | The intake routes and DTOs mention no Arrived, Waiting or check-in concept and work for a patient with no appointment; unanswered fields round-trip as `null`, never `false`; no generic `medicalHistory`, `chronicDiseases` or `previousSurgeries` property |
| 39 | Sales discount: Sales can list `PricingLineSalesDto` and set a fixed discount before finalization; the DTO has no clinical, percentage, entitlement or lab property; a stale `expectedEntrySeq` → 409; two concurrent writers → one success; after finalization → 422 (Sales 403 for corrections); a discount above price or a percentage → 400/422; Doctor sets only own lines; Manager may set any |
| 40 | Attendance: Manager-only CRUD for Assistants and Sales; one record per person per day (409); Doctor / Sales get 403; no self-check-in or Assistant route exists; edits audited before / after |
| 41 | Overtime: Manager-only; rate and amount are computed server-side from the Manager-configured rate snapshot; a later rate change leaves existing entries and payroll items unchanged; void-only lifecycle; one live entry per Assistant per day; an entry enters at most one payroll; a finalized payroll rejects overtime changes |
| 42 | Sales expense: Sales can only `POST /expenses` in categories flagged for Sales; list / read / edit / category-management / report → 403; the response echoes only the new record; Manager edits directly with audit and the creator fields stay unchanged |
| 43 | Feedback dual path: public POST creates `PatientWeb`; `POST /feedback-entries` creates `SalesEntered`; neither stores anything in a clinical table; only Manager reads results; the unique-per-request rule applies to **both** paths (see tests 49–50) |
| 44 | Unassigned Leads / Follow-Ups: Manager lists (`unassigned=true`) and assigns; Sales never sees them; assignment writes `responsibility_history` |
| 45 | Notification suite (B9) |
| 46 | **Initial price is the Doctor's:** the Doctor enters the price on an own work item; the Manager's `PUT /work-items/{id}/pricing` and consultation-fee write → 403; after finalization the Manager corrects only through `POST /charges/{id}/adjustments` (audited) |
| 47 | **Fixed Lab:** Doctor `POST /fixed-lab/{caseId}` creates revision 1; a second POST → 409; the Manager's POST → 403; the Manager's `PUT` creates revision 2 with reason and the reconciliation delta rows; the Doctor's PUT → 403; finalizing the Visit of a Fixed-lab priced work item with no Fixed Lab → 422 `fixed_lab.initial_required` and no charge, entitlement or distribution exists; an unknown Fixed Lab is never computed as 0 |
| 48 | **Ortho terms:** Doctor stores the initial terms once, during the Visit (no charge or installment exists until finalize); the Manager's and Sales' `POST /visits/{id}/orthodontic-cases` terms → 403; the Manager edits terms later (audited); Sales cannot edit terms |
| 49 | **Sales commission rate:** the Manager creates a Sales employee with a required rate (missing → 400); a Lead-sourced collection of 1,000 at 5% yields a commission item of 50; after the Manager changes the rate to 7% the next collection of 1,000 yields 70 (total 120); the first item still shows 5% / 50; a Draft or finalized payroll is not recalculated; a Walk-In / Direct collection yields 0 and no item; Doctor and Sales get 403 on the route |
| 50 | **Feedback integration:** Sales enters feedback for a Visit that has a request → the entry links the request, the request reads Submitted, the public GET returns the neutral already-submitted state and the public POST returns `feedback.already_submitted` (no second entry); a patient submission first → the Sales entry for that Visit gets 409; a race creates exactly one entry; Sales general feedback without a Visit is accepted with NULL Visit and NULL request |
| 51 | **Allowed categories:** Sales `GET /expenses/allowed-categories` returns only active `sales_can_record` categories (id, name); Sales cannot create / edit / deactivate categories (403); Sales `POST /expenses` in a category outside the list → 422/403; Doctor 403 on the lookup |
| 52 | **Contract hygiene:** the OpenAPI document contains no Manager route that creates an initial price / Fixed Lab / Ortho terms, no route that recalculates commission, no feedback-status write route, and no `separatelyPricedWork` flag |
| 53 | **Generic adjustment loophole:** the Manager's `POST /charges/{id}/adjustments` (`DiscountCorrection` or `PriceCorrection`) on an ortho charge → 422 `ortho.use_terms_edit`, no `charge_adjustments` row; the same call on a non-ortho charge → 201; no client can submit `OrthoTermsChange`; the OpenAPI document has no second route that writes an Ortho Total, Deposit or Months |
| 54 | **Terms edit:** Gross 20,000 → 24,000 with an established Discount of 2,000 → 200, Billed 18,000 → 22,000, one `PriceChange` installment of 4,000, Discount unchanged, existing installments unchanged; Gross 20,000 → 18,000 → 422 `ortho.total_decrease_not_supported`, nothing written, no negative installment; Deposit 5,000 → 7,000 → 200 with `requiresScheduleAdjustment: true`, installments and applications byte-identical; a Deposit above the Effective Total → 422 `ortho.deposit_exceeds_effective_total`; Months change → 200, nothing regenerated, added, removed or redistributed, `monthPlanDifference` ≠ 0; Months below the paid regular installments → 422 `ortho.months_conflict_with_paid_history`; Sales → 403, Doctor (after the initial entry) → 403; before finalize → 422 `ortho.charge_not_established` |
| 55 | **Schedule batch:** a batch that ends with Deposit + Σ active = Effective Ortho Total **and** active regular installments = Months → 200 and `requiresScheduleAdjustment: false`; unbalanced → 422 `ortho.schedule_unbalanced`; wrong month plan → 422 `ortho.month_plan_mismatch` (both write nothing); an amount below `amountAlreadyPaid` → 422 `ortho.installment_below_paid_amount`; retiring or re-dating an installment with any payment → 422 `ortho.installment_has_payment_history`; a negative amount → 422; an unpaid installment set to 0 → 200; `add` creates a `ManagerAdded` installment with the Manager, time and reason and no payments; `retire` marks an unpaid installment inactive and keeps it in the history; no route creates (outside the batch) or deletes an installment; Sales and Doctor → 403; every revision is audited with previous and new amount / due date, actor, time and reason |
| 56 | **Payments around terms edits:** Sales and Manager still apply payments one→one, one→many, partial, early and future after a Deposit / Months edit; allocations and applications are identical before and after a terms edit; no oldest-first auto-allocation; reversal / refund / correction of an ortho payment still succeeds through the Manager's payment routes and changes no term or installment amount |
| 57 | **Ortho discount arithmetic and timing:** Gross 20,000, Discount 2,000, Deposit 5,000, Months 5: `POST …/orthodontic-cases` creates no charge and no installment; finalize returns Billed 18,000 (original 20,000, discount 2,000) and five installments summing to 13,000 (5,000 + 13,000 = 18,000, no extra 2,000 outstanding); Gross 20,000, Discount 18,000, Deposit 5,000 → finalize 422 `ortho.deposit_exceeds_effective_total`, the Visit stays unfinalized; generic `POST /charges/{id}/adjustments` (`DiscountCorrection`) → 422 `ortho.use_terms_edit` |
| 58 | **Months 10 → 12 and 10 → 8:** `PUT …/terms` creates and removes no rows; the read shows `requiresScheduleAdjustment: true` and `monthPlanDifference` +2 / −2; the batch adds two `ManagerAdded` installments (or retires two unpaid ones) with rebalanced amounts → valid; a `PriceChange` installment does not count as regular; the Months field is never dead metadata |

### A12. Small technical defaults (veto-able)

Money as strings; no URL version segment; strict unknown-member rejection; patient search 60/min; Doctor follow-up booked with the authoring Doctor (if a Doctor must book with a different Doctor, one optional field is added); arrival allowed only on the appointment's clinic-local day; the Manager's assignment of unassigned work is a filter on the ordinary routes. Two items are OPEN at the Owner level and are not designed here: a total-price decrease for Orthodontic cases (Q-ORT-3b) and the physical key-custody locations (OPS-1); neither affects any route.

---

## PART B: Notifications and background jobs (Step 10)

### B1. Confirmed requirements implemented

A reusable Notification module whose initial recipients are internal clinic users, especially Sales. Seven types: Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral, Outstanding Payment Follow-Up, Pending Follow-Up, Feedback Request Pending. Record fields: type, recipient user and role, related patient, entity type and ID, scheduled/sent/read time, status, retry count. Processing is idempotent, retry-safe, timezone-aware and duplicate-resistant; the clinic timezone is configurable. Appointments store reminder scheduled-at, sent-at and status (on `notifications`). **Doctors have no notification inbox in V1; no notification targets a Doctor.** Feedback links are sent manually by Sales in V1 (a Sales-entered feedback path exists in parallel and creates no notification); external channels (WhatsApp, SMS, email) are future work behind an abstraction. No Inventory jobs, no X-ray jobs.

### B2. Module design (TECHNICAL)

`Notifications.Contracts` (Level 2): `EnqueueForRole`, `EnqueueForUser`, `INotificationSourceValidator`, `INotificationRenderer`, `INotificationChannel`. Planner jobs live in the **owning module** (Scheduling, Orthodontics, Crm, Feedback). Source modules implement the Notifications-defined ports and register them in DI; the dispatcher finds them at runtime (no upward compile-time dependency). `InAppChannel` is the only V1 channel ("delivered" = the row becomes visible in the inbox). There is no password-reset message (Q-ACC-1). Assistants are never recipients.

### B3. Recipients (TECHNICAL, literal reading)

| Type | Recipients |
|---|---|
| Appointment Tomorrow, Implant Follow-Up Tomorrow, Orthodontic Installment Due Tomorrow, New Doctor Referral, Feedback Request Pending | All active Sales / Reception users |
| Pending Follow-Up, Outstanding Payment Follow-Up | The follow-up's assigned Sales employee |

One row per recipient (read-state per person). The Manager has read access but no confirmed type targets the Manager. **Inactive owner:** an inactive Sales employee receives nothing; their open Leads and Follow-Ups were **redistributed evenly among Active Sales at deactivation (Q-EMP-1)**, so the assigned employee of an open follow-up is Active, or the follow-up is **unassigned** (no Active Sales existed): an unassigned follow-up notifies nobody until the Manager assigns it. The same `staff.v_sales_employee_activity` directory answers this in J3, the dispatcher and J7. Attendance and overtime never create notifications.

### B4. Lifecycle (TECHNICAL)

`Pending → Sent` (read_at set on open; status stays Sent); `Pending → Cancelled` (source no longer applicable, or recipient inactive); `Failed` (delivery error) `→ Pending` via retry while `retry_count < 5`. Inbox shows `Sent` rows for the signed-in user only. Already-sent reminders are never rewritten; the inbox renders them live from the source entity. Rendering is at display time under the viewer's permissions by the source module's `INotificationRenderer`, returning only the Sales whitelist.

### B5. Idempotency, timezone (TECHNICAL)

Dedupe key (unique): `{type}:{entity_id}:{clinic-local target date}:{recipient_user_id}`; inserts use `ON CONFLICT (dedupe_key) DO NOTHING`; planners use `DisableConcurrentExecution`. A rescheduled appointment has a new id, hence a new key; a new Sales user gets tomorrow's reminders on the next run; a follow-up due-date change notifies again. Time goes through one `IClinicClock` reading `configuration.settings.clinic_timezone` (IANA tz via NodaTime; DEFAULT `Africa/Cairo`; missing/invalid → jobs fail loudly and guess nothing). "Tomorrow" is the clinic-local calendar date; appointment queries use a half-open UTC range built from the local day (23/25-hour days work); installments compare clinic-local dates. A timezone change is audited.

### B6. Jobs (TECHNICAL)

All jobs run only in the Worker (Hangfire, PostgreSQL storage). Arguments are ids only. Every job is safe to re-run.

| Job | Frequency | Selects / behavior |
|---|---|---|
| **J1 Appointment reminders** (Scheduling) | 15 min | Appointments on tomorrow's clinic-local date, status not Cancelled/Rescheduled; `kind = Standard` → Appointment Tomorrow; `ImplantFollowUp` → Implant Follow-Up Tomorrow |
| **J2 Ortho installment reminders** (Orthodontics) | 15 min | `current due date = tomorrow`, the installment is **active** (a retired installment is never reminded) **and the installment's ortho charge has Outstanding > 0** (a fully paid ortho charge gets no reminder; a patient who prepaid some installments may still be reminded, and the notification shows the charge-level balance) |
| **J3 Follow-up due** (Crm) | 15 min | `due_date ≤ today` and the follow-up is **open** (`status <> 'FollowedUp'`, i.e. جديد / تم التواصل / لم يرد); `follow_up_type = RemainingBalance` → Outstanding Payment Follow-Up; others → Pending Follow-Up. Notifies the assigned (responsible) Sales employee; a follow-up with a NULL responsible employee is skipped and counted by J7. |
| **J4 Dispatcher** (Notifications) | every minute | See below |
| **J5 Retry** (Notifications) | 5 min | `UPDATE notifications SET status='Pending' WHERE status='Failed' AND retry_count < 5`, in batches. `retry_count` counts **failed attempts** and is incremented by the dispatcher at failure time. After the 5th failure it stays Failed and the exhausted-retries alert fires. Same row, same `dedupe_key`; fully re-validated through the dispatcher path. |
| ~~J6~~ | — | **Removed (Q-FDB-1):** feedback requests never expire; no expiry job, no Expired status. The identifier J6 is not reused. |
| **J7 Operations health** (Notifications, Worker) | 5 min | Job freshness/Worker heartbeat; dispatcher lag; exhausted retries; Hangfire failed-job count; count of **open** follow-ups assigned to an inactive Sales employee (should be 0 after redistribution; a non-zero count is an integrity alert; the count of **unassigned** open Leads / Follow-Ups is reported as well (informational: the Manager sees them); produced by **Crm** through a Notifications-defined probe port using `Staff.Contracts.IActiveSalesEmployeeDirectory`; a count with no names or ids). Pings success, or failure with a fixed message and counts; never names, ids or text. |

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
| 9 | Open follow-ups (New / Contacted / NoResponse) produce one notification per due date to the assigned employee only; FollowedUp produces none; after a Sales deactivation the redistributed follow-ups notify their new owners; unassigned follow-ups notify nobody and appear in the J7 count |
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
| 25 | J7 reports the inactive-owner count using only the staff view; the Worker cannot SELECT salary, commission or any column of `staff.sales_employees` / `staff.doctors` |
| 26 | J3 binds to the confirmed open predicate; J7 reports zero open follow-ups owned by inactive Sales after a deactivation; the dispatcher and J7 use the same directory |
| 27 | No J6 job and no feedback-expiry or feedback-reminder job is registered in Hangfire; the recurring job list equals exactly J1–J5 and J7; attendance, overtime and Sales-entered feedback create no notification |

### B10. Open and neutral items

No notification-related business question is open. Defaults to veto: 15-minute planners, reminder availability from the start of the previous local day, retry cap 5, no reminder for fully paid ortho charges, `Africa/Cairo` default timezone. Notification and print texts are Arabic. No Inventory and no X-ray appear anywhere.
