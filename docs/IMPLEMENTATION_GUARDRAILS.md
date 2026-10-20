READ THIS FILE BEFORE IMPLEMENTING ANY FEATURE.

> **THIS DOCUMENT IMPLEMENTS THE FROZEN DESIGN BASELINE v1.1** (OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION, 2026-10-20). A coding agent must **not reinterpret, simplify or replace confirmed business rules**. If implementation discovers a conflict: **STOP → report the conflict → do not invent business behavior → resolve it through a controlled design change** (DESIGN_FREEZE.md §7).

# IMPLEMENTATION_GUARDRAILS.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)

These are the rules that stop the implementation from drifting away from the approved design. They are short on purpose. Each rule points to the document that holds the detail. If a rule here seems to conflict with a feature request, **stop and report it**; do not work around it.

---

## 0. The prime rule

**DO NOT BE CREATIVE WITH OUR BUSINESS. BE CREATIVE WITH THE ENGINEERING NEEDED TO IMPLEMENT IT WELL.**

Do not invent: workflows, permissions, finance rules, approval steps, automatic allocations, statuses, ownership rules, user capabilities, notifications, reports, or fields. If the design is silent, the answer is **not built**, not "a sensible default". Two items remain deferred in OPEN_QUESTIONS.md and are **not designed, not defaulted and not built**: **Q-ORT-3b** (decreasing the total price of an Orthodontic case: DEFERRED / OPEN FOR FUTURE VERSION; V1 rejects it with `ortho.total_decrease_not_supported`, and it blocks nothing) and **OPS-1** (the physical storage locations of the two backup recovery keys, DEFERRED). Everything else is an Owner decision recorded in OWNER_REVIEW_NOTES.md.

If you find a genuine problem, report it in this form and wait:

```
SERIOUS ISSUE:
WHY:
MINIMUM RECOMMENDED FIX:
DECISION REQUIRED:
```

## 1. Source priority when documents seem to disagree

1. The latest explicit Owner decision (the final Owner business changes and the Owner Review decisions; OWNER_REVIEW_NOTES.md, DESIGN_FREEZE.md). 2. The latest approved revision of a Step. 3. An earlier approved Step. 4. The original requirements. 5. A recommendation. Report any contradiction as a `DESIGN FREEZE CONFLICT` (document, section, conflict, latest approved rule, minimum correction). Correct only when the latest rule is unambiguous.

## 2. Scope: never add these

- **No Inventory. No X-ray.** Not as a module, table, column, permission, job, enum value, label or test. "Medical Supplies" exists only as an expense category row.
- **No Assistant login, account, user_id, application role, dashboard, API audience, self-check-in or notification.** An Assistant is a staff type, not an application role; the application roles are only Manager, Doctor and Sales / Reception. The **Manager** maintains the Assistant's attendance, overtime hours, overtime rate, salary and payroll through Manager-only routes. No bonus, deduction, commission, incentive or automatic salary proration exists for Assistants; do not add one.
- No patient login, portal or mobile app. No insurance, formal accounting, multi-branch, automated WhatsApp/SMS/email in V1.
- None of the unconfirmed earlier proposals: `ILabAllocationStrategy`, `CaseDoctorAllocation`, a TreatmentPlan/FinancialAgreement workflow, OnHold/Abandoned case states, addendum workflows, `PatientContact` as a business aggregate, Manager approval workflows, price lists, deviation reports, Doctor-share clawback rules.

- **Scheduling (Owner-confirmed).** The appointment statuses are exactly seven: Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled. Patient arrival is **an event, not a status**: `scheduling.appointment_arrivals` (insert-only, recorded by Sales only), and the Doctor queue is derived from arrivals and the seven statuses. Never add an Arrived / Waiting / Checked-In status or a Check-In permission label. Appointments carry a manual `duration_minutes NOT NULL`; **overlapping appointments are allowed** (no exclusion constraint, no same-slot unique index, no default duration). When the Visit is finalized (`visit_ended_at`), unused booked time is released through a view. **Sales and Manager** may Confirm, Cancel and mark No Show.
- **Also not built (Owner-confirmed):** Daily Closing, printed receipts and invoices, consent forms, patient attachments, expense attachments, password-reset tokens, multi-branch (no `branch_id` anywhere), any language other than Arabic RTL, expense finalization, feedback expiry, a Walk-In commission pool, Assistant or Sales self-check-in, Doctor attendance, a percentage discount, `separatelyPricedWork`, attribution holds, a price decrease for Orthodontic cases (OPEN, not designed).

## 3. Architecture rules (ADR-001, 002, 003)

1. Modular monolith. One database, one DbContext, one schema per module. No microservices, message broker, event sourcing, CQRS framework or Kubernetes.
2. Module code is `internal`; the only public surface is `Contracts`. **Dependencies point only to a lower level** (Audit/Configuration/Identity → Staff/Notifications/Expenses → Patients → Scheduling/Crm/Finance → Clinical → Prescriptions/Orthodontics/Feedback → Payroll/Reporting). Architecture tests must fail on a violation; do not weaken them.
3. **Finance never calls Clinical and subscribes to no events.** At Visit finalize, Clinical calls the Finance and Scheduling contracts downward in one transaction (charges from Price − Discount; release of unused booked time). The pre-finalization discount is Clinical's own `pricing_discount_entries`; Sales reaches it only through `PricingLineSalesDto` (ADR-017).
4. **No cross-module EF navigation properties. Keep database foreign keys across modules**, declared in `CrossModuleReferences.cs`. Never create a duplicate table/entity to avoid a dependency.
5. Every table belongs to exactly one module's schema; migrations are named `<date>_<Module>_<Change>` (`Xref` for cross-module FKs).
6. Reporting reads other modules read-only; nothing depends on Reporting.

## 4. Privacy rules (ADR-004) — highest-risk area

1. **A Doctor never receives** phone, WhatsApp, email, address, CRM/contact notes, appointment `sales_notes`, or the Original Sales Representative: not in a DTO, OpenAPI document, search, PDF, CSV, notification, log, error body or audit view. Doctors search by Patient ID and name only.
2. **Sales never receives** Doctor-authored clinical information: clinical notes, root canal measurements, detailed treatment measurements, prescription medication details, **the Doctor's referral `notes`**, nor Doctor percentage, Doctor Share, entitlement, Clinic Share, commission rules of others or lab figures. Sales reads clinical data only through the whitelist summary contract, with deliberate exceptions: the structured Patient Medical History Intake (section 4a), the Referral **reason** and destination / type, the patient's **date of birth**, and the price / discount / total of a pricing line (`PricingLineSalesDto`).
3. Every response is a projection into an **audience DTO**. Never serialize an entity. Never bind a request to an entity. Unknown request members are rejected.
4. Contact-bearing types carry `[ContactData]`; nothing under `Features/Doctor` may reference them.
5. Search involving names or phones is a **POST body**, never a query string. No personal data in URLs, logs, alerts, pings, error-tracker events, file names or Hangfire arguments (ids only).
6. Write canary tests for any new endpoint, export or notification.

## 4a. Patient Medical History Intake rules (v1.1)

1. The Patient Medical History Intake is **structured patient-level data** (`patients.patient_medical_histories`, Patients module, one current record per patient). Its questions are exactly: Allergy, Hypertension, Viral Hepatitis, Diabetes, Kidney Problems, Bleeding / Clotting Problems, Heart Problems, Rheumatic Diseases, Smoker, Previous Surgical Operations, Taking Medications, Last Dental Visit (text), Pregnant, Breastfeeding.
2. **Sales / Reception is allowed to collect and edit this intake** (`Patients.MedicalHistory.Read` + `EditIntake`). There is no separate Reception role.
3. **Sales access to the intake does NOT grant access to detailed clinical records** (no notes, measurements, prescription details, Doctor percentage, shares or lab).
4. **A Doctor may read the intake** (`Patients.MedicalHistory.Read` only; no write route) **but still receives no patient contact data**.
5. **Do not create duplicate generic Medical History / Chronic Diseases / Previous Surgeries sources of truth.** Chief Complaint stays a separate field on the patient.
6. **Do not invent** medication names, surgery details, allergy types, condition details or an "other condition" field (NOT CONFIRMED / NOT DESIGNED).
7. **Never silently convert an unanswered medical question into "No".** Flags are nullable (Yes / No / Not answered); the API returns `null`, not `false`.
8. Pregnancy and breastfeeding are FOR FEMALE PATIENTS ONLY questions. V1 stores no Patient sex field (accepted limitation); Sales / Reception applies the section when relevant. Not applicable or unanswered stays NULL, never FALSE, and these answers are never required.
9. **Medical History changes are audited** (actor, time, patient id, changed field names, never values).
10. **Never put Medical History values into logs, notification payloads, the audit log values, reports or exports.** Notification text never names a condition (for example diabetes, hepatitis or pregnancy).
11. **Appointment booking never depends on Medical History completion** (a patient may be registered and booked with no intake yet; the Doctor reads the intake if it exists). The intake is **not tied to any appointment state** (no Arrived, Waiting or check-in) and the Assistant has no access.

## 5. Security rules (ADR-005, 006)

1. Authorization is enforced on the backend: authentication → endpoint permission → resource scope → audience DTO. React guards are usability only.
2. **One operational role per account.** Never compute a union of roles. Never map a contact-bearing key to DOCTOR.
3. Every mapped endpoint declares a permission key (or `[AuthenticatedOnly]`, or is one of the **six** anonymous endpoints). A mapped route whose key is assigned to no role fails the build.
4. Every permission key is assigned to at least one role: `Finance.Discount.Set` → Doctor (own line), Sales and Manager; `Finance.PricingLines.Read` → Sales and Manager; `Expenses.CreateOperational` → Sales and Manager (create only); `Expenses.ReadAll` / `Expenses.Manage` / `ExpenseCategories.Manage`, `Staff.Attendance.Manage`, `Staff.AssistantOvertime.Manage`, `Staff.AssistantOvertimeRate.Configure`, `Reports.Attendance`, `Crm.Responsibility.Assign` and `Feedback.Results.Read` → Manager only; `Feedback.Entries.Record` → Sales and Manager; the **initial** entries `Clinical.WorkItems.EnterPrice`, `Clinical.Visits.EnterConsultationFee`, `Finance.FixedLab.SetInitial` and `Orthodontics.Terms.SetInitial` → Doctor (own work) only; `Finance.FixedLab.Revise`, `Orthodontics.Terms.Edit`, `Orthodontics.Installments.Edit`, `Staff.SalesCommission.Configure`, payment corrections and adjustments → Manager only; `Expenses.Categories.ReadAllowed` → Sales and Manager; `Orthodontics.Installments.ApplyPayment` → Sales and Manager. No key may be mapped without a role.
5. "Own" endpoints take the identity from the principal; no `doctorId`/`salesEmployeeId` parameter. A scoped resource that is not the caller's returns **404**.
6. Doctor read breadth does not imply write rights: a Doctor writes only their own editable visit/work. Doctors have **no notification inbox** (`Notifications.Read.Own` is Sales and Manager only).
7. Patient registration requires **both** `Patients.Identity.Write` **and** `Patients.Contact.Write`.
8. Never log or audit passwords, hashes, stamps, reset tokens or feedback tokens. A password change cannot be reconstructed from audit.
9. Database roles: `clinic_app` (no DELETE, no UPDATE on insert-only tables, SELECT+INSERT only on `infra.data_protection_keys`), `clinic_worker` (minimal grants, only the two minimal views for identity/staff lookups), `clinic_backup` (explicit SELECTs, **no `pg_read_all_data`, no key table**), `clinic_migrator` (DDL, migrator container only). Every new table/column needs an explicit **grant decision and a backup-grant decision** in its migration.
10. Accounts are created by the Manager; passwords are set only through Manager **Set Password** (hash only). No reset tokens, no recovery-initiation endpoint, no self-service reset. Break-glass `set-manager-password` is an operator command, not an endpoint.

## 6. Finance rules (ADR-007 to ADR-010) — second highest risk

1. **Charge ≠ Payment.** **Billed, Collected, Outstanding are derived**; never store a balance column. Charges are created at Visit finalize as Price − Discount (fixed EGP, never percentages) per Doctor work item; the **Doctor** sets the initial price (and for Fixed / Removable Prosthetics also enters the initial fixed lab amount); the Manager has no initial-price authority and only makes audited corrections; the discount may be set by the Doctor, Sales or Manager (setter, role and time are stored).
2. Ledger tables are **insert-only**. A correction is a new row. Payment corrections (Reversal, Refund, Reallocation, Replacement) are **Manager-only** `payment_corrections`; Manager adjustments are Manager-only; after a Charge exists only the Manager changes a discount.
3. `RecordPayment` is **one transaction**: idempotency key (unique), ordered `FOR UPDATE` locks on charge rows (id order), payment, allocations (**manual**; remainder → Patient Credit, consumed FIFO), earnings/distributions, audit. G8: payment = Σ allocations + Σ credit entries. No oldest-first, no automatic split.
4. **Entitlement ≠ recognition.** `doctor_entitlements` holds the Doctor's maximum (net charge × snapshot %). **General lab:** doctor share, lab (rate **at allocation time**) and clinic are recognized immediately. **Fixed lab:** deterministic **Lab-First waterfall** with a FIFO pending-entitlement queue; Reconcile is idempotent and emits only delta rows. Clinic Share is never negative. G8b: lab + clinic + doctor earnings = allocation basis; 0 ≤ Σ earnings ≤ Σ entitlements.
5. The Doctor percentage comes from the work-item snapshot, never the Doctor's current percentage. `doctor_earnings.recognized_at` decides the payroll month.
6. **One fixed lab per case** (`fixed_lab_expenses` unique per case; revisions insert-only). The Doctor enters revision 1 (`Finance.FixedLab.SetInitial`) **before the first Fixed-lab Treatment charge is established**; an unknown Fixed Lab is **never L = 0**; the Manager only revises (`Finance.FixedLab.Revise`). Never attach the fixed lab to a charge, payment or Doctor. A revision triggers Reconcile and lands in the current open payroll month; past rows are not rewritten.
7. Consultation charges produce no earning and no distribution; they are Clinic revenue and a separate report line.
8. Financial ownership is on the **per-Doctor work item**. There are no attribution holds, no pending-attribution rows, and no PatientReassigned / DoctorDeactivated finance handlers; reassignment and deactivation never move history.
9. **Orthodontics:** the **Doctor** enters the initial **Gross** Total Price, Deposit and Months **during the editable Visit** (`Orthodontics.Terms.SetInitial`, Doctor only; stored, nothing else created). The fixed-EGP Discount is set before finalization. **At Visit finalization** the Discount is snapshotted, **Effective Ortho Total = Gross − Discount = Ortho Charge Billed**, `0 ≤ Deposit ≤ Effective` is validated (`ortho.deposit_exceeds_effective_total`, never a silent change), the Charge is established and the equal initial installments are generated **once** from `Effective − Deposit` (last absorbs rounding); never earlier. Sales cannot set terms. Later term edits (`Orthodontics.Terms.Edit`, the **only** way to change an established Gross Total / Deposit / Months) and schedule edits (`Orthodontics.Installments.Edit`: amount, due-date revision, manual addition, safe retirement; atomic batch) are **Manager-only**, audited, through `ortho_term_revisions` / `ortho_installment_revisions`. A valid schedule satisfies **Deposit + Σ active installments = Effective Ortho Total** (never the undiscounted Gross) **and** active regular installments = Number of Months. After a Deposit or Months edit nothing is regenerated, added, removed or redistributed automatically: the case is flagged `requires_schedule_adjustment` (derived, not a status) and the Manager adjusts the schedule manually until it is valid. Installments are never deleted; retirement is a revision and only for unpaid installments. Payments allocate to the ortho charge; Sales or Manager manually apply them to installments through `installment_payments` (no oldest-first, never regenerated by a terms edit). A Gross increase adds one `PriceChange` difference installment (the Discount snapshot is not re-applied); a decrease is rejected in V1; generic `Finance.Charges.Adjust` can never change an Ortho Total (`ortho.use_terms_edit`).
10. **Payroll** is monthly (calendar month), Draft → Finalized → Paid; finalize only after month end (G2). Doctor payroll aggregates `doctor_earnings` by `recognized_at`. **Sales commission** base = net allocations (collected money after discount and corrections) with `allocated_at < deactivated_at`, only for Lead-sourced patients with an Original Sales Employee; **Walk-In / Direct patients earn zero commission and there is no pool and no pool table**. The Manager sets the commission rate at creation and may change it later; the **rate at collection time** applies (effective-dated `sales_commission_rate_history` + per-item snapshot): a new rate affects **future collections only**. Sealed rows and old collections are never recalculated.
11. **Assistant payroll.** Staff only (`staff.assistants`, no `user_id`). **Final = Base Pay + Overtime.** Base Pay defaults to the salary snapshot for a normal full month; for a partial or mid-change month the Manager enters Base Pay manually with a reason code; there is **no /30 and no automatic proration**. Overtime = approved hours × the hourly-rate **snapshot** on the entry (rate configured by the Manager; a later rate change never recalculates an old entry or payroll item); entries are void-only and belong to at most one payroll. **Attendance alone deducts nothing** and never creates overtime. Salary change = `Staff.AssistantSalary.Configure`; overtime rate = `Staff.AssistantOvertimeRate.Configure`; overtime entries = `Staff.AssistantOvertime.Manage`; attendance = `Staff.Attendance.Manage`; payroll = `Payroll.Assistant.ManageAll`. Assistant and attendance data are never exposed to Doctor or Sales, Sales has no overtime behavior, and none of it affects Doctor Share. Reports show Assistant Base Pay and Overtime only in monthly ranges.
12. **Operating result** = Clinic Gross Revenue − Sales payroll − Assistant payroll − Clinic operating expenses − other Clinic-owned expenses. Doctor Payroll is never subtracted again; no double subtraction. Assistant Payroll is its own report line.
13. Money: `numeric(12,2)`, `decimal` in code, **strings in the API**. Percentages `numeric(5,2)`. No floating point.
14. Expenses are **never finalized** (no `finalized_at`); the Manager edits them directly and every edit is audited. Sales may only **create** Materials / Medical Supplies and Petty / Miscellaneous expenses (`Expenses.CreateOperational`; category flag `sales_can_record`; read-only `GET /expenses/allowed-categories`; no list, edit, category management or reports); the creator (`created_by_user_id`, `created_at`) is never overwritten by a Manager edit; no attachments exist.
15. **Fixed-lab entitlement ledger.** Entitlement is fixed at charge creation (net charge × snapshot %) and never changes with a lab revision; only recognition moves. Replays must reproduce the Owner examples exactly (FINANCE_DESIGN.md §17, §18).
16. **Patient Credit** is a liability: a credit row creates no Doctor, Lab, Clinic or Sales revenue until applied to a Charge. Refunds and corrections are Manager-only.

## 7. Data rules (ADR-011)

1. No physical deletes in business schemas. Use `voided_at`, `removed_at`, `withdrawn_at`, `is_active`.
2. Respect table classes: **H** insert-only, **L** only named columns change, **M** mutable and audited. Guards **G1, G2, G4–G8, G8b** are mandatory and may not be bypassed (G3 is retired); `SET LOCAL clinic.audited_correction` is set in exactly one class.
3. **Prescriptions and visit records are editable until their visit is finalized**, then protected by G1. After finalization only the **controlled Doctor correction** is allowed, requiring a `clinical.visit_corrections` row with reason and audit. Payroll immutability is financial immutability: Draft→Finalized→Paid transitions stay possible.
4. Reassignment never moves history (past work references the performing Doctor, not assignment rows). Appointment changes create a new appointment and mark the old Rescheduled; never update the old row's time.
5. Orthodontics: the total lives in the single ortho charge; initial terms are entered by the Doctor; later term revisions and installment amounts are Manager-only append rows; an ortho case has exactly one work item and one charge.
6. `patient_contacts.patient_source` is WalkIn (includes Direct) or Lead (CHECK). Leads have a nullable `responsible_sales_employee_id` with `responsibility_history`; on Sales deactivation open Leads and open Follow-Ups are redistributed evenly among Active Sales, and **with no Active Sales they become unassigned (NULL), visible and assignable by the Manager only** — the deactivation is never refused. Follow-up statuses are New / Contacted / NoResponse / FollowedUp (open = anything other than FollowedUp). Referral statuses New / Contacted / Booked / Completed / Lost remain.
7. Age: exact age only from a real date of birth; a recorded age is shown as recorded with its date and never aged forward.
8. Overlapping appointments are allowed; duration is manual and required. Do not add overlap prevention or default durations.
9. Feedback has two paths into one table (`feedback_entries.source` = `PatientWeb` via the non-expiring token, or `SalesEntered` by Sales / Manager); only the Manager reads results; Doctors and Sales receive none; feedback is never written to a clinical table. Attendance is one record per person per day, Manager-maintained.

## 8. Notification and job rules (ADR-012)

1. Jobs run only in the Worker. Arguments are ids only. Every job is idempotent (dedupe key, `ON CONFLICT DO NOTHING`).
2. "Tomorrow" is the clinic-local calendar date via `IClinicClock`; never hard-code offsets or the timezone.
3. The dispatcher processes **each notification in its own transaction** (`SKIP LOCKED`); a failure is committed as `Failed` in a new transaction; retry reuses the same row.
4. The Worker resolves users only through `identity.v_active_user_roles` and `staff.v_sales_employee_activity`. Never grant it `identity.users`, `staff.*` tables, contacts or clinical tables. No hardcoded role UUIDs.
5. No notification targets a Doctor. Reset messages are never stored in `notifications`.
6. There is no J6 and no feedback expiry. J2 selects installments due tomorrow whose ortho charge has Outstanding > 0.

## 9. API rules (ADR-013)

Audience prefixes and one OpenAPI document per audience; money as strings; RFC 7807 errors with stable codes and no echoed input; `If-Match` required on editable resources; `Idempotency-Key` required on payments (same key + different payload → 409); pagination max 100; whitelisted sort/filter; Swagger off in Production. The Manager receives the audience's DTO on shared routes. Confirm / Cancel / No Show are available to Sales and Manager. `SalesReferralDto` has no `notes` property. The SPA is Arabic RTL only. Six anonymous endpoints only (including public-token feedback without expiry).

## 10. Deployment and operations rules (ADR-014, 015)

1. **The application never migrates itself.** Only the migrator, with `clinic_migrator`, runs migrations. Expand/contract only; the destructive-operation scan and the N-1 compatibility job must pass. Readiness checks `required − applied`, not equality. Non-transactional migrations require `[NonTransactionalMigration]`, single purpose, retry-safe statements.
2. Secrets are Docker secrets, never in the repository, images or CI logs. `dp_cert` only in `api`; the Worker gets no Data Protection access; the migrator password is never in `api` or `worker`.
3. Staging and Development use **synthetic data only**. Production data is never copied down. Restore drills use real data in a throwaway production-grade environment, never in staging.
4. Every restore to an earlier point is followed by `post-restore-lockdown` (rotate stamps and lock every non-Manager account; the Manager then sets passwords). Single branch. Hosting is DigitalOcean / Frankfurt (Droplet of about 2 vCPU / 4 GB, configurable, plus Managed PostgreSQL); RPO 5 min and RTO 4 h for the realistic failures, with the provider-loss scenario honestly outside them. Primary operator: project developer / system owner; backup critical-alert contact: Clinic Manager.
5. Backups: client-side `age` encryption; the Clinic Manager (a person, not an application permission) holds the recovery keys and the physical storage locations are **OPS-1 (DEFERRED)** — do not design them; no private key on the VM; write-only upload credentials; dump excludes the key-table data.
6. Alerts, pings and error events contain no personal data.

## 11. Testing rules

1. Implement the test lists in SECURITY_DESIGN.md (1–72), FINANCE_DESIGN.md (1–38), API_ARCHITECTURE.md (A11 1–58, B9 1–27), the DATABASE_DESIGN.md test paragraphs and DEPLOYMENT_ARCHITECTURE.md (2.10, 3.7) together with the feature, not afterwards. Section 13 below maps every Owner-required test to them.
2. Integration tests run against real PostgreSQL with the real role bootstrap script so grants and triggers G1, G2, G4–G8b are exercised.
3. Canary scans over every Doctor and Sales response, PDF, CSV, notification and log. Generated role-matrix test over every mapped route.
4. Tests assert the **negative** open-question behavior (e.g. "no Arrived/Waiting status exists", "no reset-token route is mapped", "no Daily Closing, receipt, consent or attachment route exists", "no attribution hold is created").

## 12. Before you start a feature: checklist

- [ ] I read the section of the design documents that covers this feature.
- [ ] I checked OPEN_QUESTIONS.md (only Q-ORT-3b, DEFERRED with a defined V1 rejection, and OPS-1 remain) and implemented only the confirmed behavior.
- [ ] No Inventory, no X-ray, no invented rule, status, permission or field.
- [ ] Dependencies point only downward; Finance does not call Clinical.
- [ ] Every table has an owning schema, grant decision and backup-grant decision; cross-module references are database FKs.
- [ ] DTOs are audience-specific; Doctor contains no contact data; Sales contains no clinical detail and no referral notes.
- [ ] Money paths are insert-only, idempotent, transactional, and tested with the invariant checks.
- [ ] The feature's tests (including canary and negative tests) are written.
- [ ] If anything above cannot be satisfied: reported as `SERIOUS ISSUE` / `DESIGN FREEZE CONFLICT`, not worked around.

## 13. Test traceability: the 94 Owner-required tests (plus the 20 final-correction tests FC-1 … FC-20 below)

Every test the Owner requires has at least one concrete test in the documents below. SEC = SECURITY_DESIGN.md §tests, FIN = FINANCE_DESIGN.md §18, API = API_ARCHITECTURE.md (A11 and B9), DB = DATABASE_DESIGN.md test paragraphs, DEPLOYMENT = DEPLOYMENT_ARCHITECTURE.md. Where an old test asserted the opposite (Doctor-only discount, Walk-In pool, Assistant `final-amount`, redistribution refused with no Active Sales, referral notes visible to Sales) it was **rewritten**, not kept.

| # | Owner-required test | Where it is specified |
|---|---|---|
| 1 | Arabic RTL app contract | API A11 #37; DEPLOYMENT 2.10 |
| 2 | Manual appointment duration | API A11 #9 |
| 3 | Same-Doctor overlap allowed | API A11 #9; DB (no exclusion on appointments) |
| 4 | Original appointment history remains | API A11 #9; DB R1 |
| 5 | Early Visit end releases booked time | API A11 #10 |
| 6 | Arrival recorded by Sales | API A11 #11; SEC #48 |
| 7 | No Arrived / Waiting status | API A11 #14; SEC #48 |
| 8 | Doctor sees own waiting queue | API A11 #12; SEC #57 |
| 9 | Queue has no contact data | API A11 #12; SEC #57 |
| 10 | Visit start removes patient from queue | API A11 #13 |
| 11 | Sales + Manager Confirm | API A11 #14; SEC #53 |
| 12 | Sales + Manager Cancel | API A11 #14; SEC #53 |
| 13 | Sales + Manager No Show | API A11 #14; SEC #53 |
| 14 | Follow-Up exact status predicate | API A11 #15; B9 #9 |
| 15 | Finalize creates treatment Charge | FIN #2; API A11 #16 |
| 16 | Finalize creates Consultation Charge | FIN #3; API A11 #16 |
| 17 | Doctor sets own Price | SEC #32; API A11 #17 |
| 18 | Doctor may set Discount | SEC #35; FIN #18 |
| 19 | Sales sets Discount without clinical access | SEC #35, #60; FIN #18; API A11 #39 |
| 20 | Manager may set Discount | SEC #35; FIN #18 |
| 21 | Fixed EGP only | FIN #2, #18; API A11 #16, #39; DB test (1) of the discount paragraph |
| 22 | General Lab uses payment-time rate | FIN #5 |
| 23 | Fixed Lab funded before Doctor settlement | FIN #6, #7 |
| 24 | Fixed Lab may create pending entitlement | FIN #6, #7 |
| 25 | Later collection settles pending entitlement | FIN #8 |
| 26 | Fixed Lab Clinic Share never negative | FIN #9 |
| 27 | Fixed Lab full-case arithmetic reconciles | FIN #7 (case B); executable proof in FINANCE §17 |
| 28 | Fixed Lab revision creates correction rows | FIN #10 |
| 29 | Reassignment does not change ownership | FIN #11 |
| 30 | Inactive Doctor keeps old-work entitlement | FIN #12 |
| 31 | No attribution hold | FIN #11; API A9 |
| 32 | Overpayment creates Credit | FIN #14; API A11 #18 |
| 33 | Payment before Charge creates Credit | FIN #14 |
| 34 | Credit later applies to a Charge | FIN #14; API A11 #18 |
| 35 | Unused Credit creates no revenue | FIN #14, #24 |
| 36 | Manager-only Refund / Correction | FIN #15; SEC #35; API A11 #19 |
| 37 | Reversal propagates correctly | FIN #15 |
| 38 | Two Doctors, own priced work, same case | FIN #16; API A11 #17 |
| 39 | No `separatelyPricedWork` flag | API A11 #17, A9 |
| 40 | Ortho Doctor initial terms | SEC #34; API A11 #24 |
| 41 | Manager Ortho terms edit | SEC #34; FIN #25; API A11 #24 |
| 42 | Manager installment amount edit | SEC #34; FIN #25; API A11 #24 |
| 43 | Sales / Manager manual installment application | SEC #34; FIN #25; API A11 #24 |
| 44 | Partial installment application | FIN #25; API A11 #24 |
| 45 | Ortho increase creates difference installment | FIN #25, #34; API A11 #24, #54 (decrease: rejected in V1, Q-ORT-3b deferred) |
| 46 | Controlled Doctor correction of own Visit | SEC #31; API A11 #25 |
| 47 | No correction of another Doctor's Visit | SEC #28, #31; API A11 #25 |
| 48 | Walk-In: Original Sales NULL | FIN #20; API A11 #23; DB test (9) |
| 49 | Walk-In collection = zero commission | FIN #20 |
| 50 | No Walk-In shared pool | FIN #20; API A9; DB test (9) |
| 51 | Attributed commission uses collected money | FIN #19 |
| 52 | Inactive Sales gets no new commission | FIN #21 |
| 53 | Open Leads redistribute evenly | API A11 #22; SEC #66; DB test (10) |
| 54 | Open Follow-Ups redistribute evenly | API A11 #22; B9 #9; SEC #66 |
| 55 | Manager All Leads / Sales My Leads | SEC #58; API A11 #22 |
| 56 | Assistant has no user / account / role | SEC #42, #46, #52 |
| 57 | Manager creates Assistant Attendance | SEC #61; API A11 #40 |
| 58 | Manager creates Sales Attendance | SEC #61; API A11 #40 |
| 59 | Duplicate daily Attendance prevented | API A11 #40; DB attendance unique constraints |
| 60 | Attendance alone deducts nothing | FIN #23; API A11 #40 |
| 61 | Overtime = hours × rate snapshot | FIN #23; API A11 #41; DB CHECK on amount |
| 62 | Rate change does not recalculate old overtime | FIN #23; API A11 #41 |
| 63 | Sales has no overtime behavior | SEC #62; API A11 #41 |
| 64 | Full-month base = salary snapshot | FIN #23; API A11 #26 |
| 65 | Partial month: Manager sets Base Pay | FIN #23; API A11 #26 |
| 66 | No /30 or automatic proration | FIN #23; API A11 #26 |
| 67 | Final = Base + Overtime | FIN #23; API A11 #26; DB CHECK |
| 68 | Finalized Assistant payroll immutable | FIN #22, #23; DB G2 |
| 69 | Monthly report: Base + Overtime | FIN #23, #24; API A8 reports |
| 70 | Daily / Weekly do not prorate Assistant | FIN #23; FINANCE §12 |
| 71 | Sales can create Materials / Petty expense | SEC #63; API A11 #42 |
| 72 | Sales gets no Manager reports from it | SEC #63; API A11 #42 |
| 73 | Manager expense edit audited | API A11 #27; SEC #63 |
| 74 | Expense has no Finalized state | API A11 #27; DB test (7) |
| 75 | No expense attachments | API A11 #29; SEC #59; DB test (7) |
| 76 | Sales sees Referral Reason | SEC #54; API A11 #30 |
| 77 | Sales sees Referral Destination / Type | SEC #54; API A11 #30 |
| 78 | Sales does NOT get referral Doctor notes | SEC #64; API A11 #30; DB test (11) |
| 79 | Sales sees DOB | SEC #54; API A11 #30 |
| 80 | Sales still gets no clinical / prescription / shares | SEC #3, #54; API A11 #30, #39 |
| 81 | Website Feedback token submit works | SEC #55; API A11 #28 |
| 82 | Feedback token does not expire | SEC #55; API A11 #28; DB test (8) |
| 83 | No J6 expiry job | API B9 #27 |
| 84 | Sales can enter Patient Feedback | SEC #65; API A11 #43 |
| 85 | Sales-entered Feedback records source / actor | SEC #65; API A11 #43; DB test (8) |
| 86 | Doctor gets no Feedback results | SEC #65; API A11 #43 |
| 87 | No receipt / invoice route | FIN #26; SEC #59; API A11 #29 |
| 88 | No consent feature | SEC #59; API A11 #29 |
| 89 | Inventory absent | SEC #59; API A11 #29 |
| 90 | X-ray absent | SEC #59; API A11 #29 |
| 91 | Single branch, no `branch_id` | DB test (12); SEC #59; API A11 #29 |
| 92 | Account reset sets a NEW password; old one unreadable | SEC #13; API A11 #20 |
| 93 | No Assistant authentication | SEC #42, #46; API A11 #26 |
| 94 | Privacy canary tests pass | SEC #1, #3, #51, #64; API A11 #4 |

### 13b. Final-correction tests FC-1 … FC-20

| # | Test | Where it is specified |
|---|---|---|
| FC-1 | Manager cannot enter the initial work price (403); only audited correction | SEC #32, #67; API A11 #46 |
| FC-2 | Doctor enters the price on an own work item | SEC #32; API A11 #46 |
| FC-3 | Doctor enters the initial Fixed Lab (`SetInitial`) | SEC #33; API A11 #47; FIN #29 |
| FC-4 | Manager revises the Fixed Lab (`Revise`) | SEC #33; API A11 #47; FIN #10 |
| FC-5 | Manager cannot be the initial Fixed Lab actor; finalize without it → `fixed_lab.initial_required` | SEC #33, #67; API A11 #47; FIN #29; DB final-correction test (5) |
| FC-6 | Doctor enters initial Ortho terms; Manager and Sales cannot | SEC #34; API A11 #48; DB (6) |
| FC-7 | Manager edits Ortho terms later (audited) | SEC #34; API A11 #48 |
| FC-8 | Manager can change the Sales commission rate; others 403 | SEC #68; API A11 #49 |
| FC-9 | New rate applies to future collections (5 % → 50, 7 % → 70, total 120) | FIN #30; API A11 #49 |
| FC-10 | Old collections keep the old rate (item snapshot; reversal uses the original rate) | FIN #30; API A11 #49; DB (7) |
| FC-11 | A finalized Sales payroll does not recalculate | FIN #22, #30; DB G2 |
| FC-12 | Walk-In / Direct commission stays 0, no pool | FIN #20, #30; API A11 #49; DB (8) |
| FC-13 | Sales-entered Visit feedback links the request and marks it Submitted | API A11 #50; DB feedback test (8) |
| FC-14 | The public token cannot create a second result (either order, or a race) | API A11 #50; DB (8) |
| FC-15 | General Sales feedback without a Visit (NULL Visit, NULL request) | API A11 #50; DB (8) |
| FC-16 | Sales retrieves the allowed expense categories | SEC #69; API A11 #51 |
| FC-17 | Sales cannot manage categories, read analytics or edit expenses | SEC #69; API A11 #42, #51 |
| FC-18 | Every documented FK migrates on PostgreSQL | DB §24 and final-correction test (10) |
| FC-19 | ERD cardinalities and column names match the schema | DB final-correction test (11) |
| FC-20 | No PNG / SVG / JPG / rendered diagram exists | DB final-correction test (12); DIAGRAMS.md header |

### 13c. Orthodontics freeze tests OT-1 … OT-22 (see also OC-1 … OC-24 in §13d)

| # | Test | Where it is specified |
|---|---|---|
| OT-1 | Doctor enters the initial Ortho terms | SEC #34; API A11 #48; FIN #25 |
| OT-2 | Manager edits the terms later (audited, with a reason) | SEC #34; API A11 #48, #54 |
| OT-3 | Sales cannot edit terms or installment amounts | SEC #34, #71; API A11 #54, #55 |
| OT-4 | Initial installments are equal, the last absorbs rounding, generated once at Visit finalization | FIN #25; API A11 #48 |
| OT-5 | Deposit change after installments exist (5,000 → 7,000) | FIN #32; API A11 #54; DB test (15), (19) |
| OT-6 | No automatic redistribution | FIN #32; API A11 #54; DB (15) |
| OT-7 | Schedule stays unbalanced (`requires_schedule_adjustment`) until fixed | FIN #32; API A11 #54; DB (19) |
| OT-8 | Manager manually edits eligible installments | FIN #32; API A11 #55; SEC #34 |
| OT-9 | Balanced schedule passes (Deposit + Σ active installments = Effective Ortho Total) | FIN #32, §17 V; API A11 #55 |
| OT-10 | Months change after installments exist | FIN #32; API A11 #54 |
| OT-11 | No automatic regeneration, addition or removal | FIN #32; API A11 #54; DB (15) |
| OT-12 | A paid installment cannot be deleted | FIN #33; API A11 #55; DB (18) |
| OT-13 | A partially paid installment cannot go below its paid amount | FIN #33; API A11 #55; DB (16) |
| OT-14 | Payment allocations / applications are unchanged by a terms edit | FIN #32; API A11 #56; DB (15) |
| OT-15 | A Total increase creates a positive difference installment | FIN #25, #34; API A11 #54 |
| OT-16 | A Total decrease is rejected (`ortho.total_decrease_not_supported`) | FIN #34; API A11 #54; DB (14) |
| OT-17 | Generic `PriceCorrection` cannot change the Ortho Total | FIN #31; SEC #70; API A11 #53; DB (13) |
| OT-18 | Generic `DiscountCorrection` cannot change the Ortho Total | FIN #31; SEC #70; API A11 #53; DB (13) |
| OT-19 | Refund / reversal remain available through the normal finance flow | FIN #34, #15; API A11 #56 |
| OT-20 | No negative installment is ever produced | FIN #33, #34; API A11 #54, #55; DB (17) |
| OT-21 | Sales / Manager manual payment application still works | FIN #25, #34; SEC #71; API A11 #56 |
| OT-22 | No oldest-first automatic allocation | FIN #25; API A11 #56 |

### 13d. Orthodontics consistency tests OC-1 … OC-24 (Effective Total and Months)

| # | Test | Where it is specified |
|---|---|---|
| OC-1 | Gross 20,000, Discount 2,000, Deposit 5,000: Effective Total = 18,000 | FIN #35; API A11 #57; DB (20) |
| OC-2 | Initial installment sum = 13,000 | FIN #35; API A11 #57; DB (20) |
| OC-3 | Charge Original Price = 20,000 | FIN #35; API A11 #57; DB (20) |
| OC-4 | Charge Discount = 2,000 | FIN #35; API A11 #57; DB (20) |
| OC-5 | Charge Billed = 18,000 (no extra 2,000 outstanding) | FIN #35; API A11 #57; DB (20) |
| OC-6 | Deposit > Effective Total is rejected (`ortho.deposit_exceeds_effective_total`) | FIN #35; API A11 #54, #57; DB (21) |
| OC-7 | No installment is generated before Visit finalization | FIN #36; API A11 #57; DB (22) |
| OC-8 | The final Discount snapshot is used at generation | FIN #35, #36; DB (20) |
| OC-9 | A later Gross increase of 4,000 raises Billed and the schedule by 4,000 only | FIN #37; API A11 #54; DB (27) |
| OC-10 | Generic `DiscountCorrection` remains rejected for Ortho | FIN #31, #35; SEC #70; API A11 #53, #57; DB (13) |
| OC-11 | Initial Months = 10 creates the normal initial plan at finalization | FIN #36, #38; API A11 #58; DB (23) |
| OC-12 | Months 10 → 12: no automatic rows are created | FIN #38; API A11 #54, #58; DB (15) |
| OC-13 | `requiresScheduleAdjustment` is true until the Manager adjusts the schedule | FIN #38; API A11 #58; DB (19) |
| OC-14 | The Manager manually adds the necessary future installments | FIN #38; API A11 #55, #58; SEC #72; DB (26) |
| OC-15 | After a valid manual batch the schedule is valid | FIN #38; API A11 #55, #58 |
| OC-16 | Months 10 → 8: no automatic removal | FIN #38; API A11 #58; DB (15) |
| OC-17 | The Manager may retire only unpaid future installments | FIN #33, #38; API A11 #55; DB (24) |
| OC-18 | An installment with payment history cannot be retired | FIN #33; API A11 #55; DB (24) |
| OC-19 | A partially paid installment cannot be reduced below its paid amount | FIN #33; API A11 #55; DB (16) |
| OC-20 | A manual schedule adjustment preserves every payment application | FIN #38; API A11 #55, #56; DB (15) |
| OC-21 | Changing a due date preserves the revision / audit history | FIN #38; API A11 #55; SEC #72; DB (25) |
| OC-22 | An impossible Months reduction is rejected (`ortho.months_conflict_with_paid_history`) | FIN #38; API A11 #54 |
| OC-23 | Number of Months is not dead metadata after a revision | FIN #38; API A11 #58; DB (19) |
| OC-24 | `PriceChange` installments stay distinguishable and are not used as automatic month-plan regeneration | FIN #37, #38; API A11 #58 |

## 14. Final-correction guardrails: DO / DO NOT

**DO NOT**

1. DO NOT let the Manager enter an **initial** work price, consultation fee, Fixed Lab amount or Ortho terms. The Manager corrects / revises only, audited.
2. DO NOT give Sales any way to set Ortho terms, Fixed Lab or prices.
3. DO NOT recalculate old Sales commission when the rate changes; DO NOT apply a new rate to a past collection; DO NOT store one percentage on the payroll header.
4. DO NOT create a Walk-In / Direct commission or pool.
5. DO NOT allow more than one accepted feedback result per request (no duplicate result via the public token and Sales entry).
6. DO NOT leave a request **Pending** or **Sent** after Sales-entered feedback has been linked to it; DO NOT store a feedback status column; DO NOT add expiry, `Expired`, `expires_at` or J6.
7. DO NOT write an invalid composite foreign key: every composite FK targets a PRIMARY KEY or a declared UNIQUE on exactly the referenced columns (DATABASE_DESIGN.md §24); DO NOT add a `visit_id` column to `pricing_discount_entries` or to `case_work_items`.
8. DO NOT treat an unknown Fixed Lab as 0; DO NOT distribute a Fixed-lab collection without revision 1.
9. DO NOT let a Manager expense edit overwrite `created_by_user_id`, `created_by_role` or `created_at`.
10. DO NOT give Sales category management, expense listing, expense analytics or expense editing.
11. DO NOT add rendered diagram files (PNG, SVG, JPG) or a rendered-output folder to the design package.
12. **Orthodontics (freeze correction).** DO NOT decrease an Ortho Total in V1; DO NOT use `DiscountCorrection` or `PriceCorrection` on an Ortho charge or its Total; DO NOT generate a negative installment; DO NOT auto-regenerate, add, remove or redistribute installments after a Deposit or Months edit, **even if it looks easy**; DO NOT change payment history; DO NOT reduce an installment below its already-paid amount; DO NOT delete an installment that has payment history; DO NOT treat a schedule that needs adjustment as valid or finalized; DO NOT add a lifecycle status for it. **Effective Total and schedule (consistency correction):** DO NOT balance the schedule against the undiscounted Gross Total (use the Effective Ortho Total = Charge Billed); DO NOT generate installments before Visit finalization or generate them twice; DO NOT silently change the Deposit when it exceeds the Effective Total (reject); DO NOT re-apply the Discount to a Gross increase; DO NOT physically delete or UPDATE an installment (retire it by a revision, only when it has no payment history); DO NOT let a `PriceChange` installment count toward the Number-of-Months plan; DO NOT leave Number of Months without a manual way to change the schedule (the Manager's batch can add, re-date, retire and re-amount).

**DO**

1. DO have the **Doctor** enter the initial price, Fixed Lab and Ortho terms; DO let the **Manager** correct the price (`Finance.Charges.Adjust`), revise the Fixed Lab (`Finance.FixedLab.Revise`) and edit Ortho terms (`Orthodontics.Terms.Edit`).
2. DO snapshot / effective-date Sales commission rates (`sales_commission_rate_history` + `commission_percentage_applied` per item) and apply the rate in force at collection time.
3. DO link Sales-entered Visit feedback to the Visit's existing request and derive Submitted from the existence of any entry; DO allow general feedback with NULL Visit and NULL request.
4. DO validate every PostgreSQL FK (migration test) and keep DATABASE_DESIGN.md, every ERD and DIAGRAMS.md synchronized, including column names and cardinalities.
5. DO expose `GET /api/expenses/allowed-categories` to Sales as a read-only lookup.
6. DO keep Assistant overtime (hours × rate snapshot; attendance alone changes nothing; no automatic deductions; Sales attendance but no overtime), Doctor / Sales / Manager discount entries (fixed EGP; no exposure of notes, lab, percentage or shares) and the referral **reason** vs **notes** distinction exactly as designed.
7. **Orthodontics (freeze correction).** DO route **every** established Ortho term change through `Orthodontics.Terms.Edit` (`PUT /orthodontic-cases/{id}/terms`); DO preserve the audit (actor, timestamp, previous and new values, reason); DO require the **manual** Manager schedule adjustment after a Deposit or Months edit (`PUT /orthodontic-cases/{id}/installments`: amount, due date, add, retire); DO validate **Deposit + Σ active installments = Effective Ortho Total** and **active regular installments = Number of Months** when the schedule is declared valid; DO validate `0 ≤ Deposit ≤ Effective` at Visit finalization; DO protect payment history (G5(b)); DO keep refund / reversal / correction of ortho payments in the normal finance flow.
