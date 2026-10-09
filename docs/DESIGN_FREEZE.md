# DESIGN FREEZE — Dental Clinic Management System

**Design Baseline Version: 1.1**
**Status: PROPOSED FREEZE — AWAITING OWNER APPROVAL**

> **Baseline v1.0 is withdrawn and is NOT the final approved baseline.** v1.1 supersedes it because a real business requirement (Assistant payroll) was added before owner approval. The freeze takes effect only when the owner approves v1.1; until then it is a *proposed* freeze. Implementation has NOT started. No application code has been written. Step 16 has NOT been started.

## 1. Scope of the freeze
Requirements + Architecture/Design (Steps 1–15), including every approved correction. Source priority applied: (1) latest explicit user correction, (2) latest approved revision of a Step, (3) earlier approved Step, (4) original requirements, (5) recommendations.

Inventory and X-ray are out of scope everywhere. Open questions remain open; the system's current behavior for each is documented in OPEN_QUESTIONS.md.

## 2. Document list
| # | File | Content |
|---|---|---|
| 1 | DESIGN_BASELINE.md | Source priority, overview, confirmed requirements, neutral seams |
| 2 | DATABASE_DESIGN.md | Schemas, tables, guards G1–G7, indexes |
| 3 | SECURITY_DESIGN.md | Authentication, RBAC matrix, scoping, DB roles, 48 tests |
| 4 | FINANCE_DESIGN.md | Ledger, allocations, earnings, distributions, payroll, 30 tests |
| 5 | API_ARCHITECTURE.md | API (Step 11) and notifications/jobs (Step 10) |
| 6 | DEPLOYMENT_ARCHITECTURE.md | Deployment, CI/CD, backup/restore/monitoring (Steps 12–14) |
| 7 | DIAGRAMS.md | Diagrams 1–21, ERDs 22a–22d, Diagram 23 Assistant Payroll Workflow |
| 8 | OPEN_QUESTIONS.md | All open items, grouped by what they block |
| 9 | ARCHITECTURE_DECISIONS.md | ADR-001 to ADR-015 |
| 10 | IMPLEMENTATION_GUARDRAILS.md | Rules to read before any feature |
| 11 | DESIGN_FREEZE.md | This file |
| — | diagrams/ (`*.mmd`, `README.md`), rendered-diagrams/ (`*.svg`, `*.png`) | Diagram sources and rendered output (26 blocks) |

## 3. Step 15 diagram review — STEP 15 APPROVED
Corrections applied (genuine inconsistencies only):
- Diagram 1: label "token endpoint or manual marking".
- Diagram 3: added missing dependency arrows (Finance→Staff etc.); replaced a Reporting→Payroll arrow with a note.
- Diagram 4: Manager linked to clinical write; valid "never" edge for Doctor→contact data.
- Diagram 11 / ERD 22d: "Ortho charge established" node; charge link optional.
- ERD 22a: removed Settings→AuditLog.
- ERD 22b: owner link nullable; Patient→FollowUps added; notification patient optional.
- ERD 22d: added Doctors→DoctorEarnings.

## 3a. Change log
**v1.1 BUSINESS CHANGE: Assistant Staff + Assistant Payroll + Assistant Payroll Expense.**
- No new application role (Manager, Doctor, Sales / Reception only).
- No new module (16 implementation modules, 7 levels unchanged).
- No Identity account for Assistants (no `user_id`, nothing in `identity.*`).
- No clinical, reception or patient workflow for Assistants.
- Q-PAY-ASST-1 added (report-period allocation). Q-PAY-ASST-2 added (partial / mid-period salary calculation). Q-PAY-2 extended to Assistant payroll.
- Operating Result updated (Assistant Payroll Expense is its own line; Doctor Payroll still not subtracted again).
- Diagram 23 added; ERD 22d extended; Mermaid re-validated (26 blocks); `diagrams/*.mmd`, `diagrams/README.md` and `rendered-diagrams/` created.
- Wording corrections applied package-wide: Q-FIN-9 flow (no earning for held allocations), financial-ownership wording, orthodontic installment wording (Q-ORT-1).

**Scheduling correction (v1.1, before owner approval):** Arrived and Waiting states removed globally. Old Sales Check-In (`Scheduling.Appointments.CheckIn`, `POST …/check-in`, `arrived_at`, `checked_in_by_user_id`) and Doctor Waiting Queue (`GET /api/doctor/queue`) removed. The Sales dashboard has 8 items (the "waiting patients" item removed, not replaced). Appointment statuses are exactly 7. Q-APP-4 added for any future arrival / queue workflow; Q-APP-3 reworded. Diagrams 5 and 8 corrected and all 26 Mermaid blocks re-validated and re-rendered. No replacement state, event or endpoint was invented.

## 3b. v1.1 change detail: ASSISTANT PAYROLL (controlled revision)
Business change: the clinic employs Assistants. They are a **staff type, not an application role**: no login, account, role, notifications or operational workflow. They exist only for staff record, salary tracking, payroll expense and management reporting.

Approved decisions applied:
- **D1 permissions:** `Staff.Manage` (create / edit name / activate-deactivate); `Staff.AssistantSalary.Configure` (change current salary, audited); `Payroll.Assistant.ManageAll` (view, draft, finalize, mark paid). All Manager-only technical keys; no new role.
- **D2:** the database prevents overlapping payroll periods for one Assistant.
- **D3:** `final_amount = salary_snapshot` (CHECK); no arbitrary amount.
- **D4:** while Q-PAY-ASST-1 is open, a fully contained payroll period is included; a partial overlap shows "PENDING / NOT ALLOCATED FOR THIS REPORT RANGE" and the result is labelled "INCOMPLETE — ASSISTANT SALARY ALLOCATION PENDING".
- **D5 / Q-PAY-ASST-2:** partial-period and mid-period salary calculation is a separate open question from Q-PAY-ASST-1; no proration; such a payroll cannot be finalized.
- **Salary history:** `current_salary` + payroll snapshots + audited changes. No effective-dated table.
- **Operating result:** Clinic Gross Revenue − Sales Salary / Commission − Assistant Payroll Expense − Clinic Operating Expenses − other confirmed Clinic-owned expenses. Assistant salary never reduces Revenue; Doctor Payroll is not subtracted again.

Documents changed: DESIGN_BASELINE, DATABASE_DESIGN, FINANCE_DESIGN, SECURITY_DESIGN (tests 42–48), API_ARCHITECTURE (tests 30–36), DIAGRAMS (Diagram 23, ERD 22d, Diagram 4 note), OPEN_QUESTIONS (Q-PAY-ASST-1, Q-PAY-ASST-2, Q-PAY-2 extended), IMPLEMENTATION_GUARDRAILS (rules 15–19), DESIGN_FREEZE. The version/status header was updated in all 11 files. ARCHITECTURE_DECISIONS and DEPLOYMENT_ARCHITECTURE: header only (no new ADR; backup grants already cover every table in the business schemas). Nothing in the "do not change" list was altered.

## 4. Cross-document consistency report
DESIGN FREEZE CONFLICT: C1
- DOCUMENT: DIAGRAMS.md vs OPEN_QUESTIONS.md
- SECTION: Open notes (ortho terms)
- CONFLICT: Diagram referenced Q-ORT-4; open-question list called it Q-ORT-TERMS.
- LATEST APPROVED RULE: Same open item, one ID.
- MINIMUM CORRECTION (applied): renamed to Q-ORT-4 everywhere.

DESIGN FREEZE CONFLICT: C2
- DOCUMENT: DIAGRAMS.md vs OPEN_QUESTIONS.md
- SECTION: Open note (referral-reason visibility)
- CONFLICT: Diagram cited Q-PRV-1; no such entry existed.
- LATEST APPROVED RULE: Referral-reason/DOB visibility was deliberately not locked (configuration, hidden by default).
- MINIMUM CORRECTION (applied): added Q-PRV-1 to OPEN_QUESTIONS.md (non-blocking).

DESIGN FREEZE CONFLICT: C3
- DOCUMENT: DATABASE_DESIGN.md
- SECTION: Deliberate copies cross-reference
- CONFLICT: Pointed to section 12; list is in section 20.
- LATEST APPROVED RULE: n/a (reference error).
- MINIMUM CORRECTION (applied): reference changed to section 20.

Note: wiring points W1/W2 appear as Q-FIN-W1/Q-FIN-W2 in OPEN_QUESTIONS.md; same items.

Final greps found no obsolete structures (SetDiscount, age_at_registration, revenue_distribution_id, doctor_entered_*, Manager-delivered reset links); Inventory, X-ray and `pg_read_all_data` appear only as negations.

## 5. Freeze checklist
- [x] 11 files delivered
- [x] Step 15 diagrams reviewed
- [x] Cross-document IDs aligned
- [x] Open questions listed with current behavior
- [x] Unconfirmed proposals excluded (ILabAllocationStrategy, CaseDoctorAllocation, TreatmentPlan workflow, OnHold/Abandoned, addendum workflow, PatientContact aggregate, Manager approval workflows, price lists, Doctor-share clawback)
- [x] v1.1 Assistant payroll change applied (D1–D5)
- [ ] **Owner approval of Design Baseline v1.1**

## 6. Open items affecting implementation
See OPEN_QUESTIONS.md. Group 1 (blocks implementation now): Q-UI-1, Q-APP-1, Q-CRM-1, Q-FIN-W1, Q-ACC-1. New in v1.1 (block a later module / period reporting): Q-PAY-ASST-1, Q-PAY-ASST-2.

## 7. Change control
After approval, any change to this baseline requires an explicit written change request, a version bump (1.1+), and updates to all affected documents.

## 8. Next step
Awaiting your review. Step 16 and implementation start only after you approve Design Baseline v1.1.
