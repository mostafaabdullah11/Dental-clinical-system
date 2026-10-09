READ THIS FILE BEFORE IMPLEMENTING ANY FEATURE.

# IMPLEMENTATION_GUARDRAILS.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL

These are the rules that stop the implementation from drifting away from the approved design. They are short on purpose. Each rule points to the document that holds the detail. If a rule here seems to conflict with a feature request, **stop and report it**; do not work around it.

---

## 0. The prime rule

**DO NOT BE CREATIVE WITH OUR BUSINESS. BE CREATIVE WITH THE ENGINEERING NEEDED TO IMPLEMENT IT WELL.**

Do not invent: workflows, permissions, finance rules, approval steps, automatic allocations, statuses, ownership rules, user capabilities, notifications, reports, or fields. If the design is silent, the answer is **not built**, not "a sensible default". Every unanswered decision is in OPEN_QUESTIONS.md and stays open.

If you find a genuine problem, report it in this form and wait:

```
SERIOUS ISSUE:
WHY:
MINIMUM RECOMMENDED FIX:
DECISION REQUIRED:
```

## 1. Source priority when documents seem to disagree

1. The latest explicit user correction. 2. The latest approved revision of a Step. 3. An earlier approved Step. 4. The original requirements. 5. A recommendation. Report any contradiction as a `DESIGN FREEZE CONFLICT` (document, section, conflict, latest approved rule, minimum correction). Correct only when the latest rule is unambiguous.

## 2. Scope: never add these

- **No Inventory. No X-ray.** Not as a module, table, column, permission, job, enum value, label or test. "Medical Supplies" exists only as an expense category row.
- **No Assistant login, account, user_id, application role, dashboard, API or notification** (v1.1). An Assistant is a staff type, not an application role; the application roles are only Manager, Doctor and Sales / Reception. Never give Assistants access unless the owner explicitly approves it. No bonus, deduction, overtime, attendance, commission or incentive rule for Assistants exists; do not add one.
- No patient login, portal or mobile app. No insurance, formal accounting, multi-branch, automated WhatsApp/SMS/email in V1.
- None of the unconfirmed earlier proposals: `ILabAllocationStrategy`, `CaseDoctorAllocation`, a TreatmentPlan/FinancialAgreement workflow, OnHold/Abandoned case states, addendum workflows, `PatientContact` as a business aggregate, Manager approval workflows, price lists, deviation reports, Doctor-share clawback rules.

- **Scheduling (v1.1 correction).** The appointment statuses are exactly seven: Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled. **Arrived and Waiting are NOT appointment states.** Never reintroduce them under another label (Checked In, Ready, In Queue, At Clinic, Seen, Called, Pending Doctor or any other) without owner approval. No patient check-in, arrival timestamp or waiting-queue workflow exists while Q-APP-4 is OPEN: no check-in route, no `Scheduling.Appointments.CheckIn` permission, no `arrived_at` / `checked_in_by_user_id` columns, no `/api/doctor/queue`, no "waiting patients" dashboard metric. When the Doctor begins the linked visit from an eligible Scheduled or Confirmed appointment, the appointment becomes In Treatment directly.

## 3. Architecture rules (ADR-001, 002, 003)

1. Modular monolith. One database, one DbContext, one schema per module. No microservices, message broker, event sourcing, CQRS framework or Kubernetes.
2. Module code is `internal`; the only public surface is `Contracts`. **Dependencies point only to a lower level** (Audit/Configuration/Identity → Staff/Notifications/Expenses → Patients → Scheduling/Crm/Finance → Clinical → Prescriptions/Orthodontics/Feedback → Payroll/Reporting). Architecture tests must fail on a violation; do not weaken them.
3. **Finance never calls Clinical.** Clinical passes Doctor, percentage snapshot and lab model as data.
4. **No cross-module EF navigation properties. Keep database foreign keys across modules**, declared in `CrossModuleReferences.cs`. Never create a duplicate table/entity to avoid a dependency.
5. Every table belongs to exactly one module's schema; migrations are named `<date>_<Module>_<Change>` (`Xref` for cross-module FKs).
6. Reporting reads other modules read-only; nothing depends on Reporting.

## 4. Privacy rules (ADR-004) — highest-risk area

1. **A Doctor never receives** phone, WhatsApp, email, address, CRM/contact notes, appointment `sales_notes`, or the Original Sales Representative: not in a DTO, OpenAPI document, search, PDF, CSV, notification, log, error body or audit view. Doctors search by Patient ID and name only.
2. **Sales never receives** detailed medical history, clinical notes, root canal measurements, detailed treatment measurements, prescription medication details, nor Doctor percentage, Doctor Share, Clinic Share or lab figures. Sales reads clinical data only through the whitelist summary contract.
3. Every response is a projection into an **audience DTO**. Never serialize an entity. Never bind a request to an entity. Unknown request members are rejected.
4. Contact-bearing types carry `[ContactData]`; nothing under `Features/Doctor` may reference them.
5. Search involving names or phones is a **POST body**, never a query string. No personal data in URLs, logs, alerts, pings, error-tracker events, file names or Hangfire arguments (ids only).
6. Write canary tests for any new endpoint, export or notification.

## 5. Security rules (ADR-005, 006)

1. Authorization is enforced on the backend: authentication → endpoint permission → resource scope → audience DTO. React guards are usability only.
2. **One operational role per account.** Never compute a union of roles. Never map a contact-bearing key to DOCTOR.
3. Every mapped endpoint declares a permission key (or `[AuthenticatedOnly]`, or is one of the **six** anonymous endpoints). A mapped route whose key is assigned to no role fails the build.
4. **`Orthodontics.Terms.Set` and `Finance.Discount.Set` are assigned to no role and have no mapped route.** Do not seed them to anyone, do not add `/api/unassigned/*`.
5. "Own" endpoints take the identity from the principal; no `doctorId`/`salesEmployeeId` parameter. A scoped resource that is not the caller's returns **404**.
6. Doctor read breadth does not imply write rights: a Doctor writes only their own editable visit/work. Doctors have **no notification inbox** (`Notifications.Read.Own` is Sales and Manager only).
7. Patient registration requires **both** `Patients.Identity.Write` **and** `Patients.Contact.Write`.
8. Never log or audit passwords, hashes, stamps, reset tokens or feedback tokens. A password change cannot be reconstructed from audit.
9. Database roles: `clinic_app` (no DELETE, no UPDATE on insert-only tables, SELECT+INSERT only on `infra.data_protection_keys`), `clinic_worker` (minimal grants, only the two minimal views for identity/staff lookups), `clinic_backup` (explicit SELECTs, **no `pg_read_all_data`, no key table**), `clinic_migrator` (DDL, migrator container only). Every new table/column needs an explicit **grant decision and a backup-grant decision** in its migration.
10. Reset-token delivery is deployment configuration (OPEN). Do not build a Manager-delivers-link workflow or a recovery-initiation endpoint.

## 6. Finance rules (ADR-007 to ADR-010) — second highest risk

1. **Charge ≠ Payment.** **Billed, Collected, Outstanding are derived**; never store a balance column.
2. Ledger tables are **insert-only**. A correction is a new row (negated payment/earning/completion, signed adjustment). **No refund, reversal, adjustment, discount, ortho-terms, installment-application, attribution-resolution or charge-establishment endpoint exists** until its question is answered.
3. `RecordPayment` is **one transaction**: idempotency key (unique), ordered `FOR UPDATE` locks on charge rows (id order), payment, allocations, then earnings/completions or pending attributions, then audit. **Allocation is explicit**: no oldest-first, no automatic split.
4. **Doctor Share = round_half_up(allocated × the charge's percentage snapshot / 100, 2)**, recorded immediately in `doctor_earnings` for every treatment allocation **whose Doctor attribution is clear**, general and fixed lab alike. A held allocation (Q-FIN-9) creates a Pending Doctor Attribution row and **no** earning; never invent a provisional Doctor Share. **No lab value ever enters this formula.** The snapshot comes from the work item; never read the Doctor's current percentage at payment time.
5. Lab/Clinic completion lives in `revenue_distributions`. Invariant `doctor_share + lab_amount + clinic_share = allocated_amount` (database CHECK); **Clinic Share is the remainder**. A pending completion is the absence of a row.
6. **Fixed lab (Q-FIN-1 OPEN): create no completion rows for fixed-lab allocations.** **W2 OPEN:** create a general-lab completion only when the lab rate at the charge's `charged_at` equals the rate at the payment's `paid_at`; otherwise leave it pending. Record the rate used on every row.
7. **One fixed lab per case** (`fixed_lab_expenses` unique per case; revisions insert-only). Never attach the fixed lab to a charge, payment or Doctor. A revision never changes a Doctor Share.
8. Consultation charges produce no earning and no distribution; they are Clinic revenue and a separate report line.
9. Financial ownership is on the **work item**. A new work item exists only for a new case's first work, or when `separatelyPricedWork = true` (never on ortho cases). Continuing a case adds a treatment record only.
10. **Q-FIN-9 OPEN:** when a patient is reassigned or a Doctor is deactivated while a treatment charge has Outstanding > 0, record an attribution hold. Held allocations get a pending-attribution row and **no earning, no provisional share, no payroll entry**. Never block a payment because of a hold. Never release a hold automatically.
11. **Q-ORT-2 OPEN: never create an `installment_payments` row.** Payments allocate to the ortho **charge** only. Installment reads show due date, amount due and `applicationState: "Pending"`; do not compute per-installment paid/remaining/status.
12. Payroll aggregates recognized `doctor_earnings`; payout never includes lab fields; sealed columns are frozen at finalization (G2); a later completion never changes a sealed payroll. Sales payroll finalization stays disabled (Q-FIN-2 OPEN); never guess a commission. Doctor Payroll never appears in the Estimated Operating Result.
13. Money: `numeric(12,2)`, `decimal` in code, **strings in the API**. Percentages `numeric(5,2)`. No floating point.
14. Discounts are fixed EGP amounts, never percentages; only possible after Q-FIN-3 is answered.
15. **Assistant payroll (v1.1).** Assistant is staff only (`staff.assistants`, no `user_id`); payroll is `payroll.assistant_payrolls` in the existing Payroll module; no new module. Salary change = `Staff.AssistantSalary.Configure`; record create/edit/(de)activate = `Staff.Manage`; payroll view/draft/finalize/mark-paid = `Payroll.Assistant.ManageAll`. All Manager-only technical keys.
16. **Historical Assistant salary and payroll snapshots never recalculate.** A salary change touches only `current_salary` and is audited; finalized payrolls are sealed by G2. No effective-dated salary-history table until Q-PAY-ASST-2 requires it.
17. `final_amount = salary_snapshot` (CHECK). No endpoint accepts a free `final_amount`. No proration, no calendar-day or working-day division, no "divide by 30", anywhere. A payroll needing partial-period calculation cannot be finalized (Q-PAY-ASST-2 OPEN). The database rejects overlapping periods for one Assistant.
18. **Assistant payroll is an operating expense.** Never subtract Assistant salary from Revenue or Clinic Gross Revenue. Operating result = Clinic Gross Revenue − Sales Salary / Commission − Assistant Payroll Expense − Clinic Operating Expenses − other confirmed Clinic-owned expenses. Doctor Payroll is never subtracted again. Assistant Payroll is its own report line, never inside Other Expenses.
19. **Q-PAY-ASST-1 OPEN:** when a report range only partially overlaps an Assistant payroll period, show the line as PENDING / NOT ALLOCATED and label the result INCOMPLETE — ASSISTANT SALARY ALLOCATION PENDING. Never present it as a final result and never prorate. Q-PAY-ASST-1 (reporting allocation) and Q-PAY-ASST-2 (payroll amount calculation) are separate questions; do not merge them.
19a. **Assistant data is never exposed to Doctor or Sales** (no field, report line, notification, export or log). **Assistant payroll never affects Doctor Share** (Doctor Share = round_half_up(allocated × percentage snapshot / 100, 2); neither lab nor Assistant salary enters it) and does not change any Finance ledger table. Assistant Payroll Expense is read from Payroll by Reporting; it is not a Finance ledger distribution.
19b. Never invent Assistant bonus, deduction, overtime, attendance deduction, commission, incentive or allowance (NOT CONFIRMED / NOT DESIGNED); no components or adjustments table.
19c. **Q-PAY-ASST-2 OPEN:** a payroll that needs partial-period calculation, or includes a salary change inside its period, cannot be finalized.

## 7. Data rules (ADR-011)

1. No physical deletes in business schemas. Use `voided_at`, `removed_at`, `withdrawn_at`, `is_active`.
2. Respect table classes: **H** insert-only, **L** only named columns change, **M** mutable and audited. Guards **G1–G7** are mandatory and may not be bypassed; `SET LOCAL clinic.audited_correction` is set in exactly one class.
3. **Prescriptions are editable until their visit is finalized**, then protected by G1; the post-finalization correction workflow is **OPEN (Q-CLN-1)** — build none. Payroll immutability is financial immutability: status/finalized/paid transitions stay possible.
4. Reassignment never moves history (past work references the performing Doctor, not assignment rows). Appointment changes create a new appointment and mark the old Rescheduled; never update the old row's time.
5. Orthodontics: total price lives only in the charge; `ortho_cases.charge_id`, `deposit_amount`, `number_of_months` are nullable until the terms actor is decided; an ortho case has exactly one work item and at most one charge.
6. Direct patient registration leaves **Original Sales Employee NULL**; lead conversion preserves the lead's value. Never infer it from the logged-in user.
7. Age: exact age only from a real date of birth; a recorded age is shown as recorded with its date and never aged forward.
8. Double-booking is prevented by the same-slot unique index only (Q-APP-1 OPEN); do not invent durations or overlap rules.

## 8. Notification and job rules (ADR-012)

1. Jobs run only in the Worker. Arguments are ids only. Every job is idempotent (dedupe key, `ON CONFLICT DO NOTHING`).
2. "Tomorrow" is the clinic-local calendar date via `IClinicClock`; never hard-code offsets or the timezone.
3. The dispatcher processes **each notification in its own transaction** (`SKIP LOCKED`); a failure is committed as `Failed` in a new transaction; retry reuses the same row.
4. The Worker resolves users only through `identity.v_active_user_roles` and `staff.v_sales_employee_activity`. Never grant it `identity.users`, `staff.*` tables, contacts or clinical tables. No hardcoded role UUIDs.
5. No notification targets a Doctor. Reset messages are never stored in `notifications`.
6. J6 is inert while `expires_at` is unset. J2 selects installments due tomorrow whose ortho charge has Outstanding > 0.

## 9. API rules (ADR-013)

Audience prefixes and one OpenAPI document per audience; money as strings; RFC 7807 errors with stable codes and no echoed input; `If-Match` required on editable resources; `Idempotency-Key` required on payments (same key + different payload → 409); pagination max 100; whitelisted sort/filter; Swagger off in Production. The Manager receives the audience's DTO on shared routes. Appointment statuses Cancel/Confirm/No Show go through the Manager-only `UpdateStatus` seed (not a clinic rule).

## 10. Deployment and operations rules (ADR-014, 015)

1. **The application never migrates itself.** Only the migrator, with `clinic_migrator`, runs migrations. Expand/contract only; the destructive-operation scan and the N-1 compatibility job must pass. Readiness checks `required − applied`, not equality. Non-transactional migrations require `[NonTransactionalMigration]`, single purpose, retry-safe statements.
2. Secrets are Docker secrets, never in the repository, images or CI logs. `dp_cert` only in `api`; the Worker gets no Data Protection access; the migrator password is never in `api` or `worker`.
3. Staging and Development use **synthetic data only**. Production data is never copied down. Restore drills use real data in a throwaway production-grade environment, never in staging.
4. Every restore to an earlier point is followed by `post-restore-lockdown` (rotate stamps; reset-required for all staff by default). Never claim a password change can be re-applied from audit.
5. Backups: `age` to two independent recipients (either decrypts; not two-person approval); custody is OPEN and not tied to an application role; no private key on the VM; write-only upload credentials; dump excludes the key-table data.
6. Alerts, pings and error events contain no personal data.

## 11. Testing rules

1. Implement the test lists in SECURITY_DESIGN.md (1–48), FINANCE_DESIGN.md (1–30), API_ARCHITECTURE.md (A11 1–36, B9 1–26) and DEPLOYMENT_ARCHITECTURE.md (2.10 1–9, 3.7 1–22) together with the feature, not afterwards.
2. Integration tests run against real PostgreSQL with the real role bootstrap script so grants and triggers G1–G7 are exercised.
3. Canary scans over every Doctor and Sales response, PDF, CSV, notification and log. Generated role-matrix test over every mapped route.
4. Tests assert the **negative** open-question behavior (e.g. "no installment application is created", "no `/api/unassigned/*` route is mapped", "fixed-lab allocation creates an earning and no completion").

## 12. Before you start a feature: checklist

- [ ] I read the section of the design documents that covers this feature.
- [ ] I identified which OPEN questions touch it, and implemented only the stated current behavior.
- [ ] No Inventory, no X-ray, no invented rule, status, permission or field.
- [ ] Dependencies point only downward; Finance does not call Clinical.
- [ ] Every table has an owning schema, grant decision and backup-grant decision; cross-module references are database FKs.
- [ ] DTOs are audience-specific; Doctor contains no contact data; Sales contains no clinical detail.
- [ ] Money paths are insert-only, idempotent, transactional, and tested with the invariant checks.
- [ ] The feature's tests (including canary and negative tests) are written.
- [ ] If anything above cannot be satisfied: reported as `SERIOUS ISSUE` / `DESIGN FREEZE CONFLICT`, not worked around.
