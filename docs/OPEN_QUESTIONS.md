# OPEN_QUESTIONS.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Last synchronization:** Owner approval and freeze (2026-10-20) after the final Orthodontic consistency correction; before that, the Final Orthodontics freeze correction after the final correction pass (see OWNER_REVIEW_NOTES.md for the full decision register and DESIGN_FREEZE.md). The Deposit / Number-of-Months edit behavior of an Orthodontic case is now ANSWERED (Q-ORT-5); the Sales commission-rate change question was answered earlier (Q-COMM-1).

This is the single and authoritative decision register of Design Baseline v1.1 (OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION). **All V1 business questions are answered.** Exactly two items are deferred, with a defined V1 behavior or a go-live timing, and **neither blocks V1 implementation**; no new open question is created by the freeze. This is the single register of questions. **Every question the Owner has answered is ANSWERED — OWNER DECISION and is not reopened.** Exactly **two deferred items remain**: **Q-ORT-3b** (what the system should do when the total price of an Orthodontic case is *decreased*; **DEFERRED / OPEN FOR FUTURE VERSION**; V1 has a defined behavior, rejection with `ortho.total_decrease_not_supported`, so it is not a V1 blocker) and **OPS-1** (the physical storage locations of the two backup recovery private keys; DEFERRED to go-live). Neither blocks implementation. The earlier Walk-In pool default (Q-PAT-1b) no longer exists: the Owner's final decision is that Walk-In / Direct work earns zero commission.

Labels: **CONFIRMED** = Owner decision. **TECHNICAL** = engineering needed to implement a confirmed rule safely. **DEFAULT** = veto-able technical default. **DEFERRED** = Owner decided it is decided later and does not block implementation.

---

# 1. REMAINING OPEN / DEFERRED

### Q-ORT-3b — DEFERRED / OPEN FOR FUTURE VERSION: decrease of an Orthodontic case's total price
- **Status:** **DEFERRED / OPEN FOR FUTURE VERSION.** Not a V1 blocker.
- **AREA:** Orthodontics / Finance
- **WHAT IS ANSWERED:** An *increase* adds a difference installment (Q-ORT-3). The schedule is Manager-editable (Q-ORT-1); Deposit / Months edits are handled by manual schedule adjustment (Q-ORT-5). The balance is always against the Effective Ortho Total (Gross − Discount).
- **WHAT IS DEFERRED:** What the system should do when the Owner reduces the total price of an Orthodontic case after installments exist (which installments shrink; how collected money, Patient Credit and Doctor entitlement are treated).
- **V1 BEHAVIOR (defined, not a gap):** a Total decrease (for example 20,000 → 18,000) is **rejected with `ortho.total_decrease_not_supported`**. There is no negative installment, no automatic reduction of installments, no `DiscountCorrection` / `PriceCorrection` workaround (the generic correction route refuses Orthodontic charges with `ortho.use_terms_edit`) and no silent rewrite. Nothing is built for a decrease.
- **WHAT IT BLOCKS:** Nothing. It can be designed in a future version; the single place that relaxes is the V1 rejection in `Orthodontics.Terms.Edit` (and guard G9(b)).

### OPS-1 — DEFERRED (does not block implementation)
- **AREA:** Operations / backup security
- **DECIDED (CONFIRMED):** The **Clinic Manager, as a person, holds both backup recovery private keys.**
- **DEFERRED:** The exact physical / storage locations of the two private keys are decided at deployment / go-live. **No storage location is invented in this package.**
- **RULES THAT STAND:** A recovery private key is **never stored on the production VM**; either key alone decrypts (redundancy, not two-person approval); losing both makes every off-site backup unreadable. Holding a key is a custody duty of that person and grants or requires no application permission.
- **WHAT IT BLOCKS:** Only configuring the two public keys in production and the first production backup. No application code waits for it.
- **WHEN:** Before go-live (before the first production backup).

---

# 2. ANSWERED DECISIONS — OWNER DECISION

Each row is **ANSWERED — OWNER DECISION**. "Applied in" lists the documents carrying the rule. Old defaults and technical placeholders for these questions were removed from the active design; they survive only in DESIGN_FREEZE.md's change history.

## 2.1 Frontend, scheduling, CRM

| ID | ANSWERED — OWNER DECISION | Applied in |
|---|---|---|
| **Q-UI-1** | UI is **Arabic only**; RTL from the first screen; Arabic labels, user-visible validation messages, reports and application-generated printable documents (prescription print layout where application text is involved). API codes stay language-neutral. **No bilingual switching in V1.** | BASELINE §5, API §2, DEPLOYMENT, GUARDRAILS |
| **Q-APP-1** | Appointment duration is entered **manually per appointment** by Sales / Reception. No clinic-wide duration, no procedure-duration mapping. **Overlapping appointments for the same Doctor are allowed.** When the Doctor ends the linked Visit earlier than the booked duration, the unused future part of the booked time becomes **available again** (availability release from the actual Visit end; the Appointment and its history are never deleted or altered). | DATABASE §8, API A8, FINANCE n/a, DIAGRAMS 08 |
| **Q-APP-2** | The Manager may create overlapping appointments; **no separate override workflow**; the old conflict rejection is removed. | DATABASE §8, API A8, SECURITY §4 |
| **Q-APP-3** | **Sales / Reception and Manager** may **Confirm, Cancel and mark No Show**. Sales / Reception also retains **Reschedule**. | SECURITY §4, API A8 |
| **Q-APP-4** | New confirmed workflow: **Sales / Reception records patient arrival** as an event / timestamp **separate from Appointment status**. The seven statuses stay exactly Scheduled, Confirmed, In Treatment, Completed, Cancelled, No Show, Rescheduled (**no Arrived, no Waiting**). The Doctor reads a **queue derived from arrival** (arrived, not yet started, non-terminal appointment). When the Doctor starts the linked Visit the Appointment becomes In Treatment and the patient leaves the queue. | DATABASE §8, API A8/A9, SECURITY, DIAGRAMS 08/06 |
| **Q-CRM-1** | Follow-up statuses are exactly **جديد (New), تم التواصل (Contacted), لم يرد (NoResponse), تمت المتابعة (FollowedUp)**. **Open = New, Contacted, NoResponse. Closed = FollowedUp.** This predicate drives Pending Follow-Ups, follow-up notifications / jobs, dashboard counts, inactive-owner handling and validation. No other status exists. | DATABASE §12, API B3/B6, BASELINE |
| **Q-EMP-1** | Manager UI: **All Leads** table with Responsible Sales Member. Each Sales employee: **My Leads**. When a Sales employee becomes Inactive, their **open Leads and open Follow-Ups are redistributed evenly among Active Sales employees**; original Sales attribution and responsibility history are preserved. **If no Active Sales employee exists, the deactivation still succeeds and the open Leads / Follow-Ups become unassigned / pending** (responsible = NULL), visible to the Manager, who assigns them. An inactive Sales employee receives **no new commission** after the deactivation date (different from Doctors). | DATABASE §12, API, SECURITY, FINANCE §10 |
| **Q-PAT-1** | A direct patient is explicitly classified **Walk-In / Direct** (`patient_source`) and has **no Original Sales Employee**. **Money collected from a Walk-In / Direct patient generates ZERO Sales commission, and there is no shared pool** (this final decision replaces the earlier monthly pool rule). Lead-sourced patients keep individual attribution; the Sales employee who merely registers a Walk-In patient earns nothing. | DATABASE §7/§13/§15, FINANCE §10, API |

## 2.2 Finance, payments, payroll

| ID | ANSWERED — OWNER DECISION | Applied in |
|---|---|---|
| **Q-FIN-W1** | Doctor enters the price and the Consultation Fee (the **Manager has no initial-price authority**; the discount may be entered by Doctor, Sales or Manager) during the Visit; **Charge(s) are established when the Doctor Finalizes the Visit** (treatment and consultation). No Sales / Manager approval before establishment. Consultation stays separate and earns no Doctor commission. | FINANCE §3, DATABASE §9/§13, API |
| **Q-FIN-3** | **Doctor, Sales / Reception and Manager** may set a Discount before the Visit is finalized (Sales without any clinical-note access, through a narrow pricing view). **Total = Price − Discount**; Discount is a **fixed EGP amount**, never a percentage. Each write records actor, role and time; the Charge records Original Price, Discount, Final Price and the setter. After the Charge exists only the Manager corrects it, through the audited correction model. Commission and Doctor share follow the money actually collected. | FINANCE §3, SECURITY §4, API, ADR-017 |
| **Q-FIN-W2** | General Lab percentage = the rate **active at payment (allocation) time**. A later change never changes earlier payments and never changes a Doctor's entitlement. | FINANCE §6 |
| **Q-FIN-1** | **Fixed-lab Lab-First collection order** (Implant, Fixed Prosthetics, Removable Prosthetics; **the Doctor sets the price and enters the initial fixed lab amount — before the first Fixed-lab priced work is financially finalized, and an unknown Fixed Lab is never treated as zero — and the Manager may only revise it afterwards**): Fixed Lab is covered first; then Doctor entitlement is recognized; an unpaid entitlement stays **pending** and carries forward; Clinic Share receives only the remainder and **never becomes negative**. The Doctor's entitlement is preserved even while recognition is deferred. Replaces the old "Doctor earning immediately on every fixed-lab allocation". General lab is separate. | FINANCE §7, DATABASE §13 |
| **Q-FIN-6** | A Fixed Lab revision after distributions exist **preserves all rows** and adds correction / reconciliation rows so historical Lab / Clinic distributions reflect the revised amount. Never changes the Doctor percentage / ownership rule. | FINANCE §7.4 |
| **Q-FIN-9** | **Financial ownership stays with the Doctor who performed the work**, regardless of patient reassignment or that Doctor becoming inactive. Later-collected earning belongs to the **payroll month in which the collection actually occurs**. **Attribution holds, `PendingDoctorAttribution`, `PatientReassigned` / `DoctorDeactivated` finance handlers are removed.** | FINANCE §8, DATABASE §13 |
| **Q-FIN-2** | **Sales commission base = collected money** after discount (not billed value, not uncollected treatment value, not Clinic Share; Walk-In / Direct collections are excluded, see Q-PAT-1). The **Manager sets each Sales employee's commission percentage when creating the employee / account; it is required** and may change it later (Q-COMM-1). | FINANCE §10, DATABASE §6, API |
| **Q-COMM-1** | **ANSWERED (was a pending question):** the Manager may **change a Sales employee's commission rate later**. A new rate applies to **FUTURE collections only**; the rate in force **at collection time** applies to each collection; old collections, Draft items and finalized payroll are **never recalculated**. Manager only, audited. Walk-In / Direct stays zero with no pool. Implemented as effective-dated `sales_commission_rate_history` plus a per-item rate snapshot. Example: 5 % on 1,000 = 50; after the change to 7 %, 1,000 = 70; total 120; Walk-In 0. | DATABASE §6/§15, FINANCE §10, SECURITY §4, API (`PUT /sales-employees/{id}/commission`: future collections only), ADR-018 |
| **Q-PAY-1** | **Manager only** performs Refund, Payment Correction and Payment Reversal, always through new correction / reversal ledger entries (never destructive edits), propagating to Doctor entitlement, Lab, Clinic Share and Payroll; all audited. | FINANCE §9 |
| **Q-PAY-2** | Payroll is **monthly** for Doctor, Sales and Assistant, on **calendar-month** periods; statuses **Draft → Finalized → Paid**; finalized payroll stays protected. | FINANCE §11, DATABASE §15 |
| **Q-PAY-3** | **Patient Credit** is supported: overpayment remainder and money paid before a Charge exists become Patient Credit, belong to the patient, persist and can be applied to future Charges. The old rule "Σ allocations = whole payment" is removed (payment = allocated + credit). | FINANCE §5, DATABASE §13 |
| **Q-PAY-ASST-1** | Assistant salary is a **monthly** expense / reporting item. **No proration** into Daily / Weekly / partial Custom reports (no ÷30, no calendar-day or working-day division). Monthly reports include the monthly Assistant payroll expense. The old PENDING / INCOMPLETE behavior is removed. | FINANCE §12 |
| **Q-PAY-ASST-2** | **No automatic proration.** For a mid-month join / leave / salary change / other partial case, the **Manager manually enters the Base Pay**, audited. A full normal month defaults to the salary snapshot. The salary snapshot is preserved. **Final = Base Pay + Overtime.** | FINANCE §11.3, DATABASE §15 |
| **Q-ATT-1** | **Attendance** is kept for **Assistants and Sales / Reception employees**, entered and edited **only by the Manager** (no self-check-in, no Assistant account). One record per person per day. **Attendance alone deducts no pay.** | DATABASE §5, SECURITY §4, API A8 |
| **Q-OT-1** | **Assistant overtime**: the Manager configures an hourly overtime rate per Assistant and records approved overtime hours; **amount = approved hours × the rate snapshot taken when the entry is created**; a later rate change never recalculates earlier entries. Overtime is included in the Assistant's monthly payroll as its own line. Sales has no overtime. Overtime is never created automatically from attendance. | DATABASE §5/§15, FINANCE §11, API |
| **Q-FIN-DC** | **No Daily Closing** feature (no Close Day, no sealed daily state). The Manager Daily Report stays live. | FINANCE §12 |

## 2.3 Orthodontics, clinical

| ID | ANSWERED — OWNER DECISION | Applied in |
|---|---|---|
| **Q-ORT-4** | The Doctor initially enters the **Gross Total Price, Deposit, Number of Months** during the editable Visit (stored only). The initial schedule is generated **at Visit finalization**, from `Effective Ortho Total − Deposit`, where **Effective Ortho Total = Gross − Discount = Ortho Charge Billed**. The Manager may edit / correct the terms later, audited. Sales does not set terms. | DATABASE §11, FINANCE §13, API |
| **Q-ORT-1** | Initial installments are equal (the last one absorbs rounding) and are generated once, at Visit finalization. Afterwards **only the Manager** edits the schedule (installment amounts, due dates of unpaid installments, manual additions, safe retirement; audited). Doctor and Sales cannot. | DATABASE §11, API |
| **Q-ORT-2** | Payments are recorded against the Ortho Charge; **Sales / Reception or Manager manually selects** the installment(s) a payment is applied to (one→one, one→many, partial, early, future). **No automatic oldest-first.** Installment paid / remaining / status are derived from these application rows. `installment_payments` is activated. | DATABASE §11, FINANCE §13 |
| **Q-ORT-3** | If the Ortho Gross Total is **increased** (the Discount snapshot is not re-applied; Billed rises by the same amount), existing installments are **not** redistributed; a **new installment for the difference** is added (20,000 → 24,000 adds 4,000). A *decrease* is **not supported in V1** (rejected with `ortho.total_decrease_not_supported`): see Q-ORT-3b (DEFERRED / OPEN FOR FUTURE VERSION). Generic `PriceCorrection` / `DiscountCorrection` can never change an Ortho Total (`ortho.use_terms_edit`); all Ortho Total changes go through `Orthodontics.Terms.Edit`. | FINANCE §13, DATABASE §11 |
| **Q-ORT-5** | **Deposit or Number of Months edited after installments exist (OWNER DECISION):** the system does **not** automatically regenerate, add, remove or redistribute installments. The Manager **manually adjusts the schedule** through one atomic, audited batch: revise the amount of an eligible installment, revise the due date of an unpaid installment, **add** a future `ManagerAdded` installment, **retire** an unpaid installment (a revision, never a delete). Example (no discount): Effective 20,000, Deposit 5,000 → 7,000: the installments must now sum to 13,000; with a 2,000 Discount the Effective Total is 18,000 and the required sum is 11,000. Months 10 → 12 / 10 → 8 changes the month plan: nothing is created or removed until the Manager does it. Until **Deposit + Σ active installments = Effective Ortho Total** *and* the active regular installments equal the Number of Months, the case is flagged (derived `requires_schedule_adjustment`, not a new status) and the schedule is not treated as valid. A Months value lower than the regular installments that already have payments is rejected (`ortho.months_conflict_with_paid_history`). Paid history is immutable: no installment goes below what was applied to it, none with payment history is deleted, retired or re-dated, and no payment moves between installments. Sales and the Doctor cannot edit the schedule. | DATABASE §11, FINANCE §13, SECURITY §4, API A8, GUARDRAILS §13c–§13d |
| **Q-CLN-1** | The Doctor may correct their **own** finalized Visit / Prescription; **no Manager approval**. The Visit is **not reopened**. A controlled, audited correction path records Doctor, timestamp, record, changed fields and the correction relationship; historical evidence is never silently overwritten. G1 updated. | DATABASE §9/§18, SECURITY, API |
| **Q-CLN-2** | **Every Doctor enters the price of the work they performed** after their Visit; each Doctor earns only from their own priced work; no automatic revenue sharing. The manual `separatelyPricedWork` flag is **removed**. Orthodontics keeps its single-charge model. | FINANCE §4, DATABASE §9 |

## 2.4 Accounts, expenses, feedback, documents, privacy, scope

| ID | ANSWERED — OWNER DECISION | Applied in |
|---|---|---|
| **Q-ACC-1** | The **Manager creates accounts** (chooses username and initial password) and gives credentials to the employee directly. **No self-service password reset**; a forgotten password is replaced by the **Manager setting a new password**. Passwords stay securely hashed; the Manager can never read an old password; no email / SMS reset delivery. | SECURITY §2, API A2/A3, DATABASE §3 |
| **Q-EXP-1** | **No expense file attachments** in V1. | DATABASE §14, API |
| **Q-EXP-2** | Expenses have **no Finalized state**; the Manager edits an expense directly and **every edit is audited** (who, when, changed fields). No `finalized_at`, no adjustment rows for ordinary correction. | DATABASE §14/§18, API |
| **Q-EXP-3** | **Sales / Reception may record daily Materials / Medical Supplies and Petty / Miscellaneous expenses** (create only: no list, edit, reports or other categories). Entries show who created them and in which role (the creator is never overwritten by a Manager edit). Sales reads the allowed categories through a read-only lookup and cannot manage categories. | DATABASE §14, SECURITY §4, API |
| **Q-FDB-1** | Feedback requests / links **do not expire** in V1. No Expired status, `expires_at`, job J6 or timeout. | DATABASE §16, API B6/B10 |
| **Q-FDB-2** | Visit Completed → Feedback Request → Sales sees it and sends the unique link → Patient submits on the **clinic website** (secure unguessable token, no login, no manual identification, no predictable patient id) → Submitted. | DATABASE §16, API, SECURITY §3 |
| **Q-FDB-3** | **Second path:** Sales / Reception may **enter Patient Feedback about a Doctor directly** (rating, comment; source `SalesEntered`, actor stored). When it is about a Visit that has a feedback request, the entry **links that request and marks it Submitted** (the public token then behaves as already submitted); general feedback may have no Visit and no request; at most one accepted result per request. Only the **Manager** reads feedback results; Doctors and Sales do not. | DATABASE §16, SECURITY §4, API |
| **Q-DOC-1** | **No printed receipt, printed invoice or tax-compliant invoice** in V1. Payment confirmation is system data only. | API, DEPLOYMENT |
| **Q-DOC-2** | **No digital consent forms**, e-signature or consent document storage in V1. | API |
| **Q-PRV-1** | Sales / Reception **may see Patient DOB, Referral Destination / Type and Referral Reason**, but **never the referring Doctor's Notes** (`notes` is absent from every Sales DTO). Sales still receives no clinical notes, root-canal measurements, procedure notes, prescription medication details, Doctor percentage, Doctor Share, Clinic Share or protected Lab financial data. The structured Medical History exception is unchanged. | SECURITY §4, API |
| **Q-BRN-1** | **Single branch.** No `branch_id`, branch module, selector or cross-branch report. Multi-branch is future scope. | BASELINE |

## 2.5 Operations

| ID | ANSWERED — OWNER DECISION | Applied in |
|---|---|---|
| **OPS-2** | **No Owner-imposed country / region restriction** for hosting. A technically suitable provider / region is used; production must comply with applicable law and provider requirements at deployment time. "Egypt-only hosting" is not encoded. | DEPLOYMENT §1 |
| **OPS-3** | **DigitalOcean**, preferred region **Frankfurt**. Linux Droplet (about **2 vCPU / 4 GB RAM**) running Caddy, the React SPA, the ASP.NET Core API and the Worker, with **Managed PostgreSQL**. Sizing stays configurable and validated at deployment; no pricing claim in the architecture. | DEPLOYMENT §1 |
| **OPS-4** | **RPO = 5 minutes, RTO = 4 hours** for the realistic failures; a total provider / account loss is an honest exception documented in DEPLOYMENT §3.2. | DEPLOYMENT §3 |
| **OPS-1a** | The **Clinic Manager, as a person, holds both backup recovery private keys** (the physical locations are the deferred part, OPS-1 above). | DEPLOYMENT §3 |
| **OPS-5** | Primary operator: **project developer / system owner**. Backup critical-alert contact: **Clinic Manager**. Application role permissions are not tied to on-call or backup custody. | DEPLOYMENT §3/§4 |

---

# 3. TECHNICAL INTERPRETATIONS (not questions)

These are engineering readings of confirmed rules, listed so the Owner can veto any of them. None is an open business question.

| # | Interpretation | Why it is needed |
|---|---|---|
| T1 | The moment of "collection" for Doctor earning, Sales commission and the payroll month is the moment money is **allocated to a Charge** (for a direct payment = payment time; for Patient Credit = the moment the credit is applied). Unapplied credit is not yet commission or entitlement. | Patient Credit rule |
| T2 | General Lab "rate at payment time" = rate at the allocation moment (identical to payment time for direct payments). | Same |
| T3 | Sales commission base = net allocations of **all Charge kinds** (treatment, ortho, consultation) collected from the Sales employee's attributed patients; refunds / reversals reduce it in the month they occur. Consultation earns no **Doctor** commission (unchanged). | Q-FIN-2 says "collected money" |
| T4 | Sales commission does not accrue to a deactivated employee after the deactivation date and is **not redirected** to anyone else; it is shown on a "not accrued — inactive Sales" report line. Walk-In / Direct money never generates commission. | Q-EMP-1, Q-PAT-1 |
| T5 | Arrival can be recorded only for a Scheduled / Confirmed appointment of the current clinic-local day; arrival does not gate starting a Visit; the Manager is not granted the arrival action (only Sales / Reception was named). | Q-APP-4 |
| T6 | General-lab percentage and Doctor percentages are validated so Doctor % + Lab % ≤ 100 (Clinic Share cannot go negative on general-lab cases). | Owner: Clinic Share never negative |
| T7 | With no Active Sales employee left, open Leads / Follow-Ups become **unassigned** (NULL responsible) rather than the deactivation being refused; unassigned items trigger no reminder until the Manager assigns them. | Q-EMP-1 |
| T8 | Sales base salary for a partial month is not prorated by the system (no rule given); the Manager uses Bonus / Deduction adjustments. Attendance never changes Sales pay. | No Owner rule |
| T9 | Attendance statuses are `Present`, `Late`, `Absent` with optional check-in / check-out times (the Owner named attendance but no status list). | Q-ATT-1 |
| T10 | A pre-finalization discount cannot exceed the line's price; two simultaneous writers are resolved by an `entry_seq` optimistic token (the later writer gets a conflict). | Q-FIN-3 |
| T11 | A Doctor lowering a price below the existing discount is refused; the Visit's discount belongs to the pricing line, not to the person who last typed it. | Q-FIN-3 |
| T12 | "Rate at collection time" for Sales commission = the rate-history revision with the greatest `effective_from ≤ allocated_at`; a reversal / refund row uses the rate of the allocation it reverses; revision `effective_from` is the database time of the save (no back-dating, no future-dating). | Q-COMM-1 |
| T13 | "Before the first Fixed-lab priced work is financially finalized" = before the Visit that establishes the case's first Fixed-lab Treatment charge can be finalized; so revision 1 always precedes every charge and collection. | Q-FIN-1 |
| T14 | A feedback request's state is derived (Pending / Sent / Submitted); "Submitted" = an entry of either source exists for it. | Q-FDB-2/3 |

---

## NOT CONFIRMED / NOT DESIGNED (not open questions and not implemented)

Structured Patient Medical History Intake: allergy detail or type; medication names, doses or details; previous-surgery details; hepatitis type; heart, kidney, rheumatic or bleeding-disorder details; any "other medical condition" field. Pregnancy and Breastfeeding are female-patient-only intake questions; V1 stores no Patient sex field; unanswered / not-applicable stays NULL.

Assistants: bonuses, deductions, attendance-based deductions, commissions and incentives are not confirmed and not designed. (Overtime and Manager-maintained attendance **are** confirmed, Q-OT-1 and Q-ATT-1.)

## Items that are NOT open (for the record)

- Address and Original Sales Representative are hidden from Doctors; one operational role per account; Doctors collect no payments; Doctors have no notification inbox in V1.
- Assistants are a staff type only (no login, account, role, dashboard, notifications or API access).
- Inventory and X-ray are removed from scope and must not appear anywhere.
- V1 is single clinic, single branch, no patient account / mobile app, no insurance integration, no digital consent, no printed invoice / receipt, no expense attachments, no Daily Closing, Arabic-only UI, no rendered diagram files (Mermaid source only).

## Technical defaults that are not questions (may be vetoed)

Session timeouts, password policy, lockout and rate limits, reminder planner intervals and availability window, retry cap 5, `Africa/Cairo` default timezone, Doctor follow-up booked with the authoring Doctor, 30-day log ceiling, sizing defaults.
