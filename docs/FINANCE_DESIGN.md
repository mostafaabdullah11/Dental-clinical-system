# FINANCE_DESIGN.md

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Source:** Step 9 as approved, synchronized with the completed Owner Review and the final Owner business changes (OWNER_REVIEW_NOTES.md, OPEN_QUESTIONS.md §2). Schema of record: DATABASE_DESIGN.md. Obsolete behavior (immediate fixed-lab earning, attribution holds, pending completion, W1/W2 timing ambiguity, "Σ allocations = payment", Doctor-only discount, Walk-In commission pool, `final_amount = salary_snapshot`, Assistant PENDING/INCOMPLETE) is **not** part of this design; it survives only in DESIGN_FREEZE.md's change history.

Labels: **CONFIRMED** = Owner rule enforced literally. **TECHNICAL** = engineering needed to implement a confirmed rule safely. **DEFERRED / OPEN** = only the Ortho total-price decrease (Q-ORT-3b, DEFERRED / OPEN FOR FUTURE VERSION; V1 rejects it with `ortho.total_decrease_not_supported`) and OPS-1 (backup key locations); nothing else is implemented for them.

---

## 1. What Finance owns

Charges and adjustments, payments, allocations, Patient Credit, Doctor entitlements and recognized Doctor earnings, Lab / Clinic distributions, fixed-lab expenses and revisions, payment corrections, consultation fees. Expenses and Payroll are separate modules that read Finance's ledger rows. Finance depends only on Patients, Configuration, Audit and Staff. **Finance never calls Clinical**: Clinical passes Doctor, percentage snapshot, lab model, price, discount and consultation fee to Finance as data when the Doctor finalizes a Visit.

```
Doctor enters Price (+ Consultation Fee); Doctor / Sales / Manager set Discount   Clinical, during the Visit
        │  Doctor FINALIZES the Visit  (one transaction)
        ▼
PatientCharge(s): original price, discount, final price      Finance, insert-only
        ▼
Payment (Sales/Manager) ─► allocations to Charges  and/or  ─► Patient Credit ledger
        │                   (explicit; remainder = credit)         │ later applied to a Charge
        ▼  every allocation (collection) triggers the Distribution Engine
doctor_entitlements (what the Doctor is owed on this collection)
        ├─ General lab: recognized at once (Doctor + Lab at payment-time % + Clinic remainder)
        └─ Fixed lab:   Lab FIRST ─► then Doctor (pending carried forward) ─► Clinic remainder
```

## 2. Core concepts and derived figures

**CONFIRMED** Charge and Payment are separate. Billed, Collected and Outstanding are never conflated. **TECHNICAL** No stored balances: every figure is a query over insert-only rows (Patient Credit included).

| Figure | Derivation |
|---|---|
| **Billed** | Σ `final_price` of charges + Σ `charge_adjustments.amount_delta` (by `charged_at`, clinic-local date) |
| **Collected (on charges)** | Σ `payment_allocations.amount` (net of reversals / refunds; period by `allocated_at`) |
| **Outstanding** | Billed − Collected, per charge and per patient |
| **Cash received** | Σ `payments.amount` (net; period by `paid_at`) — what actually came in or went out, by method (Cash, Visa, InstaPay) |
| **Patient Credit** | Σ `patient_credit_entries.amount` per patient (derived; never stored) |
| **Discounts** | Σ `discount_amount` |
| **Consultation fees collected** | Net allocations where `charge_kind = 'Consultation'`; own report line; never Doctor commission |
| **Doctor entitlement (earned claim)** | Σ `doctor_entitlements.amount` |
| **Doctor recognized share** | Σ `doctor_earnings.amount` |
| **Pending Doctor entitlement** | entitlement − recognized, per Doctor / case (fixed-lab Lab-First deferral only) |
| **Lab, Clinic Share** | Σ `revenue_distributions.lab_amount`, `.clinic_share` |

Global identity (test, G8): **Cash received = Collected on charges + Patient Credit**, for every patient and in total.

Worked example: 10,000 charge, 3,000 payment → Billed 10,000, Collected 3,000, Outstanding 7,000, Credit 0.

## 3. Charge lifecycle (Q-FIN-W1, Q-FIN-3)

**CONFIRMED**
- The **Doctor enters the Price** of the work they personally performed (including the Lab Expense for Fixed Prosthetics, Removable Prosthetics and Implant, §7) and the **Consultation Fee** where applicable, during the Visit. UI: `Price`, `Discount`, `Total` (calculated). **Total = Price − Discount.** Discount is a **fixed EGP amount**, never a percentage.
- Charges are established **when the Doctor finalizes the Visit**: one Treatment charge per priced work item and one Consultation charge when a consultation fee was entered. No Sales / Manager approval step.
- The charge records **Original Price, Discount, Final Price**. Consultation never produces Doctor commission.
- **Doctor, Sales / Reception and Manager** may each set / change the Discount of a pricing line **before the Visit is finalized** (the Doctor on their own line, Sales / Reception and Manager on any line whose price the Doctor has already entered). Every write records **amount, actor, role and timestamp** and is audited; the Charge takes the **current approved discount as a snapshot at Visit finalization**.
- Sales / Reception receives **no clinical work-item internals** to do this (§3 TECHNICAL, `PricingLineSalesDto`).
- **The initial Price, Consultation Fee, Fixed Lab amount and Ortho terms are entered by the Doctor only.** The Manager has **no initial-entry authority** for any of them; after establishment the Manager's role is limited to audited **corrections / revisions** (`Finance.Charges.Adjust` for ordinary charges only, `Finance.FixedLab.Revise`, `Orthodontics.Terms.Edit` for the Ortho Total, Deposit and Months).

**TECHNICAL**
- **Where the discount lives.** The pre-finalization discount is an insert-only entry in `clinical.pricing_discount_entries` (one logical row per pricing line, current = highest `entry_seq`; Clinical module, because it belongs to the Visit being worked on). Finance never calls Clinical: at finalization Clinical reads the current entries and passes the resulting price / discount / setter / role / time to Finance as data.
- `Finance.EstablishCharges` is an internal command called by Clinical inside the finalize transaction: it validates `price > 0`, `0 ≤ discount ≤ price`, copies Doctor, snapshot and lab model from the data it receives, inserts the charges (`charged_at` = finalize time; discount snapshot with `discount_set_by_user_id`, role and time) and writes the audit row (**A+**). Clinical and Finance rows commit together or not at all.
- **Concurrency.** The set-discount command locks the Visit row `FOR UPDATE` (as does finalization), rejects a Finalized Visit, and uses the client-supplied previous `entry_seq` as optimistic token (`UNIQUE` violation ⇒ 409). A Doctor lowering the price below an existing discount is refused.
- **Sales audience.** Sales / Reception reads and sets discount through the Finance-facing pricing contract `ISalesPricingLines`, which returns `PricingLineSalesDto` only: pricing-line id, patient display name, procedure name, treating Doctor name, Price, Discount, Total, current `entry_seq`. It never returns clinical notes, teeth / details, steps, measurements, prescriptions, lab, Doctor percentage or shares. A Doctor-priced line is visible to Sales only once the Doctor has entered its price and the Visit is not yet finalized.
- Before finalization **only the performing Doctor** (own line) enters or edits the **price** on the work item (`case_work_items.entered_price`, `Clinical.WorkItems.EnterPrice`, audited, guard G6); the Manager has no initial-price authority. After a charge exists, price and discount are frozen on the work item and any change is the Manager's audited correction below.
- **Fixed-lab precondition.** `Finance.EstablishCharges` refuses (422 `fixed_lab.initial_required`) a Treatment charge whose `lab_model = 'Fixed'` when the case has no `fixed_lab_revisions` revision 1; the Visit therefore cannot be finalized, nothing is established and nothing is distributed. A deferred constraint trigger (G7) repeats the check at commit. An unknown Fixed Lab is **never** treated as 0.
- **After establishment the Charge is not rewritten.** Doctor and Sales can no longer change the discount; a **Manager** discount / price correction goes through the audited correction model: an insert-only `charge_adjustments` row (`adjustment_kind` = `DiscountCorrection`, signed `amount_delta`, reason). Command validation: the adjusted Billed may not fall below the charge's net Collected (the Manager first reallocates or refunds through §9). A correction never changes earlier distributions: entitlements follow money collected, not the price. **This generic route never applies to an Orthodontic charge:** `Finance.AdjustCharge` knows whether the charge is an ortho charge (`ortho_cases.charge_id`) and refuses any `DiscountCorrection` / `PriceCorrection` on it with 422 `ortho.use_terms_edit`; the master Total of an Orthodontic case changes only through `Orthodontics.Terms.Edit` (§13), and guard G9 repeats the check in the database. Refund, reversal and payment correction (§9) remain available for ortho charges.
- Doctor commission and Sales commission are computed on **Collected money**, so a discount lowers the Billed figure and therefore the money that can ever be collected; neither is computed on the undiscounted original price, and there is no separate discount formula.
- A consultation charge is established at the same Visit finalization and may carry the same kind of discount (T11); `charge_kind = 'Consultation'`, no Doctor, snapshot or lab model; it never creates an entitlement or distribution.

## 4. Multiple Doctors in one case (Q-CLN-2)

**CONFIRMED** Every Doctor enters the price of the work **they** performed after **their** Visit; each Doctor earns only from their own priced work; there is no automatic revenue sharing between Doctors.

**TECHNICAL** Priced work is a `case_work_item` created by the performing Doctor in their own Visit (attached to that Visit's treatment record, with that Doctor's percentage snapshot). Continuing a case with another Doctor therefore creates new priced work for that Doctor; no flag exists (the old manual `separatelyPricedWork` concept is removed). A Visit with no price entered creates no work item and no charge. Orthodontics keeps a single work item and a single charge per ortho case: a later Visit on an ortho case cannot add a second price (price changes go through §13).

Example (root canal case): Visit 1 Dr. A, Price 3,000 → Charge A 3,000 owned by Dr. A. Visit 2 Dr. B, Price 2,000 → Charge B 2,000 owned by Dr. B. A payment allocated to Charge A produces Dr. A's entitlement at Dr. A's snapshot; to Charge B, Dr. B's. One fixed lab obligation per case regardless of the number of Doctors (§7).

## 5. Payments, allocation and Patient Credit (Q-PAY-3)

**CONFIRMED** Payments are recorded by Sales / Reception, or the Manager. Never a Doctor. Methods: Cash, Visa, InstaPay. Full or partial payments, many payments per charge, many charges per patient. **Patient Credit** is supported: money paid in excess of what a charge owes, and money paid before any charge exists, becomes credit that belongs to the patient, persists and can be applied to later charges.

**TECHNICAL** One command `RecordPayment` in one database transaction:

1. **Idempotency.** Client-generated `idempotency_key`: same key + same payload returns the original result; same key + different payload → 409. The unique index on `payments.idempotency_key` is the final guard.
2. Validate: patient exists, method active, amount positive.
3. Lock the target charge rows (`FOR UPDATE`, id order) and the patient's credit scope.
4. Insert the `payments` row (`kind = 'Receipt'`, `paid_at` = server time; **no backdating in V1**).
5. For each requested allocation insert a `payment_allocations` row (`allocation_source = 'Payment'`, `allocated_at` = server time). An allocation **cannot exceed the charge's current Outstanding** (an over-allocation request is cut at Outstanding by the UI; the API refuses it).
6. **The remainder** (`payment amount − Σ allocations`) is inserted as a `patient_credit_entries` row `FromPayment` (+). A payment with no allocation is entirely credit.
7. For each allocation to a treatment / ortho charge, run the **Distribution Engine** (§6–§7), producing entitlement, distribution and earning rows.
8. Insert the audit row. Any failure rolls everything back.

**Applying credit** (`ApplyPatientCredit`, Sales / Reception or Manager): move an amount from the patient's credit to a charge with Outstanding. The system consumes the oldest credit entries first (FIFO) and for each consumed slice inserts a `payment_allocations` row (`allocation_source = 'Credit'`, `payment_id` = the funding payment, `allocated_at` = now) and a `patient_credit_entries` row `AppliedToCharge` (−) linked to that allocation. The Distribution Engine then runs exactly as for a direct payment; the collection moment is the application moment (T1).

**Conservation rule (guard G8, deferred constraint trigger, checked at commit):** for every payment,
`payment.amount = Σ allocations(payment).amount + Σ credit_entries(payment).amount`.
It replaces the old "Σ allocations = payment" rule: a payment may legitimately be allocation plus credit. Credit balance can never go negative (the command locks the patient's credit entries; test).

Allocation is explicit; no automatic oldest-first rule exists for charges (the credit slice FIFO is only a bookkeeping order inside credit).

## 6. Doctor entitlement and General Lab (Q-FIN-W2)

**CONFIRMED** Collected = Doctor Share + Lab + Clinic Share. **Doctor entitlement = collected amount × the work's Doctor Percentage Snapshot** (not the Doctor's current rate); Lab is **not** removed before the Doctor percentage is applied. Consultation fees do not participate in Doctor commission.

Every allocation to a Treatment (or Ortho) charge inserts one `doctor_entitlements` row:

```
entitled = round_half_up(allocated × charge.doctor_percentage_snapshot / 100, 2)
```

**General lab (everything not Fixed; Orthodontics is General): recognized immediately.**
```
lab_amount    = round_half_up(allocated × general_lab_pct_at_allocation_time / 100, 2)
doctor_earning = entitled                      (recognized now, same moment)
clinic_share  = allocated − doctor_earning − lab_amount          (remainder, ≥ 0)
```
The percentage is the **latest `lab_percentage_history` row at the allocation moment** (`allocated_at`; for a direct payment that is the payment time). It is recorded on the distribution row. A later change in the General Lab percentage never changes an earlier distribution or any entitlement. Example: 100 collected, Doctor 40%, Lab 20% (current) → 40 + 20 + 40. If the Manager later sets Lab to 15%, the next 100 collected → 40 + 15 + 45; the first 100 stays 40 + 20 + 40.

**Clinic Share can never be negative.** TECHNICAL: the Manager cannot set a General Lab percentage such that Doctor % + Lab % > 100 for any Doctor percentage currently stored or snapshotted on a charge with Outstanding > 0 (validation, T6), and `revenue_distributions` carries a per-row CHECK in the engine's output (event-level identity below).

**Event identity (guard G8b, deferred constraint trigger).** Every collection / correction creates a `distribution_events` row; at commit, `Σ(lab_amount + clinic_share) over its revenue_distributions rows + Σ(amount) over its doctor_earnings rows = Σ(amount) of the allocations to treatment / ortho charges of that event`. Rounding is half-up; the Clinic Share is always the remainder.

## 7. Fixed lab: Lab-First collection order (Q-FIN-1, Q-FIN-6)

**CONFIRMED** For Implant, Fixed Prosthetics and Removable Prosthetics cases, collected money is used in this order: (1) the case's Fixed Lab obligation is covered first; (2) after it is fully covered the Doctor entitlement is paid / recognized; (3) if collections cannot yet pay the entitlement, the unpaid entitlement stays **pending**; (4) pending entitlement carries forward to later collections; (5) the Clinic receives only what remains after Fixed Lab and Doctor entitlement; (6) **Clinic Share never becomes negative.** The Doctor's entitlement (collected × snapshot, section 6) is preserved even while its recognition is deferred. A case has **one** fixed-lab obligation `L` however many Doctors, charges or payments it has; the **Doctor** enters the initial amount (once; `Finance.FixedLab.SetInitial`) — for Fixed and Removable Prosthetics the Doctor determines both the treatment price and the Lab Expense — and the **Manager** may later revise it (`Finance.FixedLab.Revise`, every revision audited with a reason; the Manager can never create the initial amount); the initial amount must exist **before the first Fixed-lab priced work is financially finalized or distributed**, and an unknown Fixed Lab is never treated as 0; a second Doctor can never create a duplicate obligation. Sales never sees Lab financial data.

### 7.1 Deterministic waterfall (the source of truth is a pure function of ledger inputs)

Inputs for one case: `L` = latest fixed-lab revision (**revision 1 must exist: the engine refuses to run for a Fixed-lab case without it, so `L` is never defaulted to 0**; an explicit Doctor-entered 0 is a real zero); the case's **effective allocations** in allocation-sequence order (`allocation_seq`), where effective amount = original allocation + all its reversal / refund rows; each allocation's snapshot `p`.

```
labPaid = 0 ; queue = []                       // FIFO of entitlements with unpaid remainder
for each allocation i in sequence order (effective amount a_i, snapshot p_i):
    lab_i    = min(a_i, max(L − labPaid, 0));  labPaid += lab_i
    rest_i   = a_i − lab_i
    ent_i    = round_half_up(a_i × p_i / 100, 2);  queue.append(ent_i)
    fund     = rest_i
    pay the queue FIFO (oldest entitlement first, then Doctor id) from fund   → doctor_recognized_i
    clinic_i = fund                          // only what is left after the queue is fully paid
    invariant: a_i = lab_i + doctor_recognized_i + clinic_i,   clinic_i ≥ 0
```

Because Clinic receives only the leftover after the queue is empty, **Clinic Share is non-negative by construction**; when charges cannot fund the entitlement (e.g. `L > (1 − p) × Billed`) the remainder stays pending forever — the entitlement is never lost and never taken out of the Clinic's negative.

Worked examples (verified by an executable model; part of the finance tests):

| Case | Collections | Result |
|---|---|---|
| **Owner example**: L = 3,000, 40 %, charge 10,000 | Payments 2,000 → 3,000 → 5,000 | P1: Lab 2,000, entitlement 800 **pending**, Clinic 0. P2: Lab 1,000, new entitlement 1,200 → pending 2,000, remaining 2,000 settles Doctor, Clinic 0. P3: Lab 0, entitlement 2,000, Doctor 2,000, Clinic 3,000. **Totals: Lab 3,000, Doctor 4,000, Clinic 3,000** |
| L = 2,000, 40 %, charge 10,000 | Payment 2,000 | Lab 2,000; Doctor recognized 0; entitlement 800 **pending**; Clinic 0 |
| same | then payment 3,000 | Lab 0 (covered); entitlement +1,200 → pending 2,000; recognized 2,000; Clinic 1,000. Totals on 5,000: Lab 2,000, Doctor 2,000 (= 40 %), Clinic 1,000 |
| L = 2,000, 40 %, charge 10,000 | Full payment 10,000 | Lab 2,000; Doctor 4,000; Clinic 4,000 |
| Two Doctors, one fixed lab 2,000 | Dr. A 6,000 charge at 40 %, Dr. B 4,000 charge at 30 %; payments 3,000 (A) then 2,000 (B) | A-payment: Lab 2,000, entitlement 1,200 pending, rest 1,000 → recognized 1,000 (A), 200 still pending; B-payment: Lab 0, entitlement 600; queue A 200 then B 600; recognized A 200, B 600 from 2,000; Clinic 1,200 |

### 7.2 Ledger representation (nothing is ever updated)

- `doctor_entitlements` — what each collection entitles its Doctor to (signed rows). Immutable.
- `doctor_earnings` — the **recognized** Doctor amounts (signed rows), each pointing at one entitlement; `recognized_at` = the moment of recognition. **Payroll consumes `doctor_earnings` only.** Pending = entitlement − earnings.
- `revenue_distributions` — Lab and Clinic components per allocation (signed rows).
- `distribution_events` — one row per collection, credit application, reversal / refund, or fixed-lab revision, tying the above together under the event identity of §6.

### 7.3 Normal collection
An ordinary new collection appends one allocation at the end of the sequence; the waterfall of earlier allocations is unchanged, so the engine inserts one entitlement row, the lab / clinic row for the new allocation, and recognition rows for whichever entitlements the new money pays (possibly older pending ones). The Doctor earning's `recognized_at` is the moment the funds arrive, so the earning belongs to **the payroll month in which the collection that funds it actually occurs** (§8).

### 7.4 Fixed lab revision and corrections (Q-FIN-6)
A **Manager revision** (the Doctor's initial entry always precedes the first charge and therefore every collection, so it needs no reconcile) or any reversal / refund / reallocation touching a fixed-lab case triggers **`Reconcile(case)`**: recompute the waterfall for the case, compare the target with the sums of the existing rows, and insert **only the delta rows** (signed, in a new `distribution_events` row of kind `FixedLabRevision` / `Correction`). Existing rows are never touched. Reconcile is idempotent (a second run produces no rows). Properties:
- The Doctor percentage and the entitlements never change because of a Fixed Lab revision; only the **timing of recognition** and the Lab / Clinic split change.
- If a revision lowers `L`, previously unrecognized entitlement is recognized (positive earning rows, `recognized_at` = now); if it raises `L`, money previously shown as recognized is re-owed to Lab (negative earning rows, `recognized_at` = now). These deltas land in the **current open** payroll month; **a finalized or paid payroll is never touched**.
- All delta rows are audited (**A+**), carrying the revision / correction reference.

### 7.5 General-lab corrections
For a general-lab allocation, a reversal / refund recomputes the target for the reduced effective amount using the allocation's **recorded** lab percentage, and the delta rows restore the identity. The General Lab percentage active today is never applied to old money.

## 8. Financial ownership, reassignment and inactive Doctors (Q-FIN-9)

**CONFIRMED** Financial ownership stays with the Doctor who **performed** the work: `patient_charges.performing_doctor_id` and the snapshot are final. Patient reassignment does not move historical ownership. If Dr. A becomes inactive, A still earns from collections on A's earlier work. The earning belongs to the **payroll month in which the collection actually occurs** (work in January, payment in March → March payroll).

**TECHNICAL** The old attribution machinery is **removed**: no `attribution_holds`, no `pending_doctor_attributions`, no `PatientReassigned` / `DoctorDeactivated` finance subscriptions. Reassignment and deactivation never delay or alter a payment, allocation, entitlement or earning. The payroll month is the clinic-local month of `doctor_earnings.recognized_at`. Doctor payroll drafts can be created for inactive Doctors that still have unpaid earnings. Payroll never depends on the Doctor's `is_active`.

## 9. Refunds, payment reversals and corrections (Q-PAY-1)

**CONFIRMED** **Manager only** may perform Refund, Payment Correction and Payment Reversal (`Finance.Payments.Correct`, assigned to the Manager only). History is never edited or deleted; every correction is new ledger entries, audited, and propagates to Doctor entitlement, Lab, Clinic Share, Patient Credit and Payroll.

**TECHNICAL** A correction is a `payment_corrections` header (kind, reason, Manager) plus new rows that reference it:

| Kind | Rows inserted | Effect |
|---|---|---|
| **Reversal** (a receipt that should not exist) | negative `payments` row (`kind='Reversal'`, `reverses_payment_id`); negative allocation rows (`reverses_allocation_id`) for every allocation of the original; negative credit entries for its remaining unapplied credit | Money leaves Collected and Cash received; entitlements and distributions are reconciled (below) |
| **Refund** (money returned to the patient; partial allowed) | negative `payments` row (`kind='Refund'`) with negative allocations (from a charge) and / or a negative credit entry (from credit) | Same, for the refunded part only |
| **Reallocation** (money applied to the wrong charge) | negative allocation (same payment) + positive allocation to the right charge, or allocation ↔ credit moves with matching credit entries | Cash received unchanged; Collected moves between charges; Doctor entitlement moves to the correct Doctor |
| **Replacement** (e.g. wrong amount / method) | Reversal + a new Receipt in the same correction | Net effect = corrected payment |

**Orthodontic charges.** Reversal, Refund, Reallocation and Replacement work on ortho payments exactly as on any other: they are new ledger rows, they write negative `installment_payments` rows for the applications of a reversed allocation (guard G5), and they never touch the Gross Total, Deposit, Months or the installment schedule. What is **not** available on an ortho charge is a price / discount correction (§3, §13).

Propagation: every negative allocation triggers the Distribution Engine, which for general-lab money inserts the exact negation (using the original allocation's recorded lab %, §7.5) and for fixed-lab cases runs `Reconcile(case)` (§7.4). Resulting negative Doctor earning rows carry `recognized_at` = now, so they reduce the **current open** Doctor payroll; **sealed payrolls are never changed**. A refund of unapplied credit has no distribution effect. Validation: refund ≤ refundable (net collected on the allocation or unapplied credit); credit never negative; a correction cannot be repeated beyond what remains.

## 10. Sales commission, Walk-In / Direct patients and inactive Sales (Q-FIN-2, Q-PAT-1, Q-EMP-1)

**CONFIRMED**
- Sales commission base = **actual collected money** after the Charge price / discount agreement (not billed value, not the undiscounted price, not uncollected value, not Clinic Share). The Manager must set each Sales employee's commission percentage **when creating the employee / account** (required) and **may change it later** (Manager only, audited). **A new rate applies to FUTURE collections only: the rate in force at the moment of each collection (`allocated_at`) applies to that collection, always.** Old collections, Draft payroll items and finalized payroll are never recalculated.
- **Walk-In / Direct patients** (patients who came without being brought or contacted by Sales) have `patient_source = WalkIn` and **no Original Sales Employee**. **No Sales employee receives commission from their collections: Sales commission = 0.** There is **no shared Walk-In commission pool, no equal split, and no implicit attribution to the Sales user who registered the patient** (registration is an administrative act that creates no commission ownership).
- For patients genuinely acquired by a Sales employee (Lead-sourced) the Original Sales Employee is preserved and earns commission on collected money **while eligible**.
- When a Sales employee becomes inactive they receive **no new commission after deactivation**; their historical Original Sales ownership is **not transferred** and no other employee receives commission for those collections (future commission expense for them is zero until the Owner changes this). This is intentionally different from Doctors, whose old work survives deactivation.
- On deactivation their **open Leads and open Follow-Ups** are redistributed equally among Active Sales employees (operational responsibility only; DATABASE_DESIGN.md §12).

**TECHNICAL**
- Base for a period = net `payment_allocations` (all charge kinds, T3) allocated in the calendar month (`allocated_at`, clinic-local), per patient. Refunds / reversals reduce the month in which they occur. Patient Credit that has not been allocated to a Charge is not commission base.
- **Commission-rate history (Q-COMM-1).** The rate is stored once, in the effective-dated, insert-only `staff.sales_commission_rate_history` (revision 1 at creation; every change appends a revision with `effective_from` = the database time of the save; never back-dated or future-dated). The rate of a collection is the revision with the greatest `effective_from ≤ allocated_at`. Each payroll commission item **snapshots** the percentage it used (`commission_percentage_applied`, with the allocation reference and the amount) and is unique per allocation, so nothing is ever recalculated with a newer rate. A reversal / refund row uses the rate of the allocation it reverses (so a 5 % collection reversed after a change to 7 % reverses exactly the 5 % commission).
- **Lead-sourced patients:** one item per net allocation, `commission = round_half_up(base × commission_percentage_applied / 100, 2)` for the patient's Original Sales Employee, counted only for allocations with `allocated_at` before that employee's `deactivated_at`. After deactivation nothing accrues to anyone and the Manager report lists the collected amount as "not accrued — inactive Sales" (T4).
- **Walk-In / Direct patients:** no commission item is produced; the Manager report shows Walk-In collections with Sales commission 0.
- Attendance (§11.2) never changes commission.

## 11. Payroll (Q-PAY-2, Q-PAY-ASST-2)

Payroll is **monthly** (calendar month) for Doctor, Sales and Assistant. Status: **Draft → Finalized → Paid**. Finalized payroll is sealed (G2). A payroll can be finalized only after its calendar month has ended in clinic-local time.

### 11.1 Doctor payroll
- Items = the Doctor's `doctor_earnings` (signed) whose `recognized_at` falls in the month and that belong to no payroll yet (`doctor_payroll_items.doctor_earning_id` unique). Because `recognized_at` is always the server time of the collection, no earning can appear in a month that is already finalized.
- `doctor_share_total` = Σ earnings; `revenue_total`, `lab_total`, `clinic_share_total` informational (from the related distributions); `final_amount = doctor_share_total + adjustments_total`. A negative total (net clawback) is shown as is; no automatic carry-forward is designed.
- Pending (not yet recognized) fixed-lab entitlement is never in a payroll. Unaffected by patient reassignment or Doctor deactivation.
- Historical payroll is never recalculated when percentages change (snapshots on charges).

### 11.2 Sales payroll
Net = Base Salary + Commission + Bonus − Deductions. Monthly snapshot of the base salary; commission items per net allocation of attributed (Lead-sourced) patients, each carrying the percentage it used (a month with a rate change shows both rates; the header has no single percentage); Walk-In / Direct collections contribute nothing. Sales sees only own figures and never another employee's compensation. Base salary of a partially worked month is not prorated by the system (T8): the Manager uses Bonus / Deduction. **Attendance (Present / Late / Absent) is operational data only and has no automatic salary or commission effect.**

### 11.3 Assistant payroll (Base Pay + Overtime)
**CONFIRMED** Assistants are a staff type with no account or role; the Manager maintains their staff record, salary, **attendance, overtime hourly rate and approved overtime**; payroll is Manager-only and monthly; Assistants are never deleted. No bonus, commission or automatic attendance deduction exists.

- **Base Pay.** For a normal full month, Base Pay = the salary snapshot. For a mid-month join, mid-month leave, mid-month salary change or another confirmed partial-month case, **the Manager manually enters the Base Pay** for that month (audited, with a reason code `JoinedMidMonth`, `LeftMidMonth`, `SalaryChangedInMonth`, `OtherPartialMonth`). **No automatic proration** and no division by 30, calendar days or working days.
- **Overtime.** The Manager sets each Assistant's overtime hourly rate and records / approves overtime hours per date. **Paid overtime is never inferred from check-in / check-out**; only explicitly approved hours count. `Overtime Amount = Approved Overtime Hours × Hourly Rate Snapshot` (rounded half-up to 2 decimals), where the snapshot is the rate at the moment the entry was created. A later rate change never recalculates earlier overtime.
- **Final Assistant Payroll Amount = Base Pay Amount + Overtime Amount**, with the components stored and reported separately. Finalized payroll components are immutable (G2).
- Examples (verified): salary 6,000, 5 overtime hours at 100 → overtime 500, final 6,500. Partial month: Manager-entered Base Pay 3,500, overtime 400 → final 3,900.
- **Data integrity.** Hours > 0 and ≤ 24, rate ≥ 0, `decimal` money; one live entry per Assistant per day; an overtime entry belongs to at most one payroll (unique) and only to a payroll of its own Assistant and month; a referenced entry cannot be voided; correction = audited void and re-entry; no DELETE of finalized history. **Sales have attendance only — no overtime.**
- **Attendance** (Assistants and Sales / Reception; Manager-maintained, one record per employee per clinic-local date, statuses Present / Late / Absent, optional check-in / check-out times, notes) is reportable to the Manager and affects **no payroll amount**.

Assistant payroll is an operating expense, never subtracted from revenue.

## 12. Clinic revenue, operating result and reports (Q-PAY-ASST-1, Q-FIN-DC)

**CONFIRMED** Clinic operating expenses reduce only the Clinic's result, never a Doctor's share. Estimated Clinic Operating Result is an operational metric, not accounting profit. **No Daily Closing**: the Manager Daily Report is live; nothing is sealed daily. **No printed receipt / invoice.**

```
Clinic Treatment Revenue Share = Σ revenue_distributions.clinic_share                (net; already after Lab and recognized Doctor)
Clinic Gross Revenue           = Clinic Treatment Revenue Share + Consultation fees collected + other confirmed Clinic-only revenue (none)
Estimated Clinic Operating Result = Clinic Gross Revenue
                                  − Sales compensation (Sales payroll months in range: salary + commission + bonus − deductions)
                                  − Assistant Payroll Expense (Base Pay + Overtime, Assistant payroll months in range)
                                  − Clinic Operating Expenses (expense_date in range, including Materials / Medical Supplies and Petty / Miscellaneous)
```
- **Doctor payouts are not subtracted again.** Doctor recognized share has already been removed from the Clinic's share by the distribution; Doctor payroll is reported separately for information only.
- **Pending Doctor entitlement** (fixed-lab deferral) is a memo line (an obligation against *future* collections), not a deduction.
- **Patient Credit** is a liability memo line (money held for patients), not revenue. Money received before a Charge exists (or in excess of it) **does not inflate treatment Billed, Doctor entitlement / share, Lab or Clinic treatment revenue** until it is later applied to a Charge (allocation source `Credit`, which then runs the Distribution Engine). *Cash received* reporting shows it separately (Cash received = Collected on charges + Patient Credit).
- **Doctor payroll is never subtracted again** and pending Fixed-Lab Doctor entitlement is never labelled Clinic profit; Clinic Share is never negative.
- **Assistant payroll is monthly**: Monthly reports include the monthly Assistant Payroll Expense with its two components **Base Pay** and **Overtime**; **Daily / Weekly reports and partial Custom ranges allocate no fraction of it**; a Custom range includes an Assistant (and Sales) payroll month only when the range fully contains that payroll month. Nothing is divided by 30, calendar days or working days, and no PENDING / INCOMPLETE label exists. Attendance and approved-overtime activity may be reported separately as operational data, never as salary proration. Only Finalized and Paid payrolls count (TECHNICAL); Draft payrolls are listed as not yet finalized.
- Layout: REVENUE (Billed, Discounts, Collected, Consultation Revenue, General Lab, Fixed Lab funding, Clinic Treatment Revenue Share, Clinic Gross Revenue); COMPENSATION / EXPENSES (Sales Salaries, Sales Commissions — Walk-In collections shown with commission 0, Assistant Base Salaries, Assistant Overtime, Assistant Payroll Expense, Clinic Operating Expenses, of which Materials / Petty Expenses); RESULT; MEMO (Doctor recognized share, Pending Fixed-Lab Doctor entitlement, Patient Credit, Cash received by method, Outstanding, refund / correction effects, "not accrued — inactive Sales", Lab total).
- Reporting reads Finance, Payroll and Expenses rows; it owns no business table.

## 13. Orthodontics in the ledger (Q-ORT-1…4)

**Vocabulary (one meaning each).** **Gross Ortho Total** = the Total Price the Doctor enters, later increased only by the Manager. **Discount** = the fixed-EGP pre-finalization discount snapshot. **Effective Ortho Total** = Gross − Discount = the **Ortho Charge Billed** (what the patient actually owes). The schedule is always balanced against the Effective Ortho Total, never against the undiscounted Gross.

**Module ownership.** **Orthodontics owns** the master **Gross Total, Deposit, Number of Months, the installment schedule** and every edit of them. **Finance owns** payment, allocation, refund, reversal and the correction ledger (`charge_adjustments` included). Finance never decides an Ortho Total: it only records the `OrthoTermsChange` adjustment that the Orthodontics command passes it, so the ortho charge's Billed always equals the Effective Ortho Total.

**CONFIRMED** The **Doctor** initially enters **Total Price (Gross), Deposit and Number of Months** (`Orthodontics.Terms.SetInitial`, Doctor only) **during the editable Visit**; the **Manager** may later edit / correct the terms (`Orthodontics.Terms.Edit`, audited, never the initial entry); **Sales cannot set or edit terms or amounts**. Remaining = Effective Ortho Total − Deposit. The initial installments are equal (the last absorbs rounding) and are generated **once, when the Visit is finalized and the Charge is established** (never earlier). Afterwards **only the Manager** edits installments (amount, due date, manual addition, safe retirement). Payments are recorded against the Ortho Charge; **Sales / Reception or Manager manually select** the installment(s) each payment is applied to (one→one, one→many, partial, early, future); no oldest-first; allocations are never regenerated by a terms edit. If the Gross Total **increases**, existing installments are preserved and a **new installment for the difference** is added (20,000 → 24,000 adds 4,000). If the Gross Total would **decrease**, V1 **does not support it** (`ortho.total_decrease_not_supported`; Q-ORT-3b is DEFERRED / OPEN FOR FUTURE VERSION and not a V1 blocker). If the **Deposit** or the **Number of Months** is edited after installments exist, **nothing is regenerated, added, removed or redistributed automatically**: the Manager manually adjusts the schedule, and the revised terms are valid only when the schedule is balanced and conforms to the Months again (OWNER DECISION, Q-ORT-5).

**TECHNICAL**
- **Initial flow.** (1) `SetInitial` stores revision 1 (Gross, Deposit, Months) and the work item's price = Gross; no charge, no installment. (2) Doctor / Sales / Manager may set the fixed-EGP Discount on the line (§3). (3) The Doctor finalizes: Clinical calls `Finance.EstablishCharges`, which snapshots the Discount and creates the Charge with `original_price` = Gross, `discount_amount` = Discount, `final_price` = Effective. (4) Finance publishes `ChargeEstablished` (in-process, same transaction; Orthodontics consumes Finance events, ADR-002). (5) The Orthodontics handler validates **0 ≤ Deposit ≤ Effective** (otherwise 422 `ortho.deposit_exceeds_effective_total`, the whole finalize fails and nothing is written; the Deposit is never silently altered), sets `ortho_cases.charge_id` and generates `Months` equal `Generated` installments from `Effective − Deposit`, last absorbing rounding. Guard G10 repeats the check at commit. Doctor percentage snapshot is fixed at the ortho work item and applied to every collection; the Distribution Engine runs at **allocation** time, not at installment-application time. Orthodontics uses the General lab model (placeholder, unchanged).
- **Discount example.** Gross 20,000, Discount 2,000, Effective 18,000, Deposit 5,000, Months 5: installment base 18,000 − 5,000 = 13,000 → 5 × 2,600; Deposit 5,000 + 13,000 = 18,000 = Billed; Charge original 20,000, discount 2,000, billed 18,000; there is no extra 2,000 outstanding. Gross 20,000, Discount 18,000, Deposit 5,000 is refused (Effective 2,000 < Deposit).
- Installment status (Upcoming, Due, Partially Paid, Paid, Overdue) is **derived** from `installment_payments` rows (the manual application), the current amount, the current due date and the clinic-local date. Applications are capped by G5 (per allocation and per installment) and are accepted only on an **active** installment; an application row needs an allocation to this ortho charge.
- Deposit paid = `min(deposit, ortho allocations − Σ installment applications)`.
- `ortho_installment_revisions` stores each installment's amount, due date and active flag as revisions (`Generated`, `ManagerAdded`, `PriceChange`, `ManagerEdit`, `ManagerRetire`); current = latest. `amount_already_paid` (Σ applications) is derived; an installment amount is never below it (G5(b), `ortho.installment_below_paid_amount`), never negative (0 is allowed), and an installment with payment history is never deleted, retired or re-dated.
- **Derived validity (no new status).** `schedule_difference = Effective − Deposit − Σ active current amounts`; `month_plan_difference = number_of_months − active regular installments` (regular = `Generated` or `ManagerAdded`; `PriceChange` rows never count); `requires_schedule_adjustment = (schedule_difference ≠ 0) OR (month_plan_difference ≠ 0)`. Valid means **Deposit + Σ active installments = Effective Ortho Total** and the month plan conforms. Payments change only Paid / Remaining / status, never this equation.
- **Gross increase by Δ (Manager `Terms.Edit`).** Example: initial Gross 20,000, Discount 2,000, Effective 18,000; Manager raises Gross to 24,000 (Δ = 4,000): inserts an `ortho_term_revisions` row, a `charge_adjustments` row (`OrthoTermsChange`, +4,000) and exactly one `PriceChange` installment of 4,000 (Billed 18,000 → 22,000). The Discount snapshot stays 2,000 and is **not** re-applied to Δ; existing installments, applications and allocations are untouched; `Deposit + Σ active = Billed` still holds (5,000 + 13,000 + 4,000 = 22,000). A `PriceChange` installment is explicitly distinguishable and is never used to fake a Months adjustment.
- **Gross decrease (20,000 → 18,000):** rejected before any write (`ortho.total_decrease_not_supported`). No negative installment, no automatic reduction, no `DiscountCorrection` / `PriceCorrection` workaround, no silent rewrite.
- **Deposit edit** (validated `0 ≤ Deposit ≤ Effective`; e.g. Effective 20,000, Deposit 5,000 → 7,000, required Σ 13,000; with the 2,000 Discount: Effective 18,000 → required Σ 11,000): only the term revision (+ audit) is written; `requires_schedule_adjustment = true` until the Manager's schedule batch makes the equation hold.
- **Months edit** (e.g. 10 → 12 or 10 → 8): rejected with `ortho.months_conflict_with_paid_history` only if the new Months is lower than the number of active regular installments that already have payment history (they can never be retired); otherwise only the revision row is written. `month_plan_difference` becomes +2 (two installments missing) or −2 (two surplus); nothing is generated, added or removed automatically. The Manager's schedule batch then **adds** future `ManagerAdded` installments (10 → 12) or **retires** unpaid ones (10 → 8) and rebalances the amounts; the batch is rejected unless the final state is balanced (`ortho.schedule_unbalanced`) **and** conforms to the Months (`ortho.month_plan_mismatch`).
- **Manager schedule batch** (one atomic, audited command; reason required): revise an amount (≥ already paid), revise the due date of an unpaid installment (previous value stays in the revision history), add a future installment (`ManagerAdded`: case, sequence number, due date ≥ today, amount, Manager, time, reason; no payment history), retire an unpaid installment (`is_active = false`, amount 0, never a delete; zero payment history required). Sales and Doctor cannot do any of it.
- **Protection of paid history:** an edit never reduces an installment below what was applied to it, never deletes, retires or re-dates it, never moves applications between installments because of a Deposit or Months change, and never changes `payment_allocations` / `installment_payments`.
- **Pre-finalization discount:** the Discount reduces the ortho Charge before it exists (Billed = Effective); the installments are generated from `Effective − Deposit`. After establishment no discount or price correction exists for the ortho charge (G9).
- Unused first-due-date input: default one calendar month after the charge date.

## 14. Doctor own-work financial view

**CONFIRMED** A Doctor sees price, discount, total, collected, remaining, own percentage, own Doctor Share, Clinic Share, relevant Lab and own history, for **own work only**. Never other Doctors' revenue or percentages, clinic profit, expenses, Sales payroll or management reports.

**TECHNICAL** The query starts from `patient_charges.performing_doctor_id = principal` (ownership never changes after reassignment), takes no Doctor id from the request and returns one item per work item. For fixed-lab work it also shows "Lab covered so far", **recognized** vs **pending** entitlement, and Clinic Share. Patient reassignment does not hide or move earlier work from the performing Doctor's own view of *money*; clinical access to the patient follows the assignment rules.

## 15. Concurrency and integrity summary

| Risk | Control |
|---|---|
| Double-submitted payment | Unique idempotency key + replay semantics |
| Two staff paying / applying credit on the same charge | Charge rows locked in id order; Outstanding cap checked in the same transaction |
| Credit applied twice | Credit entries locked; oldest-first slices; balance ≥ 0 check |
| Payment not summing | **G8** conservation (payment = allocations + credit) |
| Distribution not summing | **G8b** event identity at commit; remainder rule |
| Over-applying ortho money | Composite FK chain + G5 |
| Cross-patient allocation | Composite FKs |
| Editing history | No UPDATE/DELETE grants on ledger tables; corrections only add rows; audit row in the same transaction |
| Work-item price changing after the charge | G6 |
| Second fixed lab for a case | Unique `treatment_case_id` + G7 |
| Correction racing a payment on the same case | Reconcile runs under the case's charge-row locks |

## 16. Finance contract surface (`Finance.Contracts`)

| Kind | Operations |
|---|---|
| Commands | `EstablishCharges` (called by Clinical at finalize; the discount is passed as data), `SetPricingLineDiscount` (Clinical-hosted, audience DTOs for Doctor / Sales / Manager), `RecordPayment`, `ApplyPatientCredit`, `EnterInitialFixedLab` (Doctor only), `ReviseFixedLab` (Manager only), `CorrectPayment` / refund / reversal / reallocation (Manager), `AdjustCharge` (Manager; **refuses ortho charges**, `ortho.use_terms_edit`), `RecordOrthoTermsChange` (internal; callable only from the Orthodontics terms-edit command; +Δ `OrthoTermsChange` only) |
| Queries | Patient balance and credit (Sales, Manager), charge list, payment history, Doctor own-work view, ortho charge balance, entitlements and earnings for payroll, collected money by attribution for Sales payroll, report aggregates |
| Events | `PaymentRecorded`, `ChargeEstablished`, `CreditApplied`, `FixedLabRevised`, `PaymentCorrected`. `ChargeEstablished` is consumed by Orthodontics in the same transaction to validate the Deposit and generate the initial installment schedule once (§13); the events are otherwise consumed only for installment display and Reporting read models; Crm and Notifications consume none in V1. |
| Subscribed events | **None.** (The reassignment / deactivation subscriptions were removed with Q-FIN-9.) |

## 17. Mathematical consistency check (items A–V)

| | Check | Result |
|---|---|---|
| A | Treatment charge at Visit finalization | One charge per priced work item inside the finalize transaction; price, discount, final recorded. |
| B | Consultation charge at finalization | One charge when the fee is entered; no Doctor, no distribution. |
| C | Price / Discount / Total | Total = Price − Discount, fixed EGP, `0 ≤ discount ≤ price`, `final_price` generated. |
| D | General lab uses rate at payment time | Rate = latest history row at `allocated_at`; recorded per row; old rows never change. |
| E | Fixed lab Lab-First | §7.1 waterfall; model verified: lab = min(L, Σ collected). |
| F | Fixed lab can defer Doctor entitlement | Entitlement row exists immediately; earning rows lag; pending = entitlement − earnings ≥ 0. |
| G | Clinic Share never negative | Clinic = leftover after the queue; general lab guarded by Doctor % + Lab % ≤ 100; proven by 30,000 randomized replays (also under Fixed-Lab revision and refund of the last allocation). |
| H | Fixed-lab revision corrects with new rows | `Reconcile` emits delta rows only; idempotent; sealed payrolls untouched. |
| I | Reassignment does not change ownership | Ownership = `performing_doctor_id` on the charge; no handler exists. |
| J | Inactive Doctor keeps entitlement | Allocation, entitlement, earning and payroll draft are independent of `is_active`. |
| K | Earning in the collection's payroll month | `recognized_at` = collection (or recognition) server time; month of that date. |
| L | Patient Credit | Payment before charge, overpayment, later application; Cash received = Collected + Credit. |
| M | Sales base = collected money | Net allocations in the month; never billed or Clinic Share. |
| N | Walk-In / Direct | `patient_source = WalkIn`, Original Sales NULL ⇒ no commission item ⇒ Sales commission 0; no pool exists. |
| O | Inactive Sales receives no new commission | Allocation filter `allocated_at < deactivated_at`; not redirected. |
| P | Refunds / corrections | Manager only, new rows, propagate to entitlement / Lab / Clinic / payroll (current open month). |
| Q | Discount by Doctor / Sales / Manager | `pricing_discount_entries` (actor, role, time, `entry_seq`); Charge snapshot at finalize; after that only Manager `DiscountCorrection`; commission and Doctor share follow collected money. |
| R | Assistant payroll | Final = Base Pay + Overtime; Base = snapshot or Manager-entered; Overtime = hours × rate snapshot; case F 6,000 + 5 × 100 = 6,500; case G 3,500 + 400 = 3,900. |
| S | Patient Credit and revenue | Unapplied credit never enters Billed, Doctor share, Lab or Clinic revenue. |
| T | Fixed Lab unknown ≠ 0 | No Fixed-lab Treatment charge without revision 1 (G7 + `EstablishCharges`); the Owner case (price 10,000, L 3,000, 40 %, payments 2,000 / 3,000 / 5,000) re-verified: P1 Lab 2,000, entitlement 800 accrued, settlement 0, pending 800, Clinic 0; P2 Lab 1,000, new entitlement 1,200, pending 2,000, settlement 2,000, Clinic 0; P3 new entitlement 2,000, settlement 2,000, Clinic 3,000; totals Lab 3,000, Doctor 4,000, Clinic 3,000. |
| U | Sales commission rate change | Collection 1,000 at 5 % → 50; Manager changes the rate to 7 %; next collection 1,000 → 70; total 120; the first item stays 5 % / 50; Walk-In / Direct collections 0 with no pool; finalized payroll byte-identical. |
| V | Ortho Effective Total, balance and month plan | Only `Orthodontics.Terms.Edit` changes an Ortho Total (+Δ `OrthoTermsChange` + one Δ `PriceChange` installment); `Finance.Charges.Adjust` refuses ortho charges. **Discount case:** Gross 20,000, Discount 2,000 → Effective 18,000; Deposit 5,000, Months 5 → installments 13,000 (5 × 2,600); 5,000 + 13,000 = 18,000 = Billed; Charge original 20,000 / discount 2,000 / billed 18,000, no extra 2,000 outstanding. **Increase:** Gross 24,000 (Δ 4,000), Discount stays 2,000 → Effective 22,000; 5,000 + 13,000 + 4,000 = 22,000. **Decrease** 20,000 → 18,000 rejected. **Deposit 5,000 → 7,000** (Effective 18,000): required Σ 11,000, `schedule_difference` = −2,000 until the batch. **Months 10 → 12:** `month_plan_difference` = +2 until two `ManagerAdded` installments exist; 10 → 8: −2 until two unpaid ones are retired; impossible if fewer than the paid regular installments. **Refused:** Gross 20,000, Discount 18,000, Deposit 5,000 (Effective 2,000). Payments never change the equation. |

**Executable proof of the Owner cases (A–G).** A: 10,000 price, 40 %, General Lab 15 % at payment time, payment 2,000 → Doctor 800 + Lab 300 + Clinic 900 = 2,000. B: Fixed Lab 3,000, 40 %, payments 2,000 / 3,000 / 5,000 → P1 Lab 2,000, entitlement 800 pending, Clinic 0; P2 Lab 1,000, entitlements 800 + 1,200 = 2,000 settled from the remaining 2,000, Clinic 0; P3 Doctor 2,000, Clinic 3,000; **totals Lab 3,000 + Doctor 4,000 + Clinic 3,000 = 10,000**. C: Fixed Lab 3,000 with only 2,500 collected → Lab 2,500, Clinic 0, pending Doctor entitlement 1,000 visible, Clinic never negative. D: payment 5,000 vs Outstanding 3,000 → 3,000 allocated + 2,000 Patient Credit, no split on the credit. E: Walk-In payment → Sales commission 0. F: 6,000 + 5 × 100 = 6,500. G: 3,500 + 400 = 3,900.

Identity summary used by the tests: for every event `Σ(lab + clinic + doctor earning) = Σ allocation basis`; for every payment `amount = Σ allocations + Σ credit`; for every case `Σ lab = min(L, Σ effective collections)`; `0 ≤ Σ earnings ≤ Σ entitlements` per entitlement.

## 18. Finance test plan

| # | Test |
|---|---|
| 1 | 10,000 charge + 3,000 payment → Billed 10,000, Collected 3,000, Outstanding 7,000 |
| 2 | Charge(s) are created when the Doctor finalizes the Visit, never before; Total = Price − Discount; a percentage discount is rejected |
| 3 | Consultation fee entered → consultation charge on finalize, no earning / distribution, own report line |
| 4 | General lab (case A): price 10,000, Doctor 40 %, General Lab 15 %, payment 2,000 → Doctor 800, Lab 300, Clinic 900; property test over random amounts: event identity holds, Clinic ≥ 0 |
| 5 | General lab payment-time rate: Lab 10 % at case start, changed to 15 %, payment after → 15 %; earlier payments unchanged; entitlement unchanged |
| 6 | Fixed lab Lab-First: L 2,000, payment 2,000 → Lab 2,000, Doctor recognized 0, entitlement 800 pending, Clinic 0 |
| 7 | Fixed lab (case B): L 3,000, 40 %, payments 2,000 / 3,000 / 5,000 → Lab 3,000, Doctor 4,000, Clinic 3,000; entitlement pending after payment 1 |
| 8 | Pending entitlement carries forward and is paid FIFO from later collections; totals reach collected × % when funded |
| 9 | Fixed lab (case C): Clinic Share never negative (randomized sequences, incl. L larger than the funded amount); pending Doctor entitlement stays visible |
| 10 | Fixed-lab revision after distributions: delta rows only, old rows byte-identical, entitlements and percentages unchanged, reconcile idempotent, sealed payroll byte-identical |
| 11 | Reassignment A→B then payment on A's charge → Dr. A entitlement; no hold object exists in the schema |
| 12 | Inactive Doctor A still earns from old work; payroll draft can be created |
| 13 | Work in January, payment in March → earning in March payroll; a pending fixed-lab amount enters the payroll month of the collection that settles it |
| 14 | Patient Credit (case D): payment 5,000 against Outstanding 3,000 → 3,000 allocated + 2,000 `FromPayment` credit with no Doctor / Lab / Clinic split on the credit; payment before any charge → full credit; credit later applied → allocation `Credit`, distribution runs; Cash = Collected + Credit; credit never negative; unused credit not in Billed / revenue |
| 15 | Reversal / Refund / Reallocation / Replacement Manager-only (Doctor and Sales denied); negation rows; entitlement, Lab, Clinic, Doctor payroll (current month) adjusted; sealed payroll unchanged; `clinic_app` cannot UPDATE/DELETE ledger rows |
| 16 | Two Doctors, own priced work each, same case; each earns only from own charge; no sharing; one fixed lab per case |
| 17 | Manager discount correction creates a `DiscountCorrection` adjustment; Billed cannot fall below Collected; Doctor and Sales cannot change a discount once the Charge exists |
| 18 | Discount actors: Doctor (own line), Sales / Reception and Manager can set a fixed-EGP discount before finalization; each write stores amount, actor, role, time; concurrent writers → 409; Sales response is `PricingLineSalesDto` (no notes / teeth / lab / percentages); Charge snapshot = latest entry |
| 19 | Sales commission base = collected money after discount (not billed, not undiscounted price, not Clinic Share); percentage required at creation; historical snapshot preserved |
| 20 | Walk-In / Direct (case E): Original Sales NULL, Sales commission 0, no pool table, registering Sales user gets nothing |
| 21 | Inactive Sales gets no commission for allocations after deactivation; collections are not redirected; amount shown as "not accrued" |
| 22 | Monthly payroll: calendar month periods, finalize only after month end, Draft → Finalized → Paid; sealed columns frozen |
| 23 | Assistant (cases F, G): full month Base = snapshot; overtime 5 h × 100 = 500, final 6,500; partial month Manager Base 3,500 + overtime 400 = 3,900; no proration or /30; final = Base + Overtime; later rate change does not recalculate old overtime; finalized payroll immutable; Daily / Weekly / partial Custom reports carry no Assistant fraction, Monthly shows Base and Overtime separately; Attendance alone deducts nothing |
| 24 | Operating result = Clinic Treatment Revenue Share + consultation − Sales compensation − Assistant payroll (Base + Overtime) − expenses (incl. Materials / Petty); Doctor payroll absent; pending entitlement and credit are memo lines only |
| 25 | Ortho: Doctor terms are stored during the Visit and generate equal installments only at finalization; Manager edits an installment (the batch must end balanced); Sales / Doctor cannot; manual application (one→many, partial, early, future); status derived; total +4,000 adds a 4,000 installment and leaves others untouched; a total decrease is rejected with `ortho.total_decrease_not_supported` (V1; Q-ORT-3b deferred) |
| 26 | No Daily Closing, no receipt / invoice route |
| 27 | Doctor own-work endpoint never returns another Doctor's charge, percentage or total (canary scans) |
| 28 | Initial entries are the Doctor's: Manager cannot enter the initial price, Fixed Lab or Ortho terms; Manager corrections / revisions are audited and never rewrite old rows |
| 29 | Fixed Lab precondition: finalizing a Visit with Fixed-lab priced work and no initial Fixed Lab is refused; no charge, entitlement or distribution exists; an unknown Fixed Lab never appears as L = 0; the Owner case (Price 10,000, L 3,000, 40 %, payments 2,000 / 3,000 / 5,000) gives the P1–P3 rows of check T |
| 30 | Sales commission rate history: 5 % on 1,000 → 50; after the change to 7 %, 1,000 → 70; total 120; old item and finalized payroll unchanged; a reversal of the 5 % collection reverses 50; Walk-In / Direct 0 |
| 31 | **Ortho generic-adjustment loophole closed:** `AdjustCharge` with `DiscountCorrection` or `PriceCorrection` on an ortho charge → 422 `ortho.use_terms_edit`, nothing written; the same on a non-ortho charge succeeds; the database guard G9 rejects the row if the command check is bypassed; the ortho Billed changes only by `Orthodontics.Terms.Edit` (+Δ) |
| 32 | **Ortho Deposit edit, no regeneration:** Gross 20,000, no Discount, Deposit 5,000, 10 installments; Deposit → 7,000 inserts one `ortho_term_revisions` row only; installment rows, revisions, applications and allocations byte-identical; `schedule_difference = −2,000`, `requires_schedule_adjustment = true`; a Manager batch making Σ = 13,000 clears it; Deposit above the Effective Total → 422 `ortho.deposit_exceeds_effective_total`; no oldest-first and no automatic redistribution |
| 33 | **Ortho paid-history protection:** an installment cannot be set below its `amount_already_paid` (`ortho.installment_below_paid_amount`), cannot be retired, re-dated or deleted once it has any application (`ortho.installment_has_payment_history`), and no edit moves a payment between installments; a batch that ends unbalanced → 422 `ortho.schedule_unbalanced` and nothing written; an unpaid installment set to 0 is accepted, a negative amount never |
| 34 | **Ortho decrease and refunds:** Gross 20,000 → 18,000 → 422 `ortho.total_decrease_not_supported`, no row written, no negative installment; Gross 20,000 → 24,000 → one +4,000 installment, others untouched; reversal / refund / correction of an ortho payment still works through §9 and leaves Gross, Deposit, Months and installments unchanged; Sales / Manager manual application still works |
| 35 | **Ortho discount arithmetic:** Gross 20,000, Discount 2,000, Deposit 5,000, Months 5 → Effective 18,000; initial installments sum 13,000; Charge original 20,000, discount 2,000, billed 18,000; 5,000 + 13,000 = 18,000; no extra 2,000 outstanding; Deposit above Effective (Gross 20,000, Discount 18,000, Deposit 5,000) → finalize fails with `ortho.deposit_exceeds_effective_total`, nothing written, Deposit not altered; the final Discount snapshot is the one used at generation; generic `DiscountCorrection` on the ortho charge → `ortho.use_terms_edit` |
| 36 | **Ortho generation timing:** `SetInitial` stores revision 1 and creates no charge and no installment; no installment exists before Visit finalization; finalization generates `Months` equal installments once (last absorbs rounding); a second generation or a charge without the schedule cannot commit (G10) |
| 37 | **Ortho increase with a Discount:** Gross 20,000 → 24,000 with an established Discount of 2,000: Billed 18,000 → 22,000 (+4,000 only), exactly one `PriceChange` installment of 4,000, Discount still 2,000 and not re-applied, existing installments untouched, `Deposit + Σ active = Billed` (5,000 + 13,000 + 4,000 = 22,000) |
| 38 | **Ortho Months change has a real manual effect:** Months 10 → 12 inserts only a revision (no rows created automatically), `requiresScheduleAdjustment = true` and `monthPlanDifference = +2`; the Manager's batch adds two future `ManagerAdded` installments and rebalances → valid; Months 10 → 8 removes no row automatically (`monthPlanDifference = −2`); the Manager retires two unpaid installments and rebalances → valid; only unpaid installments can be retired; a `PriceChange` installment is not counted as a regular one and cannot be used to satisfy the month plan; a batch that leaves the month plan wrong → 422 `ortho.month_plan_mismatch`; Months lower than the number of active regular installments with payment history → 422 `ortho.months_conflict_with_paid_history`; a due-date revision keeps the previous date in the history; every payment application is unchanged by the batch |
