# DESIGN FREEZE — Dental Clinic Management System

**Design Baseline Version: 1.1**
**Status: OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION** (Owner approval and freeze date: 2026-10-20)

> **DESIGN BASELINE v1.1 — OWNER APPROVED — FROZEN.** Baseline v1.0 was withdrawn and is not an approved baseline; v1.1 superseded it and was approved by the Owner on 2026-10-20. Implementation has not started, no application code has been written and the FRS has not been started.

## 0. Owner approval and freeze

| Item | Value |
|---|---|
| Design Baseline | **DESIGN BASELINE v1.1** (the version stays 1.1; there is no 1.2) |
| Status | **OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION** |
| Owner approval / freeze date | **2026-10-20** |
| Frozen package | Requirements + Architecture/Design: 14 Markdown documents plus 26 Mermaid source files (40 files) |
| Authority | The package is the **authoritative V1 design baseline**. Implementation must use it. |
| Decision register | OPEN_QUESTIONS.md (authoritative) and OWNER_REVIEW_NOTES.md; the decisions in them are **frozen V1 business rules**. |
| Final consistency gate | **FINAL CONSISTENCY GATE: PASSED** (§3f), with its qualification: a design / document / model review, not a proof of code, EF Core migrations or PostgreSQL runtime behavior |
| Deferred items (exactly two) | **Q-ORT-3b** (Ortho Total Price decrease; V1 rejects it with `ortho.total_decrease_not_supported`) and **OPS-1** (storage locations of the two backup recovery private keys; chosen before production go-live). Neither blocks V1 implementation. |
| Not started | FRS, implementation, application code, migrations |

The approval changed no business behavior, added no requirement, reopened no question and modified no database relationship. Any later business-rule change must **not** silently edit this baseline; it follows the controlled change rule of §7.

## 1. Scope of the freeze
Requirements + Architecture/Design (Steps 1–15), including every approved correction and every Owner decision of the Owner Review and of the final business changes. Source priority applied: (1) the latest explicit Owner decision, (2) the latest approved revision of a Step, (3) an earlier approved Step, (4) the original requirements, (5) recommendations.

Inventory and X-ray are out of scope everywhere. Every earlier question was answered by the Owner. Exactly two deferred items remain: **Q-ORT-3b** (decrease of an Orthodontic case's total price; DEFERRED / OPEN FOR FUTURE VERSION; V1 rejects it with `ortho.total_decrease_not_supported`, so it is not a V1 blocker) and **OPS-1** (physical storage locations of the two backup recovery keys; DEFERRED). See OPEN_QUESTIONS.md and OWNER_REVIEW_NOTES.md.

## 2. Document list
| # | File | Content |
|---|---|---|
| 1 | README.md | Package index and reading order |
| 2 | DESIGN_BASELINE.md | Source priority, overview, confirmed requirements, neutral seams |
| 3 | DATABASE_DESIGN.md | Schemas, tables, guards G1, G2, G4–G8b (G3 retired, G8c removed), indexes |
| 4 | SECURITY_DESIGN.md | Authentication, RBAC matrix, scoping, DB roles, 72 tests |
| 5 | FINANCE_DESIGN.md | Ledger, allocations, earnings, distributions, payroll, executable finance proof, 38 tests |
| 6 | API_ARCHITECTURE.md | API (Step 11), notifications and jobs (Step 10), A11 (58 tests) and B9 (27 tests) |
| 7 | DEPLOYMENT_ARCHITECTURE.md | Deployment, CI/CD, backup/restore/monitoring (Steps 12–14) |
| 8 | DIAGRAMS.md | Diagrams 1–21, ERDs 22a–22d, Diagram 23, plus the new flows (arrival, fixed lab, Walk-In, Assistant attendance and overtime, Sales expense, feedback dual path). **Mermaid source only.** |
| 9 | OPEN_QUESTIONS.md | Answered Owner decisions, Q-ORT-5 (answered), Q-ORT-3b (DEFERRED / OPEN FOR FUTURE VERSION), OPS-1 (DEFERRED), technical interpretations T1–T14 |
| 10 | OWNER_REVIEW_NOTES.md | Owner Decision Register |
| 11 | ARCHITECTURE_DECISIONS.md | ADR-001 to ADR-018 |
| 12 | IMPLEMENTATION_GUARDRAILS.md | Rules to read before any feature; 94-test traceability matrix plus FC-1…FC-20, OT-1…OT-22 and OC-1…OC-24; DO / DO NOT list |
| 13 | DESIGN_FREEZE.md | This file |
| 14 | diagrams/README.md | Index of the Mermaid sources (counted as the 14th Markdown file) |
| — | diagrams/ (26 `*.mmd`) | Mermaid SOURCE only. No rendered output exists in the package and none is promised. |

**File count (computed from the final package):** 13 top-level Markdown files + `diagrams/README.md` = **14 Markdown files**, plus **26 Mermaid sources** (`diagrams/*.mmd`) = **40 files**. No PNG, SVG, JPG, PDF or other rendered file, and no `rendered-diagrams/` folder.

## 3. Step 15 diagram review — STEP 15 APPROVED
Corrections applied (genuine inconsistencies only):
- Diagram 1: label "token endpoint or manual marking".
- Diagram 3: added missing dependency arrows (Finance→Staff etc.); replaced a Reporting→Payroll arrow with a note.
- Diagram 4: Manager linked to clinical write; valid "never" edge for Doctor→contact data.
- Diagram 11 / ERD 22d: "Ortho charge established" node; charge link optional.
- ERD 22a: removed Settings→AuditLog.
- ERD 22b: owner link nullable; Patient→FollowUps added; notification patient optional.
- ERD 22d: added Doctors→DoctorEarnings.

## 3c. FINAL OWNER REVIEW SYNCHRONIZATION
The whole package was synchronized with the Owner's final review. This was done before the Owner approval of 2026-10-20 (§0). FRS and implementation have not started. Every file was inspected: 14 Markdown files (13 top-level + `diagrams/README.md`) and 26 Mermaid sources (the older ZIP also held a misnamed `README (1).md`, a stale `OWNER_REVIEW_NOTES.md` and a `rendered-diagrams/` folder; these were replaced or removed).

**Decisions applied (by area).**
- **UI / scope:** Arabic RTL only (Q-UI-1); single branch (Q-BRN-1); no Daily Closing, receipts / invoices, consent, expense attachments, Inventory or X-ray.
- **Scheduling:** manual duration, overlap allowed, unused booked time released at Visit end (Q-APP-1/2); Sales and Manager Confirm / Cancel / No Show (Q-APP-3); arrival is an event recorded by Sales and the Doctor queue is derived; seven statuses, no Arrived / Waiting (Q-APP-4).
- **CRM:** follow-up statuses (Q-CRM-1); even redistribution of open Leads and Follow-Ups on Sales deactivation, unassigned if no Active Sales (Q-EMP-1); My Leads / All Leads.
- **Finance:** charges at Visit finalization (Q-FIN-W1); general lab at allocation-time rate (Q-FIN-W2); fixed-lab Lab-First waterfall with pending entitlement (Q-FIN-1) and delta-row revisions (Q-FIN-6); ownership stays with the performing Doctor (Q-FIN-9); Sales commission on collected money (Q-FIN-2); Patient Credit and Manager-only corrections (Q-PAY-1/3); monthly payroll (Q-PAY-2); Assistant payroll monthly only, Manager Base Pay for partial months (Q-PAY-ASST-1/2).
- **Orthodontics:** Doctor initial terms, Manager edits, manual installment application, difference installment on an increase (Q-ORT-1…4).
- **Clinical:** controlled Doctor correction of own finalized records (Q-CLN-1); each Doctor prices their own work (Q-CLN-2).
- **Security / Ops:** Manager-created accounts and Set Password (Q-ACC-1); public-token feedback without expiry (Q-FDB-1/2); DigitalOcean / Frankfurt, RPO 5 min / RTO 4 h, operations contacts (OPS-2…5); recovery-key custody by the Clinic Manager as a person, locations deferred (OPS-1).

**Preserved unchanged:** the structured Medical History intake (Sales collects / edits, Doctor read-only, never blocks booking, female-only Pregnant / Breastfeeding, no sex field); Assistants are a staff type with no login, account, role or application access; Inventory and X-ray out of scope.

## 3d. LATEST FINAL BUSINESS CHANGES
These Owner changes were applied after the Owner Review decisions and, where they differ, they govern:

1. **Sales Discount access.** Doctor (own line), Sales / Reception and Manager may set the fixed-EGP discount before finalization. Sales uses a pricing view (`PricingLineSalesDto`) with no clinical data; concurrent writers are resolved by an optimistic `entry_seq`; each write stores actor, role and time. Technical solution: insert-only `clinical.pricing_discount_entries` in Clinical, keeping "Finance never calls Clinical" intact (ADR-017).
2. **Assistant Attendance.** Maintained only by the Manager (`staff.attendance_records`); the Assistant has no application access and no self-check-in.
3. **Sales Attendance.** Same table and rules for Sales / Reception employees; attendance alone deducts no pay and has no overtime.
4. **Assistant Overtime.** Manager-recorded approved hours × the hourly-rate snapshot of the entry; void-only lifecycle; each entry belongs to at most one payroll; Final = Base Pay + Overtime.
5. **Manager-configured overtime rate.** `staff.assistants.overtime_hourly_rate`, changed only by the Manager and audited; a rate change never recalculates earlier entries or payroll items.
6. **Direct / Walk-In zero commission.** `patient_source = WalkIn`, no Original Sales Employee, zero Sales commission, no shared pool (the Walk-In pool, its tables and its rate default Q-PAT-1b are removed).
7. **Sales-created Materials / Petty Expenses.** Create-only (`Expenses.CreateOperational`) in categories flagged for Sales; the Manager reads and edits directly with audit.
8. **Referral Doctor Notes hidden from Sales.** `SalesReferralDto` has no `notes`; reason and destination remain visible; Sales still sees DOB.
9. **Sales-entered Patient Feedback.** A second path into `feedback_entries` (`source = SalesEntered`, actor stored) beside the non-expiring website token; only the Manager reads results.
10. **Fixed / Removable Prosthetic work: Doctor price + lab entry confirmation.** The Doctor sets the price and enters the initial fixed lab amount (`Finance.FixedLab.SetInitial`, revision 1, required before the first Fixed-lab priced work is financially finalized); the Manager may only revise it (`Finance.FixedLab.Revise`); the Lab-First waterfall and entitlement ledger are unchanged by a revision (proved in FINANCE_DESIGN.md §17).

**Also applied in this pass:** unassigned Leads / Follow-Ups when no Active Sales exists (nullable responsibility, Manager assignment, `Crm.Responsibility.Assign`); Assistant payroll as Base Pay + Overtime (replacing the `final-amount` entry); `patient_source` naming; `feedback_entries` as the only feedback result store; G6 extended to discount entries, G2 extended to overtime items; G8c removed; Orthodontic total decrease declared OPEN.

**Superseded rules removed from active text** (see OWNER_REVIEW_NOTES.md §C): Doctor-and-Manager-only discounts; the Walk-In monthly pool; refusing to deactivate the last Active Sales employee; Assistant final amount equal to the salary snapshot or entered freely, and no Assistant overtime / attendance; the work-item `entered_discount`; `acquisition_source`; feedback results on the request row; referral notes visible to Sales; rendered diagram files and any promise of them.

**Mathematical review (earlier pass; its conclusion is superseded by the Final Consistency Gate in §3f).** Details (FINANCE_DESIGN.md §17, executable replay): the Owner finance examples A–G reproduce exactly, and 30,000 random replays hold the invariants (collected = Doctor + Lab + Clinic; Clinic never negative; entitlements unchanged by a lab revision; revision idempotent). Items flagged for Owner awareness (not decided by design): Q-ORT-3b and OPS-1; technical interpretations in OPEN_QUESTIONS.md; scenario C (loss of the whole provider account) lies outside RPO 5 min / RTO 4 h unless off-site WAL archiving is added; a fixed-lab case whose Lab exceeds what its charges can fund leaves part of the Doctor entitlement pending (the Owner's Lab-First rule).

**Diagrams.** Mermaid source only. No rendered file exists or is promised; every `.mmd` was parse-validated with no output files generated.

**Documents changed:** all 13 top-level files plus all affected `diagrams/*.mmd` and `diagrams/README.md`.

## 3e. FINAL CORRECTION PASS (before the Owner approval of 2026-10-20)
This pass preceded the Owner approval of 2026-10-20 (§0); baseline v1.1. No Owner decision was reopened; no FRS and no implementation were started. This pass corrected implementability and permission drift only:

1. **Foreign keys audited for PostgreSQL** (DATABASE_DESIGN.md §24). Corrected: `pricing_discount_entries` (two direct FKs with an XOR CHECK, no `visit_id`), the overtime → attendance composite (declared `UNIQUE (id, assistant_id, attendance_date)`), `visit_corrections` (composite FK to `visits (id, doctor_id)`, so no Doctor can correct another Doctor's Visit), `feedback_entries` / `feedback_requests`, `prescriptions`, `treatment_records`, `installment_payments`, `revenue_distributions`, `case_work_items.treatment_record_id` (NOT NULL, unique) and several unspecified "FKs as named".
2. **Permission drift removed.** The Manager no longer has initial authority over the work price, consultation fee, Fixed Lab or Ortho terms; the Doctor enters, the Manager corrects / revises (`Finance.FixedLab.SetInitial` / `.Revise`; `Orthodontics.Terms.SetInitial` Doctor only / `.Edit` Manager only).
3. **Unknown Fixed Lab is never 0**: revision 1 is required before the first Fixed-lab Treatment charge (G7); the Owner waterfall case re-verified (FINANCE_DESIGN.md §17 T).
4. **Sales commission rate change (Q-COMM-1, ADR-018)**: future collections only; effective-dated history plus a per-item snapshot; 5 % on 1,000 = 50, after 7 % 1,000 = 70, total 120, Walk-In 0.
5. **Feedback integration**: Sales-entered Visit feedback links and submits the request; one accepted result per request; general feedback has no Visit and no request.
6. **Financial ERD / cardinality corrections**: a charge has 0..N entitlements; events are parents of entitlements, earnings and distributions; stale column names removed; column-name diff of every ERD against DATABASE_DESIGN.md.
7. **Expenses**: read-only allowed-categories lookup for Sales; creator preserved on Manager edits.
8. **File count** recomputed (14 Markdown + 26 Mermaid = 40).
9. **The premature "No SERIOUS ISSUE found" statements were removed** (here, in SECURITY_DESIGN.md §19 and DESIGN_BASELINE.md §10) and replaced by the Final Consistency Gate below.

## 3g. FINAL ORTHODONTICS FREEZE CORRECTION (before the Owner approval of 2026-10-20)
This pass preceded the Owner approval of 2026-10-20 (§0); baseline v1.1. No Owner decision was reopened, no unrelated module was touched, no FRS and no implementation were started. Only the Orthodontic consistency issues and the historical-note cleanup were corrected:

1. **Generic-adjustment loophole closed.** `Finance.Charges.Adjust` / `PriceCorrection` / `DiscountCorrection` can no longer change an established Ortho Total (422 `ortho.use_terms_edit`; database guard G9). All Ortho Total, Deposit and Months changes go through `Orthodontics.Terms.Edit` only. Refund, reversal and payment correction of ortho payments are unaffected.
2. **Total increase** (20,000 → 24,000) adds one positive 4,000 installment; existing installments, allocations and payment history are untouched.
3. **Total decrease** (20,000 → 18,000) is **not supported in V1**: rejected with `ortho.total_decrease_not_supported`; no negative installment, no automatic reduction, no workaround. Q-ORT-3b is **DEFERRED / OPEN FOR FUTURE VERSION** and not a V1 blocker.
4. **Deposit / Months edit after installments exist** (Q-ORT-5, ANSWERED — OWNER DECISION): no automatic regeneration, addition, removal or redistribution; the Manager manually edits the schedule (§3h extends this with manual add / retire); a derived `requires_schedule_adjustment` flag (no new status) shows until Deposit + Σ active installments = Effective Ortho Total (Gross − Discount) and the month plan conforms (see §3h).
5. **Paid history is immutable**: an installment never goes below its paid amount (G5(b)), is never deleted, and no payment moves between installments. Errors added: `ortho.schedule_unbalanced`, `ortho.installment_below_paid_amount`, `ortho.use_terms_edit` (`ortho.installment_sum_mismatch` is retired).
6. **Historical freeze notes cleaned.** The stale conflict notes C1 (an open "Q-ORT-TERMS") and C2 (referral-reason / DOB visibility described as undecided) are marked SUPERSEDED HISTORICAL NOTE in §4. The active design is: the **Doctor** sets the initial Ortho terms (Q-ORT-4, answered); **Sales may see DOB**; **Sales may see Referral Reason / Type / Destination**; **Sales may not see the referring Doctor's notes** (Q-PRV-1, answered).
7. Tests added: SEC 70–71, FIN 31–34 (+ §17 V), API A11 53–56, DB tests (13)–(19), OT-1…OT-22 (IMPLEMENTATION_GUARDRAILS.md §13c).

## 3h. FINAL FREEZE BLOCKER CORRECTION: Ortho Effective Total and Number of Months (before the Owner approval of 2026-10-20)
A final independent review found that the earlier claim "FINAL CONSISTENCY GATE: PASSED" was **premature**: two Orthodontic inconsistencies remained. The claim was withdrawn, the two blockers were corrected, and the gate was re-run (§3f). This pass preceded the Owner approval of 2026-10-20 (§0); baseline v1.1. No Owner decision was reopened, no unrelated module was touched, no FRS and no implementation were started.

**Blocker 1: Ortho Discount versus Total.** A pre-finalization Discount applies to the Ortho line, so `Deposit + Σ installments = Gross Total` contradicted the Charge (Billed = Original − Discount). Corrected: **Effective Ortho Total = Gross Ortho Total − Discount = Ortho Charge Billed**; the schedule balances against it (Gross 20,000, Discount 2,000, Deposit 5,000, Months 5 → Effective 18,000; installments 13,000; 5,000 + 13,000 = 18,000; no extra 2,000 outstanding). `0 ≤ Deposit ≤ Effective` is validated at finalization (`ortho.deposit_exceeds_effective_total`; never a silent change). A Gross increase (20,000 → 24,000 with the 2,000 Discount) raises Billed by 4,000 (18,000 → 22,000) and adds one `PriceChange` installment of 4,000; the Discount is not re-applied. Generic `DiscountCorrection` stays refused (`ortho.use_terms_edit`). **Timing** is standardized: `SetInitial` stores the terms during the Visit; the Charge and the equal initial installments are created **only at Visit finalization**, once (guard G10).

**Blocker 2: Number of Months had no implementable effect.** The API could only edit existing amounts, so 10 → 12 or 10 → 8 months could not change the plan. Corrected, keeping the Owner's **manual, not automatic** decision: the Manager's atomic schedule batch (`PUT /orthodontic-cases/{id}/installments`) can revise amounts, revise due dates of unpaid installments, **add** `ManagerAdded` future installments and **retire** unpaid installments (a revision, never a delete); `ortho_installment_amounts` became `ortho_installment_revisions` (amount, due date, active flag). `requires_schedule_adjustment` now covers both the amount balance and the month plan (active regular installments = Number of Months; `PriceChange` rows never count). An impossible reduction is rejected (`ortho.months_conflict_with_paid_history`). Paid history stays immutable (no deletion, retirement, re-dating or reduction below the paid amount).

New errors: `ortho.deposit_exceeds_effective_total`, `ortho.month_plan_mismatch`, `ortho.installment_has_payment_history`, `ortho.months_conflict_with_paid_history`, `ortho.charge_not_established`. New guards: G5(c)–(d), G9(c)–(d), G10. New tests: FIN 35–38, API A11 57–58, SEC 72, DB (20)–(27), OC-1…OC-24 (IMPLEMENTATION_GUARDRAILS.md §13d). No foreign key was added or changed.

## 3f. FINAL CONSISTENCY GATE (re-run after all corrections of §3e, §3g and §3h)
Checks performed on the final package, and their results:

| # | Check | Result |
|---|---|---|
| 1 | **All PostgreSQL FKs.** Every composite FK in DATABASE_DESIGN.md §24 (28 composites + 3 simple discriminated FKs) has a parent PRIMARY KEY / declared UNIQUE on exactly the referenced columns and its child columns exist (scripted match of the register against the table rows); this correction added **no** FK (the new `ortho_installment_revisions` table has one simple FK to `ortho_installments(id)` and one to `identity.users`); the only register change is the wording of one NULL pattern | 28 / 28 matched, 0 problems |
| 2 | Column-name diff of the four ERDs against the key-column cells of DATABASE_DESIGN.md (including the new `ORTHO_INSTALLMENTS`, `ORTHO_INSTALLMENT_REVISIONS` and `gross_total_price`); a deliberate wrong column was detected, then restored | 0 mismatches |
| 3 | ERD cardinalities versus DATABASE_DESIGN.md §19 (charge → 0..N entitlements, event → child rows, work item ↔ treatment record, request ↔ result, rate history, commission item per allocation, ortho case → charge set at finalization, installment → revisions) | consistent |
| 4 | **Mermaid / document consistency.** All 26 `.mmd` sources parse with the Mermaid parser; every block in DIAGRAMS.md is byte-identical to its `.mmd`; diagram 11 shows the Doctor → Discount → finalize → Effective Total → Charge → one-time schedule sequence and the Manager Deposit / Months / increase / decrease paths | pass |
| 5 | Executable finance proof (FINANCE_DESIGN.md §17 A–V): Owner cases A–G, Fixed Lab P1–P3 (Lab 2,000 / 1,000 / 0; entitlement 800 / 1,200 / 2,000; pending before settlement 800 / 2,000 / 2,000; settlement 0 / 2,000 / 2,000; Clinic 0 / 0 / 3,000; totals Lab 3,000, Doctor 4,000, Clinic 3,000), commission-rate case (50 + 70 = 120, reversal −50, Walk-In 0), 30,000 random replays | pass |
| 6 | **Non-zero Ortho Discount arithmetic and Effective Total balance.** Executable Ortho schedule model: Gross 20,000, Discount 2,000, Deposit 5,000, Months 5 → Billed 18,000, installments 13,000 (5 × 2,600), 5,000 + 13,000 = 18,000, no extra 2,000 outstanding; Gross 24,000 → Billed 22,000, one +4,000 `PriceChange`, 5,000 + 13,000 + 4,000 = 22,000; Discount 18,000 with Deposit 5,000 refused; decrease refused; Deposit edit leaves `schedule_difference` until the batch | pass |
| 7 | **Initial schedule generation timing.** No installment and no charge before Visit finalization; the schedule is generated once, in the finalize transaction, from `Effective − Deposit` (model and G10 specification agree; all sections state the same timing) | pass |
| 8 | **Number-of-Months change has an implementable manual effect.** Months 10 → 12 creates nothing automatically (`month_plan_difference` +2); the Manager's batch adds two `ManagerAdded` installments and rebalances → valid; 10 → 8 removes nothing automatically (−2); the batch retires two unpaid installments → valid; a `PriceChange` installment never satisfies the month plan; a Months value below the paid regular installments is rejected | pass |
| 9 | **Manual added installment and safe retirement.** `ManagerAdded` installments start unpaid with Manager, time and reason; retirement is a revision (inactive, amount 0), only for installments with no payment history, never a delete, never re-activated | pass |
| 10 | **Paid-history protection.** No amount below `amount_already_paid`; no retirement, re-dating or deletion with payment history; applications identical before and after every batch; due-date revisions keep the previous date; 15,000 randomized sequences kept every invariant | pass |
| 11 | Stale-phrase sweeps over all Markdown and Mermaid files (earlier permission, commission, feedback, Fixed Lab and FK phrases; and now: balancing against the undiscounted Gross, installments generated before finalization, revision 1 "at finalization", no create-installment route, `ortho_installment_amounts`, `ortho.installment_sum_mismatch`, an open Q-ORT-4, visibility "not locked") | only intentional mentions remain, each marked as removed / superseded / forbidden |
| 12 | Permission matrix (SECURITY §4), API catalog (A8), FINANCE, DATABASE actor rules, Mermaid 04 / 06 / 11 / 14 / 15 / 17 / 18 / 20, tests (SEC 67–72, API 46–58, FIN 28–38, FC-1…FC-20, OT-1…OT-22, OC-1…OC-24) all state the same actors and the same Ortho rules | consistent |
| 13 | File count recomputed: 14 Markdown + 26 Mermaid = 40; no PNG / SVG / JPG / PDF / rendered folder | pass |
| 14 | Version 1.1 on every Markdown file; one synchronized status (OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION, 2026-10-20); no document presents a proposed-freeze status as current | pass |

**FINAL CONSISTENCY GATE: PASSED / No blocking design inconsistency identified in the current v1.1 design package.** The earlier PASSED claim was withdrawn (§3h) and is re-stated here only after the nine named checks above (rows 6 to 10 and 1, 4) were actually re-run on the corrected package. This is a document and model review with scripted cross-checks. **It does not prove that an implementation will work**: no migration has been run, no code exists, and the first migration test (DATABASE_DESIGN.md Final-correction test 10) is where PostgreSQL itself confirms the foreign keys. The two remaining deferred items are: **Q-ORT-3b** (the Ortho Total decrease is not supported in V1 and is explicitly rejected; DEFERRED / OPEN FOR FUTURE VERSION) and **OPS-1** (DEFERRED). The Owner approved Design Baseline v1.1 on 2026-10-20 (§0); the approval does not raise the verification level above this document / model review. Application code, EF Core migrations and PostgreSQL runtime behavior will be verified during implementation through migrations, integration tests and acceptance tests.

## 3a. Historical change log (superseded details removed)
Three controlled v1.1 changes preceded the Owner Review and remain in force in their current form: **Assistant staff and payroll** (a staff type, no account / role; payroll Manager-only and monthly; now Base Pay + Overtime, §3d); **Scheduling correction** (Arrived / Waiting / Check-In removed; seven statuses; arrival later defined as an event, Q-APP-4); **Structured Patient Medical History Intake** (Patients module, 0..1 per patient). Superseded intermediate rules (pending-attribution handling, "PENDING / INCOMPLETE" Assistant reporting, final amount equal to the snapshot) are recorded in OWNER_REVIEW_NOTES.md §C and are not active anywhere.

## 4. Cross-document consistency report (history)
Every note below is a **SUPERSEDED HISTORICAL NOTE**: it records a conflict that was found and resolved in an earlier pass and is **not** a statement about the current design. The current state is §3f.

SUPERSEDED HISTORICAL NOTE — C1 (resolved; not a current fact)
- DOCUMENT: DIAGRAMS.md vs OPEN_QUESTIONS.md
- WHAT HAPPENED: an early draft used two IDs (Q-ORT-4 and Q-ORT-TERMS) for the same question.
- CURRENT STATUS: **Q-ORT-4 is ANSWERED — OWNER DECISION**, not open: the **Doctor** sets the initial Ortho terms; the Manager edits them later; Sales cannot. One ID is used everywhere.

SUPERSEDED HISTORICAL NOTE — C2 (resolved; not a current fact)
- DOCUMENT: DIAGRAMS.md vs OPEN_QUESTIONS.md
- WHAT HAPPENED: an early draft treated referral-reason and DOB visibility as a configurable, undecided item (Q-PRV-1).
- CURRENT STATUS: **answered by the Owner and locked.** Sales **may see DOB**; Sales **may see Referral Reason / Type / Destination**; Sales **may not see the referring Doctor's notes**.

SUPERSEDED HISTORICAL NOTE — C3 (resolved reference error)
- DOCUMENT: DATABASE_DESIGN.md
- SECTION: Deliberate copies cross-reference
- CONFLICT: Pointed to section 12; list is in section 20.
- LATEST APPROVED RULE: n/a (reference error).
- MINIMUM CORRECTION (applied): reference changed to section 20.

Note: wiring points W1/W2 appear as Q-FIN-W1/Q-FIN-W2 in OPEN_QUESTIONS.md; same items.

Final greps found no obsolete structures (SetDiscount, age_at_registration, revenue_distribution_id, doctor_entered_*, Manager-delivered reset links); Inventory, X-ray and `pg_read_all_data` appear only as negations.

SUPERSEDED HISTORICAL NOTE — C4 (resolved; final Owner review)
- DOCUMENT: older ZIP vs. the Owner's final decisions
- SECTION: Whole package
- CONFLICT: The ZIP still showed Owner Review questions as OPEN, kept the Walk-In pool, Doctor-only discounts, Assistant final amount, rendered diagrams, a misnamed `README (1).md` and a Medical-History-only `OWNER_REVIEW_NOTES.md`.
- LATEST APPROVED RULE: Sections 0, 3, 58, 59 and 78 of the final Owner prompt.
- MINIMUM CORRECTION (applied): every document synchronized; renders removed; register and README rewritten.

SUPERSEDED HISTORICAL NOTE — C5 (resolved; final Owner review)
- DOCUMENT: ARCHITECTURE_DECISIONS.md ADR-002 vs. the Sales discount decision
- SECTION: Finance never calls Clinical
- CONFLICT: Sales must set a discount before finalization, but Finance may not read Clinical and Sales may not read clinical data.
- LATEST APPROVED RULE: Sales may set discounts; Finance never calls Clinical; Sales sees no clinical data.
- MINIMUM CORRECTION (applied): discount entries live in Clinical and are exposed to Sales only through a narrow pricing contract (ADR-017); Finance receives the snapshot at finalization.

SUPERSEDED HISTORICAL NOTE — C6 (resolved; final Owner review)
- DOCUMENT: DATABASE_DESIGN.md / API_ARCHITECTURE.md vs. Q-EMP-1
- SECTION: Sales deactivation
- CONFLICT: Deactivation was refused when no Active Sales existed, but an employee cannot be kept active because no colleague exists.
- LATEST APPROVED RULE: Open Leads / Follow-Ups go to unassigned / pending if no Active Sales exists.
- MINIMUM CORRECTION (applied): nullable responsible columns; Manager assignment; no refusal.

## 5. Freeze checklist
- [x] 14 Markdown files delivered (13 top-level: README, baseline, six design documents, diagrams, open questions, Owner register, ADRs, guardrails, this file; plus `diagrams/README.md`) and 26 Mermaid sources
- [x] Step 15 diagrams reviewed; new flows added
- [x] Cross-document IDs aligned
- [x] Owner decisions synchronized across the package (§3c, §3d); Q-ORT-3b DEFERRED / OPEN FOR FUTURE VERSION and OPS-1 DEFERRED only
- [x] Mermaid source only; no rendered diagrams
- [x] 94 Owner-required tests mapped (IMPLEMENTATION_GUARDRAILS.md §13) plus the 20 final-correction tests FC-1…FC-20 (§13b) the 22 Orthodontics freeze tests OT-1…OT-22 (§13c) and the 24 Orthodontic consistency tests OC-1…OC-24 (§13d)
- [x] Unconfirmed proposals excluded (ILabAllocationStrategy, CaseDoctorAllocation, TreatmentPlan workflow, OnHold/Abandoned, addendum workflow, PatientContact aggregate, Manager approval workflows, price lists, Doctor-share clawback)
- [x] v1.1 Assistant payroll change applied (Base Pay + Overtime)
- [x] **Owner approval of Design Baseline v1.1 (2026-10-20): OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION**

## 6. Open items affecting implementation
**Q-ORT-3b** (an Orthodontic total-price decrease) is DEFERRED / OPEN FOR FUTURE VERSION and blocks no feature: V1 rejects a decrease with `ortho.total_decrease_not_supported`. **OPS-1** (recovery-key storage locations) is DEFERRED, blocks no feature, and must be decided before production go-live (before the first production backup). See OPEN_QUESTIONS.md.

## 7. Change control
Design Baseline v1.1 is frozen and **must not be silently mutated**. After Owner Approval, business behavior may change only through all five steps, in order:

1. an explicit Owner change request;
2. an impact review across the requirements and the design documents;
3. a new baseline version (1.2 or later);
4. a synchronized update of every affected document (and Mermaid source);
5. Owner approval of the new baseline.

Minor implementation details that do not change business behavior (for example a class name, an index choice, a UI layout detail) do not require a new business baseline. If implementation discovers a conflict with a frozen rule: stop, report the conflict, do not invent business behavior, and resolve it through this controlled change.

## 8. Next phase
The next phase after this freeze is the **FRS / implementation-ready functional specification and traceability**. It is stated here only; it is **not** part of this package and has not been started. No implementation code has been generated. The first implementation database gate is defined in DATABASE_DESIGN.md (empty PostgreSQL database, all EF Core migrations, FK / UNIQUE / CHECK verification, database integration tests).
