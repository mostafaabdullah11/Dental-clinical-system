# Task 1 Final Audit

**Design Baseline:** 1.1 — OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION
**Audit date:** 2026-10-23
**Scope:** TASK 1 — FRS + Traceability + Acceptance Criteria

## Status

**PASS**

## Files Audited

- `CLAUDE.md` (untracked as `claude.md`)
- `docs/FRS.md` — TASK 1 / STEP 1.1 — FRS COMPLETE
- `docs/TRACEABILITY_MATRIX.md` — TASK 1 / STEP 1.2 — TRACEABILITY MATRIX COMPLETE
- `docs/ACCEPTANCE_CRITERIA.md` — TASK 1 / STEP 1.3 — ACCEPTANCE CRITERIA COMPLETE
- Frozen package file list (`docs/DESIGN_FREEZE.md` §2) and the `docs/` git state. The frozen documents were consulted only for spot-checks: the FINANCE_DESIGN.md numeric proofs and the package rules on rendered diagrams.

## Mechanical Coverage

All counts below were computed by script.

| Check | Result |
|---|---|
| FR count (FRS) | 237 |
| Unique FR IDs | 237 (duplicates 0) |
| FLOW count (FRS §29) | 16 — FLOW-01 … FLOW-16, each exactly once |
| SN-* source notes remaining in FRS | 0 |
| Primary AC IDs (allocated / defined) | 237 / 237 |
| Negative AC IDs (allocated / defined) | 43 / 43 |
| Total unique AC IDs | 280 |
| ACs with all of Traces, Type, Task, Given, When, Then, Test level | 280 / 280 |
| Forbidden items (FRS §30 → matrix §7 → AC-NEG-001 … 033) | 33 / 33 |
| Privacy boundaries (matrix §5 → AC-NEG-034 … 043) | 10 / 10 |
| High-risk finance / ortho rules mapped (matrix §6) | 22 |
| Stable error codes used in ACs that the FRS does not define | 0 (23 codes used) |

## Cross-Document Consistency

| Check | Result |
|---|---|
| FRs missing from the matrix | 0 |
| Duplicate FRs in the matrix | 0 |
| Unknown FRs (matrix or AC) | 0 |
| FRs without an Acceptance ID | 0 |
| Primary allocations ≠ `FR-XXX-nnn` → `AC-XXX-nnn` | 0 |
| Allocated ACs missing from the AC document (orphans) | 0 |
| Unknown / unallocated AC IDs in the AC document | 0 |
| Primary ACs tracing to the wrong FR | 0 |
| FLOW mismatch (FRS §29 / matrix §4 / AC §5) | 0. The ID lists are identical, and the AC sets per FLOW match the matrix. |
| Direct §29 FR references missing from matrix FLOW rows | 0 |

## Critical Business Rules

**PASS.** Each rule below was spot-checked as FRS → matrix → AC, with the numeric proofs anchored in FINANCE_DESIGN.md.

| Rule | Result |
|---|---|
| A. Fixed Lab Lab-First: payments 2,000 / 3,000 / 5,000 → Lab 3,000, Doctor 4,000, Clinic 3,000; pending entitlement | PASS (FR-LAB-008/009, AC-LAB-008/009) |
| B. Commission 50 + 70 = 120; reversal −50 | PASS (FR-STAFF-004, FR-PAYROLL-003/006) |
| C. Walk-In: Original Sales NULL, zero commission, no pool | PASS (FR-PAYROLL-004, AC-NEG-015) |
| D. Inactive Sales: redistribution or unassigned; attribution unchanged; no new or transferred commission | PASS (FR-CRM-010/011/012, FR-PAYROLL-005) |
| E. Performing-Doctor ownership; no attribution hold | PASS (FR-FIN-026/027/030/031, AC-NEG-016) |
| F. Patient Credit is a liability; no revenue, earning or commission until applied; never negative | PASS (FR-FIN-006/007/008, FR-PAYROLL-007) |
| G. Ortho: 20,000 − 2,000 = 18,000; no schedule before finalization; 5 × 2,600; +Δ adds one PriceChange installment (22,000); decrease rejected with `ortho.total_decrease_not_supported`; no automatic regeneration; Months counts Generated + ManagerAdded only; `ortho.months_conflict_with_paid_history` | PASS (FR-ORT-001…021) |
| H. Scheduling: overlap allowed; arrival is an event; exactly 7 statuses; early-end time released | PASS (FR-SCH-004/005/009/013) |
| I. Medical History: tri-state with NULL ≠ No; no sex field; never blocks booking; audit stores field names only | PASS (FR-MED-004/005/006/007) |
| J. Feedback: no patient login; secure token; Sales-entered flow; results Manager-only | PASS (FR-FDB-003/006/008/011) |
| K. Expenses: 11 categories; Sales may record only Materials / Petty; input `{ categoryId, amount, specification }` with specification required; no Inventory | PASS (FR-EXP-001/002, AC-NEG-001) |
| L. Payroll and attendance: Draft → Finalized → Paid; sealed after finalization; Assistant Final = Base Pay + Overtime; manual partial-month Base Pay; no proration; attendance never deducts pay; Sales has no overtime | PASS (FR-PAYROLL-008/015/016, FR-STAFF-014/017) |
| Latest Owner decisions: follow-up `{ visitId, startAt, durationMinutes, reason }` with duration 1..480 and no default; 8 Lead statuses; 6 Follow-Up sources; 4 Follow-Up statuses (open New / Contacted / NoResponse); 8 Sales dashboard items | PASS |

## Privacy

**PASS.**

- **Doctor:** never receives phone, WhatsApp, email, address, CRM notes or the Original Sales Representative (FR-SEC-002, AC-NEG-034).
- **Sales:** never receives clinical notes, measurements, medication details, referral notes, Doctor percentage, Doctor or Clinic Share, lab finance, or other employees' compensation (FR-SEC-003, AC-NEG-035/036/039).
- **Assistant:** has no principal, role, dashboard or API audience (AC-NEG-004/040).
- **Patient:** has no account, portal or mobile app; only the token feedback flow (AC-NEG-005/006/041).
- **Feedback results:** Manager-only (AC-NEG-043).

## Scope / Negative Requirements

**PASS.** All 33 FRS §30 items are forbidden and each has its own negative criterion: Inventory, X-ray, multi-branch, patient portal / mobile app, Assistant login, insurance, digital consent, printed receipts / invoices, expense attachments, Daily Closing, feedback expiry, Arrived / Waiting statuses, automated SMS / WhatsApp / email, automatic oldest-first application or schedule regeneration, percentage discounts, attribution holds, `separatelyPricedWork`, self-service password reset, and the rest.

No Task-2 work exists. The repository contains no backend, frontend, migration or schema implementation files.

## Repository Hygiene

**AC-NEG-033: PASS in the working tree after cleanup.**

Two sets of files were found tracked and removed from the working tree. Neither is part of the frozen package:

- `docs/rendered-diagrams/`: 52 files (26 `.png` and 26 `.svg`). These are generated 1:1 duplicates of the 26 `docs/diagrams/*.mmd` sources. They violate the Mermaid-source-only rule (DESIGN_FREEZE.md §2, IMPLEMENTATION_GUARDRAILS.md DO NOT #11, OWNER_REVIEW_NOTES.md).
- `docs/README (1).md`: the pre-approval copy ("PROPOSED FREEZE — AWAITING OWNER APPROVAL") of `docs/diagrams/README.md`. It references rendered outputs. DESIGN_FREEZE.md §2 does not list it, and line 61 records it as removed.

All 26 `.mmd` sources are preserved. The re-check finds 0 PNG / JPG / SVG files and 0 rendered-output folders under `docs/`. The design package now matches the frozen file count: 13 top-level Markdown files + `diagrams/README.md` + 26 Mermaid sources = 40.

The 53 deletions are unstaged working-tree changes. The user decides whether to commit them, and any of them can be restored with `git restore`.

## Deferred Items

Exactly two:

- **Q-ORT-3b:** V1 rejects every Ortho Total decrease with `ortho.total_decrease_not_supported` (FR-ORT-010, AC-ORT-010, AC-NEG-021).
- **OPS-1:** the exact physical storage locations of the backup recovery private keys. This must be decided before production go-live (FR-OPS-007).

## Files Changed During Final Audit

- Created: `docs/TASK1_FINAL_AUDIT.md`
- Deleted (working tree, unstaged): `docs/rendered-diagrams/*` (52 files), `docs/README (1).md`
- Not modified: FRS.md, TRACEABILITY_MATRIX.md, ACCEPTANCE_CRITERIA.md, CLAUDE.md, the frozen design documents, and every `.mmd` file.

## Blockers

None.
