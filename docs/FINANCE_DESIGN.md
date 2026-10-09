# FINANCE_DESIGN.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL
**Source:** Step 9 as approved, including the `doctor_earnings` correction, Q-FIN-6 redefinition, Q-FIN-9 pending attribution, W2 reopened, and the Step 11 clarification that Q-ORT-2 is not resolved through the API. Schema of record: DATABASE_DESIGN.md.

Labels: **CONFIRMED** = user's rule enforced literally. **TECHNICAL** = engineering implementation. **OPEN** = undecided, not implemented, not defaulted.

---

## 1. What Finance owns

Charges, payments, allocations, Doctor earnings, revenue-distribution completions, fixed lab expenses and revisions, adjustments, attribution holds and consultation fees. Expenses and Payroll are separate modules that read Finance's ledger rows. Finance depends only on Patients, Configuration, Audit and Staff. **Finance never calls Clinical**: Clinical passes the Doctor, percentage snapshot and lab model to Finance as data.

```
Doctor enters price (work item)            Clinical
        │  data passed down, Finance never reads Clinical
        ▼
PatientCharge (price, discount, final)     Finance, insert-only
        ▼
Payment (Sales/Manager) ──► PaymentAllocation (payment → charge)
                                  │
                                  ├─► (attribution clear, no hold) DoctorEarning — immediately
                                  │        └─► RevenueDistribution (Lab + Clinic completion) or "pending"
                                  └─► (hold)    PendingDoctorAttribution — no earning, no completion
```

## 2. Core concepts and derived figures

**CONFIRMED** Charge and Payment are separate. Billed, Collected and Outstanding are never conflated. No mutable balance exists. **TECHNICAL** No stored totals; every figure is a query over immutable rows.

| Figure | Derivation |
|---|---|
| **Billed** | Σ `final_price` of charges + Σ `charge_adjustments.amount_delta` (by `charged_at`, clinic-local date) |
| **Collected** | Σ `payment_allocations.amount` (reversals reduce it; period by payment `paid_at`) |
| **Outstanding** | Billed − Collected, per charge and per patient |
| **By method** | Σ allocations joined to `payments.payment_method_code` (Cash, Visa, InstaPay) |
| **Discounts** | Σ `discount_amount` |
| **Consultation fees** | Charges/allocations where `charge_kind = 'Consultation'`; own report line; never enter an earning or distribution |
| **Doctor Revenue Share** | Σ `doctor_earnings.doctor_share` (includes fixed-lab cases from the moment of collection) |
| **Lab, Clinic Share** | Σ over `revenue_distributions` |
| **Pending completion** | Σ (allocated − doctor_share) over earnings with no distribution row (Lab-plus-Clinic portion not yet split) |
| **Pending Doctor Attribution** | Allocations with a `pending_doctor_attributions` row and no earning |

Worked example (test): a 10,000 EGP charge with a 3,000 EGP payment gives Billed 10,000, Collected 3,000, Outstanding 7,000.

## 3. Charge lifecycle

A charge is the established agreement: insert-only, carrying original price, discount, final price (generated), performing Doctor, Doctor percentage snapshot (copied from the work item), lab model.

**CONFIRMED** The Doctor enters the price. The Doctor does not apply discounts. Once established, the agreement is not silently changed; a correction is controlled and audited.

**OPEN** Who enters or approves the discount (Q-FIN-3). Who enters the ortho total, deposit and months (`Orthodontics.Terms.Set`).

**TECHNICAL**
- `Finance.EstablishCharge` is an internal command called by Clinical, Orthodontics or the consultation flow. It copies Doctor, snapshot and lab model from the data it receives, validates `0 ≤ discount ≤ price`, writes the audit row (**A+**).
- `Finance.Discount.Set` belongs to no role, so every charge today has `discount_amount = 0` and `discount_set_by_user_id = null`. Assigning a role later needs no schema change.
- Ortho charges cannot be created until the ortho-terms permission is assigned.
- One work item produces at most one treatment charge. A case may have several charges.

**OPEN W1 – when a charge is established** (timing, plus the source of a consultation charge). One internal call site; schema works with any answer. Options: (A) when the Doctor finalizes the visit containing the priced work item; (B) when Sales/Manager confirms the agreement; (C) another moment. **Not chosen.**

## 4. Recording a payment

**CONFIRMED** Payments are recorded by Sales/Reception, or the Manager when necessary. Never a Doctor. Methods: Cash, Visa, InstaPay. Full payment, partial payment, many payments per charge and many charges per patient. A payment relates to a charge, visit or case.

**TECHNICAL** One command, `RecordPayment`, in **one database transaction**:

1. **Idempotency.** Client-generated `idempotency_key`. Same key + same payload returns the original result; same key + different payload returns 409. The unique index on `payments.idempotency_key` is the final guard.
2. Validate: patient exists, method active, amount positive.
3. **Lock** target charge rows (`SELECT … FOR UPDATE`, id order, so concurrent payments cannot deadlock).
4. Insert the `payments` row (`received_by_user_id` = signed-in user).
5. Insert `payment_allocations`, one per target charge (composite FKs make cross-patient allocation impossible).
6. For each allocation to a treatment charge: **if the charge has an attribution hold**, insert a `pending_doctor_attributions` row (no earning, no completion). **Otherwise** insert the `doctor_earnings` row, then insert a `revenue_distributions` row **only if the completion is computable** (section 5). Consultation allocations insert neither.
7. For ortho charges, the allocation stays available for installment application (section 8).
8. Insert the audit row. Any failure rolls everything back.

**Allocation is explicit.** The person recording the payment chooses which charge(s) each part of the money belongs to. No automatic rule (e.g. oldest first) exists.

**Technical validation defaults (veto-able):** Σ allocations equals the payment amount in V1 (no unallocated credit); an allocation cannot push a charge's Collected above its Billed (overpayment policy undefined). Only these two validations change if overpayment/credit is later defined.

## 5. Doctor Share and revenue distribution

**CONFIRMED** Collected = Doctor Share + Lab Expense + Clinic Share, all components of the total. Doctor Share = collected amount × the work's Doctor Percentage Snapshot (not the Doctor's current rate). Lab is **not** removed before the Doctor percentage is applied. Clinic Share = Collected − Doctor Share − Lab. Consultation fees do not participate in Doctor commission. General lab is the Manager's percentage applied to collected.

### Doctor Share (all treatment work; recognized immediately when attribution is clear)

```
doctor_share = round_half_up(allocated × charge.doctor_percentage_snapshot / 100, 2)
```

Example: a 1,000 EGP allocation at a 40% snapshot gives a 400 EGP Doctor Share when collected, for general-lab and fixed-lab work alike. **No lab value, fixed or general, enters this formula.** A reversal inserts the exact negation, not a recomputation.

### Lab and Clinic completion

| Work type | Completion |
|---|---|
| **General lab** | `lab_amount = round_half_up(allocated × lab% / 100, 2)`; `clinic_share = allocated − doctor_share − lab_amount` (remainder). Computed only when the lab rate is unambiguous (W2). Example: 100 collected, 40% Doctor, 20% lab = 40 + 20 + 40. |
| **Fixed lab** (Implant, Fixed Prosthetics, Removable Prosthetics) | **Pending (Q-FIN-1, OPEN).** No row is generated. |
| **Consultation** | Not applicable; 100% Clinic-side. |

A pending completion is the **absence** of a row. A later `rule_version` fills it in; earlier rows are never changed.

### OPEN Q-FIN-1 – fixed lab across partial payments, charges and Doctors

There is **one fixed lab amount per case**, however many Doctors, work items, charges or payments it has (CONFIRMED). The Doctor enters the initial amount; the Manager may edit it, every edit audited. It is taken from the total, not removed first. How that one amount is split across partial payments, charges and Doctors is **not decided**. Q-FIN-1 affects only the Lab and Clinic completion of fixed-lab allocations, **never the Doctor Share**.

Available inputs for a later rule: the one case-level amount (current and historical), every charge in the case with its Doctor and percentage, every allocation, and lab already distributed on the case.

### OPEN W2 – which general-lab rate applies to a payment

Not defaulted. The calculator generates a general-lab completion **only when every candidate rule gives the same answer**: the lab rate in force at the charge's `charged_at` equals the rate at the payment's `paid_at` (no rate change between them). If a rate change lies between them, the completion stays pending and appears in Pending completion until W2 is answered; a new `rule_version` then fills it in. The rate used is recorded on every completion row. The reference point is the charge's creation time because Finance does not read Clinical; whether "case start" means the case's own start time is part of the W2 answer. Doctor Shares are never affected.

### Q-FIN-6 (redefined)

**CONFIRMED / resolved:** a Fixed Lab revision can **never** change a recognized Doctor Share (a 200 → 300 edit leaves a 400 EGP share at 400). `doctor_earnings` has no reference to the fixed lab tables.
**OPEN (redefined):** how a Fixed Lab revision affects previously completed Lab/Clinic allocations (leave earlier completions unchanged and apply going forward, create corrections, or another rule). Nothing exists to affect today because fixed-lab completions do not exist until Q-FIN-1.

### Q-FIN-9 – attribution after reassignment or departure (OPEN) and pending attribution

Financial ownership generally follows the Doctor's performed work and percentage snapshot (CONFIRMED); patient assignment and financial ownership are different. Future collections after patient reassignment or Doctor deactivation are subject to Q-FIN-9 and may remain Pending Doctor Attribution; who earns them is **OPEN** and is not silently answered. A payment is never blocked; Outstanding stays based on allocations; no Doctor Share is provisionally invented.

**TECHNICAL – holds.** Payment, allocation and Outstanding are never blocked. A hold is created only by these events, in the same transaction as the event, with charge rows locked `FOR UPDATE` in id order (same as `RecordPayment`):

| Event | Handler |
|---|---|
| `PatientReassigned` (published by Patients) | For the patient's treatment charges where `performing_doctor_id` = previous Doctor and Outstanding > 0, insert an `attribution_holds` row |
| `DoctorDeactivated` (published by Staff) | For that Doctor's treatment charges with Outstanding > 0, insert a hold |

A payment either commits before the event (normal earning) or after it (pending); it cannot slip between. Holds are never removed automatically (reassigning back is part of Q-FIN-9). For held allocations **no Doctor Share is computed, not even provisional.** A late payment on completed work by Dr. A after the patient moved to Dr. B is also held; the payment itself is never affected. A reversal of a pending allocation is also pending.

### Negative Clinic Share, rounding

If Doctor % + Lab % exceeded 100, the remainder is negative; the invariant still holds. No validation was added (not confirmed). Rounding is half-up with the Clinic Share as the remainder.

## 6. Multiple Doctors in one case

Each Doctor's separately priced work is a work item that becomes its own charge with its own Doctor and snapshot. A payment allocated to Charge A is split with Dr. A's percentage; to Charge B with Dr. B's. A case holds exactly **one** fixed lab obligation however many Doctors. Continuing a case adds a treatment record only; a new work item is created only when `separatelyPricedWork = true` (refused on ortho cases).

Example: a 10,000 EGP implant case: Work Item A → Charge 6,000 → Dr. A; Work Item B → Charge 4,000 → Dr. B; one fixed lab of 2,000 with its revisions. The 2,000 appears once.

## 7. Fixed lab handling

`Finance.EnterInitialFixedLab` creates the case's single `fixed_lab_expenses` row plus revision 1 (Doctor, own initial entry, once). `Finance.ReviseFixedLab` (Manager) inserts the next revision with a reason; every edit writes **A+**. G7 blocks a fixed-lab row on a General-lab case. A second Doctor on the case cannot create another fixed lab or edit the amount. No snapshot of the fixed amount is stored on charges or distributions.

## 8. Orthodontics in the ledger

**CONFIRMED** The case has total price, deposit and months. Remaining = total − deposit. Installments are stored individually; statuses Upcoming, Due, Partially Paid, Paid, Overdue. The Doctor percentage is fixed when the case begins and applies to every collection.

**TECHNICAL**
- The total is the ortho charge's `final_price`, stored once. Deposit and installment payments are payments allocated to the ortho charge, each producing an earning (and a completion) with the case's snapshot percentage. The split is computed at **allocation** time, never at installment-application time.
- `installment_payments` consumes allocations through the composite FK chain, capped by G5. The table exists but is **unused**.
- **OPEN:** how payments map to installments (early, several at once, more or less than due: Q-ORT-2), initial generated installments are equal, the final installment absorbing any rounding difference; whether individual amounts may later be customized is OPEN (Q-ORT-1), price change mid-treatment (Q-ORT-3), and who enters total/deposit/months (`Orthodontics.Terms.Set`). **Q-ORT-2 is not resolved through the API**: no endpoint writes `installment_payments`.
- Finance events are consumed only by Orthodontics; Crm and Notifications consume none in V1.

## 9. Corrections and refunds

**CONFIRMED** Financial history is never silently overwritten. The Manager can correct financial data through controlled workflows. Every correction is audited.
**OPEN (Q-PAY-1)** The refund and correction workflow (authorization, what it reverses, effect on shares already distributed).
**TECHNICAL – primitives only:** a negative payment (`reverses_payment_id`) with negative allocations and negated earning/completion; `charge_adjustments` with a signed delta. `Finance.Adjustments.Create` is assigned to the Manager but **no endpoint is exposed**: a reversal would flow into the next Doctor payroll as a negative amount, silently deciding that refunds claw back a Doctor's share.

## 10. Doctor own-work financial view

**CONFIRMED** A Doctor sees price, collected, remaining, Doctor percentage, Doctor Share, Clinic Share, relevant lab and own historical revenue, for own work only. Never other Doctors' revenue or percentages, clinic profit, expenses, Sales payroll or management reports.

**TECHNICAL** The query starts from `patient_charges.performing_doctor_id = principal`, takes no Doctor id from the request, returns one item per work item and no case/patient/clinic total.

| Field | Source | Fixed-lab work while Q-FIN-1 open |
|---|---|---|
| Price, Collected, Remaining | Doctor's own charge and allocations | Available |
| Doctor percentage | charge snapshot | Available |
| Doctor Share | `doctor_earnings` | Available immediately when attribution is clear; absent for held allocations (Q-FIN-9) |
| Historical revenue | own earnings by date | Available |
| Lab Expense, Clinic Share | `revenue_distributions` | Shown as **pending**; no value invented |
| Held (pending attribution) allocations | none | Neutral "Share pending" marker, no amount; Dr. B sees nothing |

## 11. Payroll integration

### Doctor payroll
**CONFIRMED** Each Doctor has their own percentage. Historical payroll is never recalculated with new percentages. Fields: period, Doctor, revenue, lab, percentage snapshot, payout, adjustments, final amount, status, paid date.
**TECHNICAL**
- Payroll aggregates **recognized Doctor Share** from `doctor_earnings`: the Doctor's earnings in the period that are in no payroll (`doctor_payroll_items.doctor_earning_id` unique).
- Fixed-lab cases contribute as soon as money is collected. Q-FIN-1, Q-FIN-6 and W2 never block a Doctor's percentage-based earning.
- Payout is derived from `doctor_share_total` and adjustments only. `lab_total` and `clinic_share_total` are informational completions existing at finalization.
- G2 freezes the sealed columns at finalization; later completions never change a sealed payroll.
- A pending-attribution allocation has no earning, so it cannot enter any payroll. Which period a late-recognized earning falls into is decided with Q-FIN-9.
- The payroll period is a parameter (not defined by the user).

### Sales payroll
**CONFIRMED** Net = Base + Commission + Bonus − Deductions. Commission percentage is set by the Manager. The original Sales employee of each patient is preserved. Monthly snapshots are kept. Sales sees only own figures.
**OPEN (Q-FIN-2)** The mathematical base of the commission percentage. `ISalesCommissionBase` has no implementation; the system does not guess a commission. Whether a Sales payroll may be finalized without it is part of that decision.

### Assistant payroll (v1.1)
**CONFIRMED** Assistants are a staff type with no account or application role; the Manager sets each Assistant's salary; salary history is preserved by snapshot (a later salary change never alters an earlier payroll); payroll has Assistant, period, salary snapshot, final amount, status, finalized-at and paid date; Manager-only; Assistants are never deleted. **Salary only**: bonus, deduction, overtime, attendance deduction, commission and incentive are not confirmed and are not designed.
**TECHNICAL (approved D2/D3)** `final_amount = salary_snapshot` (CHECK; the Manager cannot type an arbitrary amount). The database prevents overlapping periods for one Assistant. The snapshot is copied from `staff.assistants.current_salary` when the draft is created and sealed at finalization (G2). Status Draft → Finalized → Paid (placeholders, Q-PAY-2). A partial-period payroll or a payroll covering a mid-period salary change **cannot be finalized** until Q-PAY-ASST-2 is answered; the system does not prorate.
Assistant payroll is an **operating expense**. It is never subtracted from Revenue.

## 12. Clinic revenue and operating result

**CONFIRMED** Clinic operating expenses reduce the Clinic's result only, never a Doctor's share. The result is an operational metric, not formal accounting profit.

```
Clinic Treatment Revenue Share = Σ clinic_share over completed distributions
                               (= Treatment Collected − Doctor Revenue Share − Lab Expense)

Clinic Gross Revenue = Clinic Treatment Revenue Share
                     + Collected Consultation Fees
                     + any other confirmed Clinic-only revenue        (none confirmed today)

Estimated Clinic Operating Result = Clinic Gross Revenue
                                  − Sales Salary / Commission expense
                                  − Assistant Payroll Expense                    (v1.1)
                                  − Clinic Operating Expenses
                                  − any other confirmed Clinic-owned expenses   (none other than the lines above confirmed today)
```

- **Doctor Payroll does not appear in the result.** It is reported separately (adjustments, status, paid date). Payroll cash timing does not redefine Doctor Revenue Share.
- Consultation fees have no earning or distribution; the report adds them explicitly to Clinic Gross Revenue and also shows them on their own line.
- The result is displayed with four notices (the fourth is the Assistant allocation notice above, v1.1): *Pending completion* (Q-FIN-1, W2), *Pending Doctor Attribution* (Q-FIN-9), *Sales commission not configured* (Q-FIN-2). The two pending lines never overlap (completion = earnings without a completion row; attribution = allocations without an earning). Neither pending amount enters Clinic Gross Revenue until resolved.
- Sales compensation expense comes from Sales payroll figures (base, commission, bonus, deductions) by payroll period.
- **Assistant Payroll Expense (v1.1)** is **read from the Payroll module** (Reporting reads `assistant_payrolls`); it is **not** a Finance ledger entry or distribution, Finance owns no Assistant table, and no Finance ledger table is altered for it. It has no effect on Doctor Share or on any Doctor earning. It comes from `assistant_payrolls.final_amount`. It is its own report line, never folded into Other Expenses, and Clinic Gross Revenue is not reduced by it. The report shows the total for the period, the per-Assistant payroll history, and total payroll expense split into Doctor payroll (reporting only, not subtracted), Sales payroll and Assistant payroll.
- **Manager financial report layout (v1.1).** REVENUE: Clinic Treatment Revenue Share; Consultation Revenue; other confirmed Clinic-only revenue (none today); **Clinic Gross Revenue**. COMPENSATION / EXPENSES: Sales Salaries; Sales Commissions; **Assistant Salaries**; Clinic Operating Expenses. RESULT: Estimated Clinic Operating Result. Also available: total Assistant salary expense for a completed period, per-Assistant payroll history, and payroll reporting split Doctor / Sales / Assistant (Doctor payroll shown for reporting only, never subtracted again).
- **Period inclusion (D4, while Q-PAY-ASST-1 is OPEN).** If the report range fully contains an Assistant payroll's period, its `final_amount` is included normally. If the range only partially overlaps a payroll period, nothing is prorated and the Assistant Payroll Expense line shows **PENDING / NOT ALLOCATED FOR THIS REPORT RANGE** (with the unallocated payroll amount and list), and the result is labelled **Estimated Clinic Operating Result: INCOMPLETE — ASSISTANT SALARY ALLOCATION PENDING**. A known subtotal before the pending amount may be shown but is never presented as the final result. A range with no overlapping Assistant payroll shows 0 for that line with no notice. (Which payroll statuses count — Draft, Finalized, Paid — is part of Q-PAY-ASST-1; until answered, only Finalized and Paid payrolls count and Draft payrolls are listed as "not yet finalized".)
- The Manager report has a **Pending Doctor Attribution** line (total plus list: charge, patient, held Doctor, hold reason, amount) under `Finance.Management.Read`.
- **"Daily Closing"** appears in the first prompt's module list with no definition. Nothing is designed for it.

## 13. Concurrency and integrity summary

| Risk | Control |
|---|---|
| Double-submitted payment | Unique idempotency key + replay semantics |
| Two staff paying the same charge | Charge row locks in id order; cap check in same transaction |
| Over-applying ortho money | Composite FK chain + G5 |
| Cross-patient allocation | Composite FKs |
| Distribution not summing | Database CHECK + remainder rule |
| Distribution copies not matching its earning | Composite FK to `doctor_earnings (id, allocated_amount, doctor_share)` |
| Editing established history | No UPDATE/DELETE grants on ledger tables + audit row in same transaction |
| Work item price changing after the charge | G6 + RV for concurrent edits before it |
| Second fixed lab for a case | Unique `treatment_case_id` + G7 |
| Payment racing a reassignment/deactivation | Hold handler locks the same charge rows |

## 14. Finance contract surface (`Finance.Contracts`)

| Kind | Operations |
|---|---|
| Commands | `EstablishCharge`, `RecordPayment`, `EnterInitialFixedLab`, `ReviseFixedLab` (Manager), internal reversal/adjustment services (not exposed) |
| Queries | Patient balance (Sales, Manager), charge list, payment history, Doctor own-work view, ortho charge balance, earnings for payroll, report aggregates, pending-completion and pending-attribution lists |
| Events | `PaymentRecorded`, `ChargeEstablished`, `FixedLabRevised`. In V1 these are consumed **only by Orthodontics** (Crm and Notifications consume none). |
| Subscribed events | `PatientReassigned` (Patients), `DoctorDeactivated` (Staff): lower publishers, Finance subscribes |

## 15. Finance test plan

| # | Test |
|---|---|
| 1 | 10,000 charge + 3,000 payment → Billed 10,000, Collected 3,000, Outstanding 7,000 |
| 2 | 100 collected, 40% Doctor, 20% lab → 40 + 20 + 40 |
| 3 | Property test over random amounts/percentages: every earning sums exactly with its completion row, including rounding cases such as 333.33. Lab is not subtracted before the Doctor percentage. |
| 4 | A Doctor rate change after work started does not change any later distribution on that work; a new case uses the new rate |
| 5 | Two Doctors on one case: payments to each charge use each Doctor's percentage |
| 6 | Duplicate idempotency key creates no second payment; concurrent submissions create one |
| 7 | Concurrent payments to one charge cannot exceed Billed |
| 8 | Ortho: allocation cap, wrong-charge rejection, deposit and installment split use the case snapshot |
| 9 | Consultation fees produce no earning/distribution and appear on their own line |
| 10 | Fixed-lab allocation of 1,000 at 40% creates a 400 earning immediately and no completion row; appears in Collected and Pending completion (600); Doctor Share = 40% regardless of fixed lab amount |
| 11 | Second Doctor on a fixed-lab case cannot create another fixed lab; Manager edit creates a revision with an audit row |
| 12 | Reversal produces exactly negated rows; direct UPDATE/DELETE by `clinic_app` fails |
| 13 | Payroll draft: an earning appears in one payroll only; sealed columns reject changes after finalization while status changes succeed |
| 14 | A payroll finalized before a rate change is byte-identical afterwards |
| 15 | Doctor own-work endpoint never returns another Doctor's charge, percentage or total (canary scans) |
| 16 | Estimated result = Clinic Treatment Revenue Share + consultation collections − Sales compensation − **Assistant Payroll Expense** − Clinic expenses; Doctor Payroll absent; notices shown |
| 17 | A fixed-lab Doctor Share enters a Doctor payroll draft in the period collected; changing the fixed lab or deciding Q-FIN-1 leaves a sealed payroll byte-identical |
| 18 | Consultation collections included in Clinic Gross Revenue and excluded from Doctor Revenue Share |
| 19 | W2: no rate change → completion generated; rate change between charge and payment → pending, nothing guessed |
| 20 | Doctor own-work view shows Doctor Share for fixed-lab work and "pending" for lab/clinic; never another Doctor's figures |
| 21 | `clinic_app` cannot UPDATE/DELETE `doctor_earnings`; a distribution whose copies do not match its earning is rejected by the composite FK |
| 22 | A Q-FIN-6 revision leaves every `doctor_earnings` row byte-identical (400 stays 400 for 200 → 300) |
| 23 | No reassignment: payment creates a Dr. A earning immediately |
| 24 | Reassignment A → B with an outstanding charge, then a payment: payment/allocation/Outstanding recorded, pending row exists, no earning, no payroll entry, no A or B revenue |
| 25 | A charge fully paid before reassignment gets no hold; a payment before the event is a normal earning |
| 26 | Payment and reassignment racing on one charge yield either a normal earning (payment first) or pending (reassignment first), never a mix |
| 27 | Doctor deactivation holds the Doctor's outstanding charges; sealed payrolls and earlier earnings unchanged |
| 28 | Manager report shows Pending Doctor Attribution; Doctor view shows the marker with no amount; canary scans find no other Doctor's data |
| 29 *(v1.1)* | Assistant payroll snapshot: change the salary after a payroll is created → the finalized payroll is byte-identical and the next payroll uses the new salary; sealed columns reject change after finalization while status/paid_at succeed; `final_amount ≠ salary_snapshot` is rejected; overlapping periods for one Assistant are rejected by the database |
| 30 *(v1.1)* | Operating result with Assistant payroll: range fully containing the period → included as its own line, Revenue unchanged, Doctor Payroll still absent; partial overlap → line shows PENDING, result labelled INCOMPLETE, nothing prorated; no divide-by-30 anywhere |

## 16. Open items and what each blocks

| ID | Open item | Neutral seam | Blocked until answered |
|---|---|---|---|
| **Q-FIN-1** | Fixed lab across partial payments, charges, Doctors | Earning recorded; no completion for fixed-lab allocations; versioned calculator backfills | Lab and Clinic Share for implants/prostheses only. Doctor Share and payroll not blocked. |
| **W2** | Which general-lab rate applies to a running case | Completion only when candidate rules agree; rate recorded per row | Lab/Clinic completion for cases straddling a rate change |
| **Q-FIN-2** | Sales commission base | `ISalesCommissionBase`, no implementation | Commission amounts; Sales payroll finalization rule |
| **Q-FIN-3** | Discount role | `Finance.Discount.Set` assigned to nobody | Any non-zero discount |
| **Ortho terms** | Who enters total, deposit, months | `Orthodontics.Terms.Set` assigned to nobody; ortho columns nullable | Creating ortho charges |
| **Q-FIN-6** (redefined) | Revision effect on completed Lab/Clinic allocations | Insert-only completions and revisions | Nothing now. Never affects Doctor Share. |
| **Q-FIN-9** | Who earns collections after reassignment/departure | Holds + pending attributions | Doctor Share, payroll, revenue for held allocations only |
| **Q-PAY-ASST-1** | Assistant salary expense across Daily/Weekly/Custom periods | Fully contained payrolls included; partial overlap shows PENDING and INCOMPLETE; no proration | Accurate Assistant expense in non-monthly/custom reports |
| **Q-PAY-ASST-2** | Assistant partial-period / mid-period salary calculation | `final_amount = salary_snapshot`; no proration | Partial-period or mid-period-change Assistant payroll finalization |
| **Q-PAY-1** | Refund and correction workflow | Primitives only, no endpoint | Any refund or correction |
| **Q-ORT-1/2/3** | Customization of generated equal installments, payment mapping, price change | Per-installment rows; allocation-based application (unused) | Installment application behavior |
| **W1** | When a charge is established | One internal call site | Wiring Billed into the visit flow |
