# DESIGN_BASELINE.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)

This is the consolidated baseline of the Requirements + Architecture/Design phase (Steps 1–15), **synchronized with the Owner Review decisions and the final Owner business changes** (OWNER_REVIEW_NOTES.md; OPEN_QUESTIONS.md; DESIGN_FREEZE.md). It states what the system is, what is confirmed, what is out of scope, what is architecture, and where the detail lives. Detail is not repeated here: see the document index in section 12.

## 1. Source priority used for this freeze

When two statements disagreed, the higher one won:

1. **The final Owner Review decisions and the latest final business changes** (OWNER_REVIEW_NOTES.md; highest priority; they override older requirements, recommendations, placeholders and open-question defaults).
2. The latest explicit corrections already present in v1.1.
3. The latest approved design.
4. Earlier design.
5. The original requirements (master prompt and clarification sets).
6. Recommendations and technical defaults.

Labels used in every document: **CONFIRMED** (a business rule the user stated), **TECHNICAL** (engineering choice, no business meaning), **DEFAULT** (technical value that may be vetoed), **DEFERRED** (OPS-1; and Q-ORT-3b, DEFERRED / OPEN FOR FUTURE VERSION, with a defined V1 rejection). Every other former question is an Owner decision (OPEN_QUESTIONS.md §2).

**Standing working rules:** *Do not be creative with the business; be creative with the engineering needed to implement it well.* No invented workflows, permissions, finance rules, approval steps, automatic allocations, statuses, ownership rules or user capabilities. Open questions stay open. Items proposed earlier and not confirmed (`ILabAllocationStrategy`, `CaseDoctorAllocation`, TreatmentPlan/FinancialAgreement workflow, OnHold/Abandoned case states, addendum workflow, `PatientContact` as a business aggregate, Manager approval workflows, price lists) are **not requirements**.

## 2. What the system is

A single-clinic dental operations system with three internal application roles (Manager, Doctor, Sales / Reception). The clinic also employs **Assistants**, who are a *staff type only*: they have no account, no login, no role, no application access, no notifications and no operational workflow; the **Manager** maintains their staff record, attendance, overtime, salary and payroll, which feed management financial reporting. Patients are business entities only: no login, no portal, no mobile app. The core loop: Lead (individually attributed) or Walk-In / Direct (no Original Sales Employee, zero Sales commission) → Patient → Appointment (published Doctor availability; manual duration; overlap allowed) → Sales records arrival → Doctor queue → Doctor reads the Medical History Intake if it exists → Visit (Appointment becomes In Treatment). Separately, Sales / Reception collects the structured Medical History Intake for the Patient outside the Doctor room, before the Doctor consultation when possible; Appointment booking never depends on it → Treatment case(s) → Doctor-entered Price (and Consultation Fee; for Fixed / Removable Prosthetics also the fixed lab amount) with a Discount that the Doctor, Sales or Manager may set → Doctor finalizes the Visit → Charge(s) → Payments (Sales/Manager; any excess is Patient Credit) → Revenue split (general lab: Doctor / Lab / Clinic at once; fixed lab: Lab first, then Doctor, then Clinic) → monthly Payroll and reports, with referrals, follow-ups, orthodontic installment tracking, feedback (patient website link or Sales-entered), internal notifications, Manager-maintained attendance and Assistant overtime, and an audit log around it.

Ideas the whole design hangs on:
- **Privacy is structural.** A Doctor never receives phone, WhatsApp, email, address, CRM/contact notes or the Original Sales Representative. Sales / Reception may collect and access only the explicitly whitelisted structured Patient Medical History Intake and the Referral reason (never the referring Doctor's notes); Sales never receives detailed Doctor-authored clinical data. Enforcement is backend-only: storage separation, contract separation, audience DTOs, architecture tests, canary tests.
- **Money is a ledger.** Charge ≠ Payment. Billed, Collected and Outstanding are derived, never stored as balances. History is insert-only.
- **Financial ownership stays with the Doctor who performed the work** (Q-FIN-9, CONFIRMED). The Doctor earns at the percentage snapshotted when the work began, even after patient reassignment or Doctor deactivation; the earning belongs to the payroll month in which the collection actually occurs. Patient assignment and financial ownership are different concepts.
- **Visit ≠ Case.** A visit is one encounter; a treatment case may span many visits.
- **Owner decisions are implemented literally; engineering only makes them safe** (insert-only corrections, deterministic fixed-lab replay, conservation guards).

## 3. Confirmed technical constraints

Modular monolith (no microservices, Kafka, RabbitMQ, event sourcing, Kubernetes or complex CQRS). React + TypeScript SPA; ASP.NET Core Web API; PostgreSQL; EF Core. RBAC with backend authorization. Hangfire (PostgreSQL storage) for background work. Docker, Linux, HTTPS, cloud-hosted, managed PostgreSQL preferred, GitHub Actions CI/CD. Configurable clinic timezone. Optimistic concurrency, transactions, unique constraints, duplicate-payment protection, idempotent scheduled operations. (Appointments may overlap — Q-APP-1.) **UI: Arabic only, right-to-left from the first screen (Q-UI-1).** **Single branch (Q-BRN-1).** No destructive migration at application startup.

## 4. Confirmed business requirements (summary)

Full detail and traceability are in the module sections of the other documents; this table is the one-page statement of what is confirmed.

| Area | Confirmed |
|---|---|
| Roles | Manager (highest operational authority, never destructive history editing; sensitive actions audited), Doctor, Sales / Reception. **Only these three are application roles.** Staff types are Doctor, Sales / Reception employee and **Assistant**; ASSISTANT is a staff type, not an application role. **The Manager creates accounts (username and initial password) and sets a new password when one is forgotten; there is no self-service reset (Q-ACC-1).** |
| Patients | Fields: Patient ID, Full Name, Phone, DOB or Age, Address, Chief Complaint, Original Sales Representative, Registration Date, Active/Inactive. **Medical history is no longer one generic free-text field (v1.1):** it is the structured **Patient Medical History Intake** (0..1 per patient, Patients module): Allergy, High Blood Pressure / Hypertension, Viral Hepatitis, Diabetes, Kidney Problems, Bleeding / Blood Clotting Problems, Heart Problems, Rheumatic Diseases (Yes / No / Not answered); Smoker, Previous Surgical Operations, Currently Taking Medications (Yes / No / Not answered); Last Dental Visit (a text answer, not forced into a date); Pregnant and Breastfeeding (FOR FEMALE PATIENTS ONLY; Yes / No / Not answered or not applicable; V1 stores no Patient sex field, Sales / Reception applies the section when relevant, and non-applicable cases stay NULL). It replaces the old Medical History, Chronic Diseases and Previous Surgeries fields. NOT CONFIRMED / NOT DESIGNED: allergy type, medication names or doses, surgery details, hepatitis / heart / kidney / rheumatic / bleeding details, an "other condition" field. History preserved permanently. Complete timeline. All Doctors may view all patients' clinical history. Manager reassigns a patient's Doctor (previous Doctor, new Doctor, time, Manager, reason stored; past work and its money stay with the performing Doctor; audited). **Patient source (Q-PAT-1): `Lead` patients keep an Original Sales Employee; direct patients are `WalkIn` / Direct with none and generate zero Sales commission.** |
| Doctor visibility | May see name, age/DOB when clinically required, the structured Patient Medical History Intake (read-only; no Doctor write access), chief complaint, full clinical history, prescriptions, clinical notes, implant and ortho info, and the financial breakdown of **own** work. **Never** phone, WhatsApp, email, address, direct contact data, CRM/contact notes or the Original Sales Representative. Search by Patient ID and name only. |
| Sales visibility | May see contact data, **DOB, Referral Destination / Type and Referral Reason, but never the referring Doctor's Notes (Q-PRV-1)**, and the Original Sales Representative of Lead-sourced patients. Whitelist: procedure name, treating doctor, treatment status, appointment/follow-up instructions, price, discount (and may **set** a discount before finalization, through a pricing view with no clinical data), amount paid, remaining, Patient Credit, ortho payment/installment information. **Medical history:** Sales / Reception collects, reads and updates the structured Patient Medical History Intake only (a deliberate, narrow exception). It is separate from Appointment booking and independent of any appointment state. **MUST NOT** see Doctor clinical notes, referral Doctor Notes, root canal measurements, detailed procedure notes, prescription medication details, Doctor percentage, Doctor Share, Clinic Share or protected lab details. |
| Visits and cases | Visit: Draft → In Progress → Finalized (finalizing protects that visit's record, not the whole case; **Charges are established at finalization**). After finalization the owning Doctor may correct their own Visit / Prescription through a controlled, audited correction path without reopening the Visit and without Manager approval (Q-CLN-1). Case: Planned → Active → Completed → Cancelled. |
| Doctor workflow | Patient (from the arrival queue) → Doctor reads the structured Medical History Intake if it exists → Visit → Tooth/Area → Procedure → Details → Steps → Lab expense (if applicable; the Doctor enters the **initial** fixed lab amount for Implant / Fixed / Removable work, before the first such charge can be finalized; the Manager can only revise it later) → **Price, Discount, Total (= Price − Discount; fixed EGP)** → Clinical notes → Prescription → Consultation Fee → Next appointment → Referral → Finalize. The Doctor does not collect payments; **the Doctor, Sales and the Manager may set the Discount before finalization (Q-FIN-3).** Every procedure records the Doctor who performed it, and **every Doctor prices the work they performed (Q-CLN-2)**; the Manager has no initial-price authority and only makes audited corrections. |
| Procedure catalog | Root Canal (canals 1–5 in mm; steps Access Cavity, Mechanical Preparation, Obturation, optional Post & Core); Fillings (GIC / Composite → Anterior / Posterior); Extraction (Simple / Surgical / Advanced Surgical); Pediatric (Pulpotomy, Pulpectomy, Space Maintainer, Stainless Steel Crown, Extraction without type, Zircon Crown, Other); Implant (healing 1–6 months, optional bone graft and membrane); Fixed Prosthetics (Porcelain/PFM, Zirconia, E-max); Removable Prosthetics (Complete/Partial; Flexible/Standard Acrylic); Scaling/Cleaning, Deep Cleaning, Polishing, Laser Bleaching/Whitening (only Laser Bleaching has Basic / High Quality); Orthodontics (Upper Arch, Lower Arch, Appliance + free-text type); Other (free text). The catalog can evolve. |
| Prescriptions | Patient → Visit → Doctor; multiple medications; all Doctors may view history; print layout with clinic branding, **Arabic application text** and **no** patient contact; the owning Doctor may correct a finalized prescription through the controlled path (Q-CLN-1). |
| Scheduling | Calendar, Doctor schedule, **7 statuses** (Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled). **Duration is entered manually per appointment; overlapping appointments for a Doctor are allowed (Q-APP-1/2); when the linked Visit ends early the unused future part of the booked time is available again, without deleting the Appointment.** Sales / Reception **records patient arrival as an event separate from status** (Q-APP-4); the Doctor reads an **arrival queue**; starting the linked Visit sets In Treatment and removes the patient from the queue. **Arrived and Waiting are not appointment statuses.** **Sales and Manager** may Confirm, Cancel, mark No Show (Q-APP-3); Sales may Reschedule; Manager publishes weekly availability. Doctor may create the next appointment and reschedule their own follow-up. A change preserves the original and creates a new appointment. |
| Sales | Leads (8 statuses), referrals (types Surgery/Implant/Orthodontics/Prosthetics/Other; statuses New/Contacted/Booked/Completed/Lost), follow-ups (6 sources; statuses **جديد / تم التواصل / لم يرد** open and **تمت المتابعة** closed — Q-CRM-1; contact attempts tracked, never overwritten), **All Leads (Manager) and My Leads (Sales)**; when a Sales employee becomes inactive their open Leads and Follow-Ups are redistributed evenly among Active Sales (Q-EMP-1), Original Sales attribution is preserved; with no Active Sales they become **unassigned** for the Manager to assign. Sales dashboard (8 items). Sales may also record Materials / Petty expenses and enter Patient Feedback (below). |
| Orthodontics | The **Doctor** enters the Gross Total Price, Deposit and Number of Months during the Visit (Q-ORT-4); **Effective Ortho Total = Gross − Discount = Ortho Charge Billed**; Remaining = Effective − Deposit; the initial installments are equal (the last absorbs rounding) and are generated **once, at Visit finalization** from `Effective − Deposit`; afterwards **only the Manager** edits terms (the only route that changes an established Gross Total / Deposit / Months) and the schedule (amount, due date of an unpaid installment, manual addition, safe retirement; audited, atomic batch); generic price / discount corrections can never change an Ortho Total; a Deposit or Months change regenerates, adds, removes and redistributes nothing automatically: the case is flagged `requires_schedule_adjustment` until Deposit + Σ active installments = Effective Ortho Total and the active regular installments equal the Number of Months (Q-ORT-5); an installment never goes below its paid amount and is never deleted; payments are recorded against the ortho charge and **Sales / Manager manually apply them to installment(s)** (Q-ORT-2); a Gross increase adds a new installment for the difference without re-applying the Discount (Q-ORT-3); a Total **decrease** is not supported in V1 (rejected with `ortho.total_decrease_not_supported`; Q-ORT-3b DEFERRED / OPEN FOR FUTURE VERSION); statuses Upcoming/Due/Partially Paid/Paid/Overdue derived from the applications; Doctor percentage fixed at case start; reminder one day before due. |
| Payments | Cash, Visa, InstaPay; full, partial, multiple payments per charge, multiple charges per patient; never overwritten; Charge and Payment separate; recorded by Sales or the Manager only. **Patient Credit (Q-PAY-3):** overpayment and money paid before a Charge become credit that belongs to the patient and can be applied to future Charges. **Refund, Payment Correction and Payment Reversal are Manager-only, always new ledger entries (Q-PAY-1).** Consultation fees stored and reported separately with no Doctor commission; original price, fixed-EGP discount and final price stored separately; agreement not silently changed. **No printed receipt or invoice in V1 (Q-DOC-1).** |
| Revenue split | Collected = Doctor Share + Lab + Clinic Share. Doctor entitlement = Doctor % snapshot × collected (Lab not removed first). **General lab** = the Manager's percentage **at payment time** (Q-FIN-W2), recognized at once. **Fixed lab (Implant, Fixed/Removable Prosthetics) = one amount per case, entered by the Doctor and revisable by the Manager, collected FIRST (Q-FIN-1):** Fixed Lab → then Doctor entitlement (unpaid part stays pending and carries forward) → Clinic remainder, never negative. A Fixed Lab revision adds correction rows and never changes the Doctor percentage (Q-FIN-6). **Financial ownership stays with the performing Doctor after reassignment or deactivation (Q-FIN-9).** Clinic expenses reduce the Clinic's result only. |
| Doctor finance view | Own work only: price, discount, total, collected, remaining, percentage, entitlement, recognized and pending Doctor Share, Clinic Share, relevant lab, historical revenue. Never clinic-wide figures, other Doctors' data, expenses, Sales payroll, management reports. |
| Payroll | **Monthly (calendar month) for Doctor, Sales and Assistant; Draft → Finalized → Paid; finalized payroll protected (Q-PAY-2).** Doctor: earnings belong to the month of the collection that recognizes them; historical snapshots never recalculated. Sales: base + commission + bonus − deductions; **commission base = collected money after discount; the Manager sets each employee's percentage at creation (required) and may change it later — the new rate applies to future collections only, the rate at collection time applies (Q-COMM-1)**; **Walk-In / Direct collections earn zero commission and there is no pool** (Q-PAT-1); an inactive Sales employee accrues no new commission (Q-EMP-1); Sales attendance is recorded but deducts nothing and Sales has no overtime. Assistant: Manager sets the salary and the hourly overtime rate; payroll has Assistant, month, salary snapshot, **Base Pay, Overtime, final amount (= Base Pay + Overtime)**, status, finalized-at, paid date; **no automatic proration — for a partial month the Manager manually enters the audited Base Pay (Q-PAY-ASST-2)**; overtime = approved hours × the rate snapshot of the entry; Manager-only; Assistants never deleted. No bonus, deduction, commission or incentive for Assistants. **Attendance** (Assistants and Sales; Manager-maintained, one record per person per day) deducts no pay (Q-ATT-1). |
| Expenses | 11 categories; fields ID, date, category, description, amount, payment method, vendor, notes, created by/at, entered-by role. **Sales / Reception may record only Materials / Medical Supplies and Petty / Miscellaneous expenses (create only; Q-EXP-3).** **No attachments (Q-EXP-1); no Finalized state — the Manager edits an expense directly and every edit is audited (Q-EXP-2).** |
| Dashboard and reports | Manager daily report (live; **no Daily Closing**); Daily/Weekly/Monthly/Custom reports; Estimated Operating Result is an operational metric, not accounting profit: Clinic Gross Revenue − Sales compensation − **Assistant Payroll Expense** − Clinic Operating Expenses. **Assistant salary is a monthly item: monthly reports include it; Daily / Weekly reports allocate no fraction; no proration (Q-PAY-ASST-1).** Doctor Payroll is not subtracted again. Pending Doctor entitlement and Patient Credit are memo lines. Assistant payroll shows Base Pay and Overtime separately and only in monthly reports. |
| Feedback and notifications | Visit completed → feedback request → Sales sees it → Sales sends the unique link → patient submits on the clinic website through the token (no login, no expiry) → Submitted (Q-FDB-1/2); **or** Sales enters the Patient's feedback about a Doctor directly (source and actor stored; Q-FDB-3) — for a Visit with a request, the entry links the request and marks it Submitted; at most one result per request. Only the Manager reads feedback results. Seven notification types, Sales recipients, idempotent/retry-safe/timezone-aware. |
| Security and audit | HTTPS, secure hashing (Manager-set passwords; unreadable), RBAC, backend authorization, session management, validation, SQLi/XSS/CSRF protection, auth rate limiting, secrets management, DB backup, least privilege, export restrictions, secure audit log. No physical deletion of treatments, payments, payroll, prescriptions, expenses, lab expenses or doctor assignments. Employees never deleted (Active/Inactive; login disabled; history preserved). |

## 5. Removed and out of scope

**Removed everywhere (must not appear in any schema, API, UI, diagram, requirement or test): X-ray functionality; Inventory management.** "Medical Supplies" exists only as a clinic expense category.

Also removed: the Walk-In commission pool; the refusal to deactivate the last Active Sales employee; Assistant `final-amount` entry; referral notes visible to Sales; Sutures/Bone Removal options on Extraction; an extraction type under Pediatric Extraction; Anterior/Posterior as top-level filling types; Basic/High Quality under Scaling/Deep Cleaning/Polishing; Doctors collecting payments (Doctor, Sales and Manager **may** set the Discount since Q-FIN-3); Daily Closing; expense finalization and attachments; feedback expiry; printed receipts / invoices; digital consent; self-service password reset; attribution holds / Pending Doctor Attribution; the `separatelyPricedWork` flag; the Arrived / Waiting statuses; the old payroll formula (Revenue − Lab) × Commission %; Doctor commission looked up at payroll time; the hardcoded 3-month healing period; Sales commission trigger events (dropped as requirements).

Out of scope for V1: Assistant login, account, role, dashboard, notifications, self-check-in or any application access (an Assistant is a staff record maintained by the Manager, unless the owner explicitly approves more); patient mobile app and accounts; advanced formal accounting; insurance integration; multi-branch (Q-BRN-1: V1 is single branch; no `branch_id`); automated WhatsApp/SMS/email delivery (future, behind the channel abstraction).

## 6. Architecture baseline

**Shape.** One codebase, two processes (`Clinic.Api`, `Clinic.Worker` running Hangfire), one PostgreSQL database, one DbContext, one schema per module. Modular monolith with in-process hand-written command/query/event handlers; no message broker.

**Modules.** The 22 confirmed modules map to **16 implementation modules**:

| Implementation module | Confirmed module(s) |
|---|---|
| Identity | Authentication & Security |
| Audit | Audit Logging |
| Configuration | System Configuration |
| Staff | Doctor Management + Sales employee profiles + Assistant staff records + Manager-maintained attendance and Assistant overtime (no Assistant user account) |
| Patients | Patient Management |
| Scheduling | Appointment & Scheduling |
| Clinical | Clinical Visits + Dental Procedures + Implant Cases |
| Prescriptions | Prescriptions |
| Orthodontics | Orthodontic Cases |
| Crm | Sales/Reception + Referrals & Follow-Ups |
| Finance | Patient Billing & Payments + Finance & Accounting + Lab Expenses |
| Expenses | Clinic Operating Expenses |
| Payroll | Doctor Payroll + Sales Payroll & Commission + Assistant Payroll (Base Pay + Overtime) |
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

Full arrow list: DIAGRAMS.md Diagram 3. Notable rules: **Finance never calls Clinical** (Clinical passes the Doctor, percentage snapshot, lab model and the price / discount snapshot to Finance as data; the pre-finalization discount lives in Clinical, ADR-017); Reporting reads other modules read-only and nothing depends on it; Finance depends on Patients, Staff, Configuration, Audit; Notifications defines ports that source modules implement.

**Data.** Schema = module. Cross-module EF navigation is avoided; **database foreign keys across schemas are kept** (declared in one file, `CrossModuleReferences.cs`, tested against the database). Tables are insert-only (H), lifecycle (L) or mutable audited (M). Guards G1, G2, G4–G8 (+G8b) protect finalized visits (with the controlled Doctor correction), payroll sealing, work-item freeze, allocation caps, fixed-lab eligibility, payment conservation and distribution identity (G3 expense sealing was retired). See DATABASE_DESIGN.md.

**Security.** Four layers per request (authentication, endpoint permission key, resource scope, audience DTO). One operational role per account. Cookie sessions with security-stamp check on every request. See SECURITY_DESIGN.md.

**Finance.** Doctor finalizes the Visit → Charge(s). Payment → allocations to charges and/or Patient Credit (payment = allocations + credit, guard G8). Each collection inserts a Doctor entitlement and runs the Distribution Engine: general lab recognizes Doctor + Lab (rate at payment time) + Clinic at once; fixed lab replays the case's Lab-First waterfall (Lab → pending Doctor entitlement → Clinic remainder), inserting delta rows for revisions and corrections. Ownership stays with the performing Doctor; Doctor earnings belong to the payroll month of recognition. Corrections (Manager only) are new rows. See FINANCE_DESIGN.md.

**API.** Audience-prefixed routes, one OpenAPI document per audience, money as strings, RFC 7807, ETag/If-Match, idempotent payments. Arabic-only SPA over a language-neutral API. See API_ARCHITECTURE.md.

**Deployment.** One DigitalOcean Droplet in the preferred Frankfurt region (Caddy, SPA, api, worker; migrator and backup one-shots) plus Managed PostgreSQL; RPO 5 min / RTO 4 h; expand/contract migrations with a compatibility-based readiness check; three-layer backups; monitoring with no personal data in alerts. See DEPLOYMENT_ARCHITECTURE.md.

## 7. Remaining open or flagged items

| Item | Status |
|---|---|
| **Q-ORT-3b** decrease of an Orthodontic case's total price | **DEFERRED / OPEN FOR FUTURE VERSION**; not a V1 blocker; V1 rejects a decrease with `ortho.total_decrease_not_supported` (no negative installment, no workaround) |
| **OPS-1** exact storage locations of the two backup recovery private keys (the Clinic Manager, as a person, holds both) | **DEFERRED** to deployment / go-live; does not block implementation; a private key is never on the production VM |

Every other former question (Q-UI-1 … Q-BRN-1, OPS-2 … OPS-5 and the final business changes) is **ANSWERED — OWNER DECISION** (OPEN_QUESTIONS.md §2; OWNER_REVIEW_NOTES.md). Technical interpretations T1–T14 of confirmed rules are listed in OPEN_QUESTIONS.md for review; they are not business questions.

## 8. Technical placeholders (not business rules, may be vetoed)

Payroll statuses Draft/Finalized/Paid (now confirmed); attendance statuses Present / Late / Absent (T9); `referrals.follow_up_instructions`; orthodontics lab model = General; the technical Manager key `Reports.Export`; Doctor follow-up booked with the authoring Doctor; first ortho due date one month after the charge date; Lead status open / closed classification as seed data; arrival allowed only on the appointment's day; credit consumed oldest-first.


## 9. Delivery phases referenced by the design

This freeze closes Requirements + Architecture/Design (Steps 1–15). It does **not** begin implementation. The next phase starts only after the user approves the freeze.

## 10. Serious-issue register at freeze

No blocking design inconsistency is identified in this design review (the Final Consistency Gate in DESIGN_FREEZE.md §3f); implementation is not proven. The Owner Review and final business decisions were checked for mathematical impossibility, security, destructive-history, privacy and legal impossibilities (details in DESIGN_FREEZE.md §3c). Items the Owner should knowingly accept: Doctors may open every patient's full clinical history (confirmed; compensating controls are record-access audit and contact isolation); one role per account means a person needing two functions needs an explicitly approved role definition; a fixed-lab case whose Lab exceeds what its charges can fund leaves part of the Doctor entitlement pending indefinitely (the Owner's Lab-First rule, with Clinic Share never negative); a restore forces a Manager password reset for staff; the confirmed RPO 5 min applies to VM and database failures, not to loss of the whole provider account.

## 11. Consistency conflicts found and corrected during the freeze

See DESIGN_FREEZE.md section 4 (C1–C3). **v1.1 business changes made before the Owner approval of 2026-10-20:** (1) Assistant Staff / Payroll / Operating Expense. (2) Scheduling correction (Arrived / Waiting / Check-In removed). (3) Structured Patient Medical History Intake. (4) **Owner Review synchronization:** ~45 Owner decisions applied package-wide. (5) **Final correction pass:** FK implementability audit, Doctor-only initial price / Fixed Lab / Ortho terms, Sales commission rate history (future collections only), feedback integration, expense lookup (DESIGN_FREEZE.md §3e–§3f). (6) **Final Owner review and final business changes:** Sales discount, Manager-maintained Assistant and Sales attendance, Assistant overtime, zero-commission Walk-In / Direct, Sales-recorded Materials / Petty expenses, referral notes hidden from Sales, Sales-entered feedback (DESIGN_FREEZE.md).


## 12. Document index

| Document | Content |
|---|---|
| README.md | Package index and reading order |
| DESIGN_BASELINE.md | This file |
| DATABASE_DESIGN.md | Final schema, guards G1–G8b, cardinalities, source of truth, snapshots |
| SECURITY_DESIGN.md | Authentication, RBAC matrix, scoping, privacy boundary, DB roles, restore security |
| FINANCE_DESIGN.md | Charges, payments, earnings, completions, payroll, reports, finance proofs |
| API_ARCHITECTURE.md | API conventions, endpoint catalog, notifications and jobs |
| DEPLOYMENT_ARCHITECTURE.md | Deployment, CI/CD, migrations, backup, restore, monitoring |
| DIAGRAMS.md | Diagrams 1–21, ERDs 22a–22d, Diagram 23; **Mermaid source only** in DIAGRAMS.md and `diagrams/*.mmd` (no rendered output is shipped or promised) |
| OPEN_QUESTIONS.md | Answered Owner decisions, the two remaining items (Q-ORT-3b DEFERRED / OPEN FOR FUTURE VERSION, OPS-1 DEFERRED) and technical interpretations |
| OWNER_REVIEW_NOTES.md | Owner Decision Register: completed decisions, latest final changes, superseded rules |
| ARCHITECTURE_DECISIONS.md | ADR-001 to ADR-018 |
| IMPLEMENTATION_GUARDRAILS.md | Rules to read before implementing any feature; 94-test traceability matrix plus FC-1…FC-20, OT-1…OT-22 and OC-1…OC-24 |
| DESIGN_FREEZE.md | Freeze statement, checklist, conflict report, synchronization history |
