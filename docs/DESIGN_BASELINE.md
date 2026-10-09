# DESIGN_BASELINE.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL

This is the consolidated baseline of the Requirements + Architecture/Design phase (Steps 1–15). It states what the system is, what is confirmed, what is out of scope, what is architecture, and where the detail lives. Detail is not repeated here: see the document index in section 12.

## 1. Source priority used for this freeze

When two statements disagreed, the higher one won:

1. The latest explicit user correction.
2. The latest approved revision of a Step.
3. An earlier approved Step.
4. The original requirements (master prompt and clarification sets).
5. My recommendations.

Labels used in every document: **CONFIRMED** (a business rule the user stated), **TECHNICAL** (engineering choice, no business meaning), **DEFAULT** (technical value that may be vetoed), **OPEN** (undecided; nothing implemented; listed in OPEN_QUESTIONS.md).

**Standing working rules:** *Do not be creative with the business; be creative with the engineering needed to implement it well.* No invented workflows, permissions, finance rules, approval steps, automatic allocations, statuses, ownership rules or user capabilities. Open questions stay open. Items I proposed earlier and the user did not confirm (`ILabAllocationStrategy`, `CaseDoctorAllocation`, TreatmentPlan/FinancialAgreement workflow, OnHold/Abandoned case states, addendum workflow, `PatientContact` as a business aggregate, Manager approval workflows, price lists, Doctor-share clawback rules) are **not requirements**.

## 2. What the system is

A single-clinic dental operations system with three internal application roles (Manager, Doctor, Sales / Reception). The clinic also employs **Assistants**, who are a *staff type only* (v1.1): they have no account, no login, no role, no notifications and no operational workflow; they exist in the system solely for staff record, salary tracking, payroll expense and management financial reporting. Patients are business entities only: no login, no portal, no mobile app. The core loop: Lead or walk-in → Patient → Appointment (published Doctor availability) → Visit → Treatment case(s) → Doctor-entered price → Charge → Payments (Sales/Manager) → Revenue split (Doctor / Lab / Clinic) → Payroll and reports, with referrals, follow-ups, orthodontic installment tracking, feedback requests, internal notifications and an audit log around it.

Ideas the whole design hangs on:
- **Privacy is structural.** A Doctor never receives phone, WhatsApp, email, address, CRM/contact notes or the Original Sales Representative. Sales never receives detailed clinical data. Enforcement is backend-only: storage separation, contract separation, audience DTOs, architecture tests, canary tests.
- **Money is a ledger.** Charge ≠ Payment. Billed, Collected and Outstanding are derived, never stored as balances. History is insert-only.
- **Financial ownership generally follows the work.** The Doctor who performed the work earns on it at the percentage snapshotted when the work began. Patient assignment and financial ownership are different concepts. Future collections after patient reassignment or Doctor deactivation are subject to Q-FIN-9 and may remain Pending Doctor Attribution.
- **Visit ≠ Case.** A visit is one encounter; a treatment case may span many visits.
- **Open questions stay open behind neutral seams.**

## 3. Confirmed technical constraints

Modular monolith (no microservices, Kafka, RabbitMQ, event sourcing, Kubernetes or complex CQRS). React + TypeScript SPA; ASP.NET Core Web API; PostgreSQL; EF Core. RBAC with backend authorization. Hangfire (PostgreSQL storage) for background work. Docker, Linux, HTTPS, cloud-hosted, managed PostgreSQL preferred, GitHub Actions CI/CD. Configurable clinic timezone. Optimistic concurrency, transactions, unique constraints, duplicate-payment and double-booking protection, idempotent scheduled operations. No destructive migration at application startup.

## 4. Confirmed business requirements (summary)

Full detail and traceability are in the module sections of the other documents; this table is the one-page statement of what is confirmed.

| Area | Confirmed |
|---|---|
| Roles | Manager (highest operational authority, never destructive history editing; sensitive actions audited), Doctor, Sales / Reception. **Only these three are application roles.** Staff types are Doctor, Sales / Reception employee and **Assistant**; ASSISTANT is a staff type, not an application role (v1.1). |
| Patients | Fields: Patient ID, Full Name, Phone, DOB or Age, Address, Medical History, Chronic Diseases, Previous Surgeries, Chief Complaint, Original Sales Representative, Registration Date, Active/Inactive. History preserved permanently. Complete timeline. All Doctors may view all patients' clinical history. Manager reassigns a patient's Doctor (previous Doctor, new Doctor, time, Manager, reason stored; past work stays with the performing Doctor; audited). Original Sales Employee preserved on leads and patients. |
| Doctor visibility | May see name, age/DOB when clinically required, medical history, chronic diseases, surgeries, chief complaint, full clinical history, prescriptions, clinical notes, implant and ortho info, and the financial breakdown of **own** work. **Never** phone, WhatsApp, email, address, direct contact data, CRM/contact notes or the Original Sales Representative. Search by Patient ID and name only. |
| Sales visibility | May see contact data and the Original Sales Representative. Whitelist: procedure name, treating doctor, treatment status, referral destination, appointment/follow-up instructions, price, amount paid, remaining, ortho payment/installment information. **MUST NOT** see detailed clinical notes, root canal measurements, detailed medical history, detailed treatment measurements, prescription medication details. |
| Visits and cases | Visit: Draft → In Progress → Finalized (finalizing protects that visit's record, not the whole case). Case: Planned → Active → Completed → Cancelled. |
| Doctor workflow | Patient → Visit → Tooth/Area → Procedure → Details → Steps → Lab expense (if applicable) → Price → Clinical notes → Prescription → Next appointment → Referral → Complete. The Doctor does not collect payments and does not independently apply discounts. Every procedure records the Doctor who performed it. |
| Procedure catalog | Root Canal (canals 1–5 in mm; steps Access Cavity, Mechanical Preparation, Obturation, optional Post & Core); Fillings (GIC / Composite → Anterior / Posterior); Extraction (Simple / Surgical / Advanced Surgical); Pediatric (Pulpotomy, Pulpectomy, Space Maintainer, Stainless Steel Crown, Extraction without type, Zircon Crown, Other); Implant (healing 1–6 months, optional bone graft and membrane); Fixed Prosthetics (Porcelain/PFM, Zirconia, E-max); Removable Prosthetics (Complete/Partial; Flexible/Standard Acrylic); Scaling/Cleaning, Deep Cleaning, Polishing, Laser Bleaching/Whitening (only Laser Bleaching has Basic / High Quality); Orthodontics (Upper Arch, Lower Arch, Appliance + free-text type); Other (free text). The catalog can evolve. |
| Prescriptions | Patient → Visit → Doctor; multiple medications; all Doctors may view history; print layout with clinic branding and **no** patient contact; changes after finalization are auditable (who may edit: OPEN). |
| Scheduling | Calendar, Doctor schedule, **7 statuses** (Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled). Arrived and Waiting are **not** appointment states and no check-in or waiting queue exists (Q-APP-4). An appointment moves Scheduled → Confirmed, becomes In Treatment when the Doctor starts the linked visit from an eligible Scheduled or Confirmed appointment, and then Completed; Cancelled, No Show and Rescheduled are the other terminal paths. Manager publishes weekly availability. Double-booking prevented. Doctor may create the next appointment and reschedule their own follow-up; Sales may reschedule; Manager may change appointments. A change preserves the original and creates a new appointment. |
| Sales | Leads (8 statuses), referrals (types Surgery/Implant/Orthodontics/Prosthetics/Other; statuses New/Contacted/Booked/Completed/Lost), follow-ups (6 sources; contact attempts tracked, never overwritten), Sales dashboard (8 items; the former "waiting patients" item was removed with the Waiting state and was not replaced). |
| Orthodontics | Total price, deposit, number of months; Remaining = Total − Deposit; initial generated installments are equal, with the final installment absorbing any rounding difference (whether individual amounts may later be customized remains OPEN under Q-ORT-1); installments stored individually; statuses Upcoming/Due/Partially Paid/Paid/Overdue; payment details visible to Doctor, Sales, Manager; Doctor percentage fixed at case start; reminder one day before due. |
| Payments | Cash, Visa, InstaPay; full, partial, multiple payments per charge, multiple charges per patient; never overwritten; Charge and Payment separate; recorded by Sales or the Manager only; consultation fees stored and reported separately with no Doctor commission; original price, fixed-EGP discount and final price stored separately; agreement not silently changed. |
| Revenue split | Collected = Doctor Share + Lab Expense + Clinic Share, all components of the total. Doctor Share = Doctor % snapshot × collected. General lab = Manager's percentage of collected. Fixed lab (Implant, Fixed/Removable Prosthetics) = one amount per case, Doctor enters, Manager may edit with full audit; not removed before the Doctor percentage. Doctor percentage snapshotted at work start. Financial ownership generally follows the Doctor's performed work and percentage snapshot; future collections after patient reassignment or Doctor deactivation are subject to Q-FIN-9 and may remain Pending Doctor Attribution (no earning until resolved). Clinic expenses reduce the Clinic's result only. |
| Doctor finance view | Own work only: price, collected, remaining, percentage, Doctor Share, Clinic Share, relevant lab, historical revenue. Never clinic-wide figures, other Doctors' data, expenses, Sales payroll, management reports. |
| Payroll | Doctor: own percentage, historical snapshots, never recalculated. Sales: base + commission + bonus − deductions; per-employee commission percentage; monthly snapshots; Sales sees only own. **Assistant (v1.1, CONFIRMED):** Manager sets each Assistant's salary; payroll has Assistant, period, salary snapshot, final amount, status, finalized-at, paid date; Manager-only; historical salary and payroll never recalculated; Assistants are never deleted (Active/Inactive). **Salary only:** bonuses, deductions, overtime, attendance deductions, commissions and incentives are NOT confirmed and NOT designed. Payroll period, partial-period calculation (Q-PAY-ASST-2) and period-report allocation (Q-PAY-ASST-1) are OPEN. |
| Expenses | 11 categories; fields ID, date, category, description, amount, payment method, vendor, notes, created by/at; finalized entries use adjustment/reversal. |
| Dashboard and reports | Manager daily report (live, no manual generation); Daily/Weekly/Monthly/Custom reports; Estimated Operating Result is an operational metric, not accounting profit. **Operating result (v1.1):** Clinic Gross Revenue − Sales Salary / Commission Expense − **Assistant Payroll Expense** − Clinic Operating Expenses − other confirmed Clinic-owned expenses. Assistant salary is an operating expense; it never reduces Revenue itself. Doctor Payroll is not subtracted again (the Doctor share is already removed when the Clinic's share is formed). Assistant Payroll is its own report line, never hidden inside Other Expenses. |
| Feedback and notifications | Visit completed → feedback request → Sales queue → Sales sends link (manual in V1) → patient submits on the clinic website. Seven notification types, Sales recipients, idempotent/retry-safe/timezone-aware. |
| Security and audit | HTTPS, secure hashing, RBAC, backend authorization, session management, secure reset, validation, SQLi/XSS/CSRF protection, auth rate limiting, secrets management, DB backup, least privilege, export restrictions, secure audit log. No physical deletion of treatments, payments, payroll, prescriptions, expenses, lab expenses or doctor assignments. Employees never deleted (Active/Inactive; login disabled; history preserved). |

## 5. Removed and out of scope

**Removed everywhere (must not appear in any schema, API, UI, diagram, requirement or test): X-ray functionality; Inventory management.** "Medical Supplies" exists only as a clinic expense category.

Also removed: Sutures/Bone Removal options on Extraction; an extraction type under Pediatric Extraction; Anterior/Posterior as top-level filling types; Basic/High Quality under Scaling/Deep Cleaning/Polishing; the Doctor applying discounts; Doctors collecting payments; the old payroll formula (Revenue − Lab) × Commission %; Doctor commission looked up at payroll time; the hardcoded 3-month healing period; Sales commission trigger events (dropped as requirements).

Out of scope for V1: Assistant login, account, role, dashboard, notifications or any operational workflow (an Assistant is a staff record with payroll only, unless the owner explicitly approves more); patient mobile app and accounts; advanced formal accounting; insurance integration; full multi-branch (Q-BRN-1); automated WhatsApp/SMS/email delivery (future, behind the channel abstraction).

## 6. Architecture baseline

**Shape.** One codebase, two processes (`Clinic.Api`, `Clinic.Worker` running Hangfire), one PostgreSQL database, one DbContext, one schema per module. Modular monolith with in-process hand-written command/query/event handlers; no message broker.

**Modules.** The 22 confirmed modules map to **16 implementation modules**:

| Implementation module | Confirmed module(s) |
|---|---|
| Identity | Authentication & Security |
| Audit | Audit Logging |
| Configuration | System Configuration |
| Staff | Doctor Management + Sales employee profiles + Assistant staff records (v1.1; no user account) |
| Patients | Patient Management |
| Scheduling | Appointment & Scheduling |
| Clinical | Clinical Visits + Dental Procedures + Implant Cases |
| Prescriptions | Prescriptions |
| Orthodontics | Orthodontic Cases |
| Crm | Sales/Reception + Referrals & Follow-Ups |
| Finance | Patient Billing & Payments + Finance & Accounting + Lab Expenses |
| Expenses | Clinic Operating Expenses |
| Payroll | Doctor Payroll + Sales Payroll & Commission + Assistant Payroll (v1.1; salary only) |
| Notifications | Notifications |
| Feedback | Feedback |
| Reporting | Reports & Analytics |

(`SharedKernel` is a small non-business library: money, ids, clock, result types.)

**Levels and dependencies.** Code dependencies point only to a lower level; events flow from a lower publisher to a higher subscriber; a build-time architecture test enforces both.

| Level | Modules |
|---|---|
| 1 | Audit, Configuration, Identity |
| 2 | Staff, Notifications, Expenses |
| 3 | Patients |
| 4 | Scheduling, Crm, Finance |
| 5 | Clinical |
| 6 | Prescriptions, Orthodontics, Feedback |
| 7 | Payroll, Reporting |

Full arrow list: DIAGRAMS.md Diagram 3. Notable rules: **Finance never calls Clinical** (Clinical passes the Doctor, percentage snapshot and lab model to Finance as data); Reporting reads other modules read-only and nothing depends on it; Finance depends on Patients, Staff, Configuration, Audit; Notifications defines ports that source modules implement.

**Data.** Schema = module. Cross-module EF navigation is avoided; **database foreign keys across schemas are kept** (declared in one file, `CrossModuleReferences.cs`, tested against the database). Tables are insert-only (H), lifecycle (L) or mutable audited (M). Guards G1–G7 protect finalized visits, payroll sealing, expense sealing, work-item freeze, allocation caps and fixed-lab eligibility. See DATABASE_DESIGN.md.

**Security.** Four layers per request (authentication, endpoint permission key, resource scope, audience DTO). One operational role per account. Cookie sessions with security-stamp check on every request. See SECURITY_DESIGN.md.

**Finance.** Payment → PaymentAllocation. If Doctor attribution is clear → DoctorEarning immediately → RevenueDistribution when Lab / Clinic completion is computable. If an attribution hold exists (Q-FIN-9) → Pending Doctor Attribution and **no** DoctorEarning until Q-FIN-9 is resolved. The payment itself is never blocked, and Outstanding is based on payment allocations. See FINANCE_DESIGN.md.

**API.** Audience-prefixed routes, one OpenAPI document per audience, money as strings, RFC 7807, ETag/If-Match, idempotent payments. Unresolved business actions have no mapped route. See API_ARCHITECTURE.md.

**Deployment.** One Linux VM (Caddy, SPA, api, worker; migrator and backup one-shots) plus managed PostgreSQL; expand/contract migrations with a compatibility-based readiness check; three-layer backups; monitoring with no personal data in alerts. See DEPLOYMENT_ARCHITECTURE.md.

## 7. Neutral seams for open items

| Open item | Seam |
|---|---|
| Fixed-lab allocation (Q-FIN-1) | Earning recorded immediately (when attribution is clear); completion rows absent until a versioned calculator exists |
| General lab rate selection (W2) | Completion only when every candidate rule agrees; rate recorded per row |
| Sales commission base (Q-FIN-2) | `ISalesCommissionBase`, no implementation |
| Discount role (Q-FIN-3) | Key assigned to no role; no route |
| Ortho terms actor | Key assigned to no role; no route; ortho columns nullable |
| Patient arrival / queue workflow (Q-APP-4) | Not implemented: no Arrived / Waiting state, no check-in endpoint, no `Scheduling.Appointments.CheckIn` permission, no arrival columns, no Doctor waiting queue |
| Installment application (Q-ORT-2) | `installment_payments` exists, unused; allocations only |
| Attribution after reassignment/departure (Q-FIN-9) | Attribution holds + pending attributions |
| Assistant salary in Daily/Weekly/Custom reports (Q-PAY-ASST-1) | Payroll rows are included only when the report range fully contains the payroll period; otherwise the line is PENDING and the result is marked INCOMPLETE; no proration |
| Assistant partial-period / mid-period salary (Q-PAY-ASST-2) | `final_amount = salary_snapshot` (CHECK); no free amount; no components table; no salary-history table |
| Refund/correction (Q-PAY-1) | Internal primitives, no endpoint |
| Post-finalization correction (Q-CLN-1) | G1 blocks for everyone; audited-correction path unbuilt |

## 8. Technical placeholders (not business rules, may be vetoed)

Follow-up status values (text validated in code); payroll statuses Draft/Finalized/Paid; `referrals.follow_up_instructions`; orthodontics lab model = General; Manager-only appointment status changes (a seed, not a clinic rule); Doctor follow-up booked with the authoring Doctor; the two technical Manager keys `Scheduling.Appointments.UpdateStatus` and `Reports.Export`; payment validation defaults (Σ allocations = payment; Collected ≤ Billed).

## 9. Delivery phases referenced by the design

This freeze closes Requirements + Architecture/Design (Steps 1–15). It does **not** begin implementation. The next phase starts only after the user approves the freeze.

## 10. Serious-issue register at freeze

None blocking. Items the user should knowingly accept: Doctors may open every patient's full clinical history (confirmed; compensating controls are record-access audit and contact isolation); one role per account means a person needing two functions needs an explicitly approved role definition; consultation fees and new charges cannot exist until W1 is answered; fixed-lab Lab/Clinic reporting and Sales commission remain incomplete until Q-FIN-1 and Q-FIN-2 are answered; a restore forces password recovery for staff.

## 11. Consistency conflicts found and corrected during the freeze

See DESIGN_FREEZE.md section 4 (C1–C3). **v1.1 change:** the Assistant payroll requirement (D1–D5) is the only business change since v1.0; see DESIGN_FREEZE.md sections 3a and 3b.

## 12. Document index

| Document | Content |
|---|---|
| DESIGN_BASELINE.md | This file |
| DATABASE_DESIGN.md | Final schema, guards G1–G7, cardinalities, source of truth, snapshots |
| SECURITY_DESIGN.md | Authentication, RBAC matrix, scoping, privacy boundary, DB roles, restore security |
| FINANCE_DESIGN.md | Charges, payments, earnings, completions, payroll, reports |
| API_ARCHITECTURE.md | API conventions, endpoint catalog, notifications and jobs |
| DEPLOYMENT_ARCHITECTURE.md | Deployment, CI/CD, migrations, backup, restore, monitoring |
| DIAGRAMS.md | Diagrams 1–21, ERDs 22a–22d, Diagram 23 Assistant Payroll Workflow; sources in `diagrams/*.mmd`, rendered output in `rendered-diagrams/` |
| OPEN_QUESTIONS.md | Every undecided item |
| ARCHITECTURE_DECISIONS.md | ADR-001 to ADR-015 |
| IMPLEMENTATION_GUARDRAILS.md | Rules to read before implementing any feature |
| DESIGN_FREEZE.md | Freeze statement, checklist, conflict report |
