# OWNER_REVIEW_NOTES.md — Owner Decision Register

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)

This is the authoritative, concise register of **what the Owner decided and which old rules those decisions removed**. It exists so that a reader who finds an old statement in an earlier draft, a chat history or an older ZIP can see why it is gone. The detail of each rule lives in the document named in the "Where" column; OPEN_QUESTIONS.md holds the question register. **The Owner approved Design Baseline v1.1 on 2026-10-20 (see section E).** The decisions in this register are frozen V1 business rules. FRS and implementation have not started.

Source priority: the latest explicit Owner decision always wins over older statements, recommendations and defaults. Nothing here reopens an answered question.

---

## A. Completed Owner Review decisions

| ID | Decision (summary) | Where |
|---|---|---|
| Q-UI-1 | Arabic only, right-to-left from the first screen; no language switch | BASELINE, API, DEPLOYMENT |
| Q-BRN-1 | Single branch; no `branch_id`, no branch module | BASELINE, DATABASE |
| Q-APP-1/2 | Manual appointment duration; overlapping appointments allowed (Sales and Manager); no override workflow; unused booked time is released when the Visit ends early | DATABASE, API |
| Q-APP-3 | Sales and Manager Confirm, Cancel, No Show; Sales also Reschedules | SECURITY, API |
| Q-APP-4 | Patient arrival is an event recorded by Sales; seven statuses, no Arrived / Waiting; the Doctor sees a derived queue | DATABASE, API |
| Q-CRM-1 | Follow-up statuses جديد / تم التواصل / لم يرد (open) and تمت المتابعة (closed) | DATABASE, API |
| Q-FIN-W1 | Charges are established when the Doctor finalizes the Visit (treatment and consultation) | FINANCE |
| Q-FIN-W2 | General lab uses the percentage at payment (allocation) time | FINANCE |
| Q-FIN-1 / Q-FIN-6 | Fixed lab is collected first (Lab-First waterfall); Doctor entitlement is preserved and may be pending; Clinic Share never negative; a lab revision adds correction rows | FINANCE |
| Q-FIN-9 | Financial ownership stays with the Doctor who performed the work; no attribution holds | FINANCE, DATABASE |
| Q-FIN-2 | Sales commission base is collected money; the percentage is required when the Sales employee is created and may be changed later (Q-COMM-1) | FINANCE |
| Q-COMM-1 | The Manager may change the Sales commission rate later; the new rate applies to future collections only | FINANCE, DATABASE, API |
| Q-PAY-1 | Refund, Payment Correction and Reversal are Manager-only, always new ledger rows | FINANCE, SECURITY |
| Q-PAY-2 / Q-PAY-3 | Payroll is monthly (Draft → Finalized → Paid); Patient Credit exists | FINANCE, DATABASE |
| Q-PAY-ASST-1/2 | Assistant payroll is a monthly item with no proration; the Manager enters Base Pay for partial months | FINANCE |
| Q-FIN-DC | No Daily Closing | FINANCE |
| Q-ORT-1…4 | Doctor enters initial Ortho terms; Manager edits terms and installment amounts; Sales / Manager apply payments manually to installments; an increase adds a difference installment | DATABASE, FINANCE, API |
| Q-CLN-1 | A Doctor may correct their own finalized Visit / Prescription through a controlled, audited path; no reopening, no approval | DATABASE, SECURITY |
| Q-CLN-2 | Each Doctor prices the work they performed; no `separatelyPricedWork` flag | FINANCE, API |
| Q-EMP-1 | Open Leads and Follow-Ups are redistributed evenly on Sales deactivation; an inactive Sales employee earns no new commission | CRM, FINANCE |
| Q-ACC-1 | The Manager creates accounts and sets passwords; no self-service reset | SECURITY, API |
| Q-EXP-1/2 | No expense attachments; no expense finalization; the Manager edits expenses directly, audited | DATABASE, API |
| Q-FDB-1/2 | Feedback links never expire; the patient submits on the clinic website through a secret token | DATABASE, API |
| Q-DOC-1/2 | No printed receipts / invoices; no digital consent | API |
| Q-PRV-1 | Sales sees DOB and Referral destination and reason | SECURITY, API |
| OPS-2…5 | No hosting-region restriction; DigitalOcean / Frankfurt; RPO 5 min and RTO 4 h; developer is the primary operator and the Clinic Manager is the backup alert contact | DEPLOYMENT |
| Medical History | The structured intake design is preserved: Sales collects and edits, the Doctor reads, it never blocks booking, no generic free-text history, pregnancy / breastfeeding are female-only questions stored as NULL when not applicable. (An earlier wording that put "Doctor reads Medical History" after the Visit was cleaned up: the Doctor reads it before the Visit.) | DATABASE, SECURITY, API |

## B. Latest final business changes

| # | Change | Where |
|---|---|---|
| 1 | **Sales discount.** The Doctor, Sales / Reception and the Manager may all set the fixed-EGP discount before finalization. Sales does it through a pricing view that contains no clinical data; every write stores actor, role and time. Afterwards only the Manager corrects it. | FINANCE §3, SECURITY, API, ADR-017 |
| 2 | **Assistant has no application access**, but the Manager maintains the Assistant's attendance, overtime, salary and payroll through Manager-only routes. | SECURITY, API, DATABASE |
| 3 | **Attendance** for Assistants and for Sales / Reception, maintained only by the Manager; one record per person per day; attendance alone deducts nothing. | DATABASE, API |
| 4 | **Assistant overtime**: the Manager configures an hourly rate; overtime = approved hours × the rate snapshot of the entry; later rate changes never recalculate old entries; Final = Base Pay + Overtime; Sales has no overtime. | FINANCE §11, DATABASE |
| 5 | **Direct / Walk-In patients generate zero Sales commission**; there is no shared pool. | FINANCE §10 |
| 6 | **Sales may record Materials / Medical Supplies and Petty / Miscellaneous expenses** (create only). | SECURITY, API, DATABASE |
| 7 | **Referral Doctor Notes are never visible to Sales**; the Referral reason and destination are. | SECURITY, API |
| 8 | **Sales may enter Patient Feedback about a Doctor** in addition to the patient's website path; only the Manager reads results. | DATABASE, API |
| 9 | **Fixed / Removable Prosthetics**: the Doctor sets the price and enters the fixed lab amount; the Manager may revise it afterwards. | FINANCE §7 |
| 10 | **A Sales deactivation with no Active Sales** succeeds; open Leads / Follow-Ups become unassigned for the Manager to assign. | CRM, API |
| 11 | **Diagrams are Mermaid source only**: no PNG / SVG / JPG / PDF renders are shipped or promised. | README, DIAGRAMS |
| 12 | **Sales Commission Rate Change.** The Manager sets the rate at creation and may change it later; the new rate applies to **future collections only**; the rate at collection time applies; effective-dated history plus a per-item snapshot; Manager only, audited; no recalculation; finalized payroll immutable; Walk-In / Direct stays zero with no pool. Check: 5 % on 1,000 = 50; after 7 %, 1,000 = 70; total 120. | FINANCE §10, DATABASE §6/§15, API, SECURITY |
| 13 | **Permission-drift correction (final correction pass).** The Manager's initial authority over the work price (`Clinical.WorkItems.EnterPrice`), the consultation fee, the Fixed Lab (`Finance.FixedLab.Enter`) and the Ortho terms (`Orthodontics.Terms.SetInitial`) was removed: the Doctor enters the initial values; the Manager only corrects / revises (`Finance.Charges.Adjust`, `Finance.FixedLab.Revise`, `Orthodontics.Terms.Edit`), audited. The ambiguous keys `Finance.FixedLab.Enter` / `.Edit` are retired. An unknown Fixed Lab is never treated as zero. | SECURITY, API, FINANCE, DATABASE |
| 14 | **Feedback integration.** A Sales-entered feedback about a Visit with a request links that request and marks it Submitted; the public token then behaves as already submitted; general feedback may have no Visit and no request; at most one accepted result per request; no expiry. | DATABASE §16, API |
| 15 | **Expense lookup and creator.** Sales gets a read-only allowed-categories lookup; Sales cannot manage categories, read analytics or edit expenses; the creator and creation time are preserved on Manager edits. | API, SECURITY, DATABASE §14 |
| 16 | **Foreign-key audit.** Every documented FK was checked for PostgreSQL implementability (DATABASE_DESIGN.md §24); the invalid `pricing_discount_entries` composite and the unbacked composites were corrected. | DATABASE |
| 17 | **Orthodontics freeze correction.** (a) **Loophole closed:** generic `PriceCorrection` / `DiscountCorrection` / `Finance.Charges.Adjust` can never change an established Ortho Total (`ortho.use_terms_edit`); every Ortho Total, Deposit and Months change goes through `Orthodontics.Terms.Edit` (guard G9). (b) **Total increase** adds one positive difference installment and leaves existing installments untouched. (c) **Total decrease is not supported in V1** (`ortho.total_decrease_not_supported`; no negative installment, no workaround); Q-ORT-3b stays DEFERRED / OPEN FOR FUTURE VERSION. (d) **Deposit or Months change after installments exist (Q-ORT-5, OWNER DECISION):** no regeneration or redistribution; the Manager manually edits the schedule; a derived `requires_schedule_adjustment` flag (not a status) shows until Deposit + Σ active installments = Effective Ortho Total (Gross − Discount; see Change 18) and the month plan conforms. (e) **Paid history is immutable:** no installment below its paid amount, none with payment history deleted, no payment moved. (f) Initial equal installments are generated once, at Visit finalization; authority unchanged (Doctor initial, Manager later, Sales payments only). | DATABASE §11/§18, FINANCE §13, SECURITY, API, GUARDRAILS §13c |
| 18 | **Orthodontic consistency correction (Effective Total and Months).** (a) **Discount arithmetic:** a pre-finalization Discount applies to the Ortho line too, so the schedule balances against the **Effective Ortho Total = Gross − Discount = Ortho Charge Billed**, never the undiscounted Gross (Gross 20,000, Discount 2,000, Deposit 5,000, Months 5 → installments 13,000; 5,000 + 13,000 = 18,000; no extra 2,000 outstanding). (b) **Deposit** must satisfy `0 ≤ Deposit ≤ Effective` at finalization (`ortho.deposit_exceeds_effective_total`; never silently altered). (c) **Timing:** `SetInitial` stores the terms during the Visit; the Charge and the equal initial installments are created **only at Visit finalization**, once. (d) **Gross increase** adds one `PriceChange` installment equal to the increase; the Discount snapshot is not re-applied. (e) **Number of Months is operational:** the Manager's atomic batch can revise amounts, revise due dates of unpaid installments, **add** `ManagerAdded` installments and **retire** unpaid ones (revision, never delete); `requires_schedule_adjustment` covers both the amount balance and the month plan (active regular installments = Months); an impossible reduction is rejected (`ortho.months_conflict_with_paid_history`). (f) Paid history stays immutable. | DATABASE §11/§18, FINANCE §13, API A8, SECURITY, GUARDRAILS §13d |

## C. Superseded rules (removed from the active design)

| Old rule | Replaced by | Why |
|---|---|---|
| Doctor and Manager only may set a discount; Sales has no discount authority | Doctor, Sales and Manager (Change 1) | Owner decision |
| Walk-In commission pool shared equally by Active Sales; pool tables, pool payroll lines and the pool rate default Q-PAT-1b | Walk-In / Direct earns zero commission (Change 5) | Owner decision; the default is withdrawn |
| Refuse to deactivate the last Active Sales employee while open Leads / Follow-Ups exist (T7) | Deactivation succeeds; items become unassigned (Change 10) | An inactive employee must never be blocked by missing colleagues |
| Assistant `final_amount = salary_snapshot` or a Manager-entered final amount; no overtime, no attendance for Assistants | Base Pay + Overtime; attendance; overtime (Changes 2–4) | Owner decision |
| `case_work_items.entered_discount` (a Doctor-only field on the work item) | Insert-only `clinical.pricing_discount_entries` | Needed for history, concurrency and the Sales audience |
| `patient_contacts.acquisition_source` | `patient_source` (`WalkIn` / `Lead`) | Names the Owner's Walk-In / Direct concept; no Original Sales Employee for Walk-In |
| `feedback_requests` stored the rating and comment; request status "Submitted" stored | `feedback_entries` is the only place for rating and comment; request state is derived | Two entry paths need one table |
| Referral `notes` visible to Sales in the shared DTO | `notes` only on the Doctor DTO | Change 7 |
| Guard G8c (pool shares sum to the pool) | Removed with the pool | Change 5 |
| Rendered diagram files (`rendered-diagrams/`, PNG / SVG) and documents promising renders | Mermaid source only | Owner instruction |
| The Manager may enter the initial work price / consultation fee, the initial Fixed Lab (`Finance.FixedLab.Enter`) or the initial Ortho terms | Doctor-only initial entries; Manager corrections / revisions only (Change 13) | Permission drift found in the final correction pass |
| One `commission_percentage` column on the Sales employee and one `commission_percentage_snapshot` per Sales payroll | Effective-dated `sales_commission_rate_history` and a per-item `commission_percentage_applied` (Change 12) | A rate change must not recalculate old collections |
| Unique-per-request feedback applied to the web path only; Sales-entered feedback never linked to a request | One accepted result per request regardless of source; Sales-entered Visit feedback links the request (Change 14) | A submitted request must not stay Pending |
| `Fixed Lab L = 0 if none yet` in the waterfall | Fixed Lab revision 1 is required before the first Fixed-lab charge; unknown is never zero | Change 13 |
| `pricing_discount_entries` composite FK `(case_work_item_id, visit_id)` | Two direct nullable FKs with an XOR CHECK | Not implementable in PostgreSQL (Change 16) |
| An older Q-ORT-3b position routed an Ortho Total reduction through a generic Manager `DiscountCorrection` / `PriceCorrection` adjustment (REMOVED: no longer valid anywhere) | `Orthodontics.Terms.Edit` is the only route; generic adjustments refuse Ortho charges (Change 17) | It was a bypass of the Ortho business validation |
| "Σ installments must always equal Total − Deposit" enforced as an automatic regeneration after any terms edit; `ortho.installment_sum_mismatch` | Derived `requires_schedule_adjustment`; manual Manager adjustment; `ortho.schedule_unbalanced` / `ortho.month_plan_mismatch` (Changes 17–18, Q-ORT-5) | Owner decision: do not guess a redistribution |
| "Deposit + Σ installments = Total Price" where Total Price is the undiscounted Gross; "installments generated at initial creation"; "revision 1 entered at Visit finalization"; "no create-installment route" together with a changeable Number of Months | Balance against the Effective Ortho Total; terms stored during the Visit, schedule generated at finalization; the Manager's batch can add and retire installments (Change 18) | Found by the final independent review: the discount and the Months field made those statements contradict each other |
| Earlier stale statements (older ZIP) listing the Owner Review questions as OPEN | ANSWERED — OWNER DECISION | They were answered; see section A |

## D. Genuinely remaining items

| ID | Status | Note |
|---|---|---|
| **Q-ORT-3b** | **DEFERRED / OPEN FOR FUTURE VERSION** | What an Orthodontic case's *total price decrease* should do. V1 behavior is defined: rejection with `ortho.total_decrease_not_supported`. Not a V1 blocker; no other design depends on it. |
| **OPS-1** | **DEFERRED** | Where the two backup recovery private keys are physically stored. The Clinic Manager, as a person, holds both; the locations are chosen at go-live. |

Nothing else is open.

## E. Owner approval

**OWNER APPROVAL (2026-10-20):** The Owner reviewed and approved Design Baseline v1.1 after the final Orthodontic consistency correction. The decisions in this register are now **frozen V1 business rules**. Status: **OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION**. The decision history above is unchanged. Any later change to a business rule requires an explicit Owner change request, an impact review, a new baseline version, a synchronized document update and Owner approval of that new baseline (DESIGN_FREEZE.md §7).
