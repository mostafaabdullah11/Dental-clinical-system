# Task 4 Final Audit — Finance, Labs, Orthodontics, Payroll, Expenses, Attendance

**Baseline:** Design v1.1 (frozen), FRS, Traceability Matrix, Acceptance Criteria, Owner decisions recorded during Task 4.
**Branch:** `main` (uncommitted; nothing committed, pushed or tagged).
**Date:** 2026-10-27

**Verdict: TASK 4 COMPLETE.** Every required flow passes; no blocker remains (§10).

---

## 1. Gate results

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build Clinic.sln` | **PASS**: 0 errors, 0 warnings |
| Tests (real PostgreSQL) | `dotnet test --solution Clinic.sln` | **PASS**: 234 / 234 (integration + architecture), 0 failed, 3m49s (final run, 2026-10-27) |
| Model / migration sync | `dotnet ef migrations has-pending-model-changes` | **PASS**: no changes |
| Frontend typecheck | `npm.cmd run typecheck` | **PASS** |
| Frontend build | `npm.cmd run build` | **PASS**: 100 modules, 432.13 kB JS (120.02 kB gzip) |
| Lint | — | Not configured (no script exists) |
| Empty DB → Task 4 head | The test fixture creates a throwaway `clinic_test_*` DB and applies all 33 migrations as `clinic_migrator` | **PASS** |
| Task 3 DB → Task 4 head | `Task3_database_upgrades_to_task4`: populated Task 3 DB, then migrate | **PASS**: row counts, contact value and Task 3 role grants unchanged |
| Task 2 DB → Task 3 head | `Task2_database_upgrades_to_task3`, now pinned to `Notifications_Task3Grants` | **PASS** |

Task 1–3 tests changed only where they asserted that Task 4 did **not yet exist**. Examples: "no finance table", "credit = null", the Doctor key list, and the OpenAPI token list. Each was replaced with the Task 4 assertion. No Task 1–3 behavior changed.

## 2. Migrations (forward-only; Task 1–3 migrations untouched)

| # | Migration | Content |
|---|---|---|
| 26 | `Configuration_Task4` | `lab_percentage_history` (insert-only, effective-dated), `payment_methods` with seeds Cash / Visa / InstaPay in Arabic. No lab % seed: a missing rate returns 422 `lab.percentage_not_configured`. |
| 27 | `Staff_Attendance` | `attendance_records`, `assistant_overtime_entries`, composite FK §24 #12, grants |
| 28 | `Finance_Ledger` | 10 ledger tables; nullable-parent keys §24 #16/#28; G7(b); G8 conservation / credit ≥ 0 / 0 ≤ allocated ≤ Billed (deferred); G8b distribution identity (deferred); grants (insert-only); Worker column grants for J2 |
| 29 | `Orthodontics_Installments` | installments, revisions, applications; §24 #18; G5(a)–(d); G9(a)(c)(d); G10(a)(b) |
| 30 | `Payroll_Initial` | 8 payroll tables; G2 sealing, status transitions, month-ended rule, item ownership / month / deactivation / rate checks, overtime void block |
| 31 | `Expenses_Initial` | 11 seeded categories (two Sales-recordable), Sales-category trigger, creator columns not updatable |
| 32 | `Xref_Task4ForeignKeys` | 39 cross-schema FKs, plus `ortho_cases (treatment_case_id, charge_id) → patient_charges (treatment_case_id, id)` |
| 33 | `Identity_Task4Permissions` | 30 keys mapped exactly per SECURITY_DESIGN §4 |

The trigger-based checks above use no FOR UPDATE on insert-only tables. Serialization uses transaction advisory locks, per patient and per installment / allocation.

## 3. Flow checklist

| Flow | Status | Evidence (tests) |
|---|---|---|
| F4.1 Charges at Visit finalization (Price − Discount; Charge ≠ Payment; finalize failure writes nothing) | **PASS** | `Finalize_establishes_the_charge…`, `Finalize_failure_rolls_back_everything` |
| F4.2 Derived balance (Billed / Collected / Outstanding / Credit) | **PASS** | same |
| F4.3 Payments, explicit allocations, remainder → Patient Credit, credit application, idempotency (same key + other body → 409), G8 at DB | **PASS** | `Payment_allocates_creates_credit_is_idempotent…` |
| F4.4 General Lab at the allocation-time rate (100 at 40% / 20% → 40 / 20 / 40; a later rate change never recalculates) | **PASS** | `General_lab_distributes…` |
| F4.5 Fixed Lab, Lab-First (owner case 2,000 / 3,000 / 5,000 → Lab 3,000 / Doctor 4,000 / Clinic 3,000; pending 800 after first payment; Manager revision re-runs the waterfall) | **PASS** | `Fixed_lab_uses_lab_first_cash_priority` |
| F4.6 Performing-Doctor ownership (reassignment and deactivation never move money) | **PASS** | `Performing_doctor_keeps_ownership…` |
| F4.7 Doctor own finance (own items only, own %, no contact data) | **PASS** | `Doctor_and_sales_finance_audiences_are_isolated`, `General_lab…` |
| F4.8 Sales finance view (patient-facing money only; no correction / management routes) | **PASS** | same; OpenAPI audience test |
| Corrections: refund, reversal, reallocation, replacement, charge adjustment (Manager only, new rows, audited) | **PASS** | `Reversal_and_refund_are_controlled_new_entries` |
| F4.9 Sales commission (5% → 7% future only; reversal at the original rate; Walk-In 0; inactive Sales accrues nothing) | **PASS** | `Commission_and_doctor_payroll…` |
| F4.10 Ortho finance (20,000 − 2,000, deposit 5,000, 5 months → 5 × 2,600; +4,000 → PriceChange 4,000; decrease → `ortho.total_decrease_not_supported`; generic adjustment → `ortho.use_terms_edit`; paid history immutable) | **PASS** | `Initial_schedule…`, `Payments_terms_and_schedule…`; SPA `/ortho/:id` screen (§8) |
| F4.11 J2 installment-due-tomorrow reminder (Worker, `clinic_worker` grants, idempotent, Sales recipients only) | **PASS** | `Installment_due_tomorrow_reminder_runs_in_the_worker` |
| F4.12 Outstanding / installment follow-up references the patient's own charge / installment | **PASS** | `Outstanding_follow_ups_reference…`; UI "متابعة المتبقي" button |
| F4.13 Doctor payroll (earnings by collection month, adjustment, finalize only after month end, sealed, once per month) | **PASS** | `Commission_and_doctor_payroll…` |
| F4.14 Sales payroll (base + commission + bonus − deductions; sealed; own read only) | **PASS** | same |
| F4.15 Assistant staff (salary, overtime rate; no account / login) | **PASS** | `Assistant_payroll_is_base_pay_plus_approved_overtime` |
| F4.16 Assistant payroll (6,000 + 500 = 6,500; manual partial 3,500 + 400 = 3,900 with reason code; overtime in payroll cannot be voided) | **PASS** | same |
| F4.ATT Attendance (Manager-maintained, duplicate day 409, no automatic deduction) | **PASS** | same |
| F4.17 Manager expenses (11 categories, direct audited edit, creator never overwritten, no delete / finalize / attachment) | **PASS** | `Expenses_follow_the_category_and_role_rules` |
| F4.18 Sales operational expenses (create only, two categories, minimal response; DB trigger backstop) | **PASS** | same |
| F4.19 Dashboards (Manager live, no Daily Closing; exactly 8 Sales items; arrival only as `arrivedAt`; no Doctor dashboard) | **PASS** | `Dashboards_show_live_figures_per_audience` |
| F4.20 Reports and exports (layout, result formula, memo, full-month payroll only with no proration, CSV whitelist + formula neutralization, Manager only) | **PASS** | `Reports_are_derived…`, `Full_month_ranges_include_finalized_payroll…` |
| F4.21 Financial timeline per audience | **PASS** | `Timeline_shows_finance_entries_per_audience` |
| F4.SEC Permission matrix and privacy | **PASS** | `Task4_permission_matrix_is_exact`, architecture DTO whitelists, OpenAPI audience test |
| F4.DB Grants, constraints, guards, upgrade | **PASS** | `Task4_grants_are_least_privilege`, `Task4_constraints_and_guards_exist`, `Task3_database_upgrades_to_task4` |
| Frontend (Arabic RTL) | **PASS** | See §8 |

## 4. Finance formulas as implemented

- Billed = `final_price` (Price − Discount, set at finalization) + Σ `charge_adjustments`.
- Collected = Σ signed allocations. Outstanding = Billed − Collected.
- Payment conservation (G8): `payment.amount = Σ allocations + Σ credit entries`. Credit ≥ 0 per patient. 0 ≤ allocated ≤ Billed per charge.
- General lab: entitlement = round_half_up(effective × Doctor % snapshot). Lab = round_half_up(effective × lab % at `allocated_at`). Clinic = remainder, never < 0. The rate is recorded on the first distribution row and reused by corrections.
- Fixed lab (Lab-First): in allocation order, cash covers the Lab, then the FIFO Doctor-entitlement queue, then the Clinic leftover. Pending = entitlements − earnings.
- Identity (G8b): per event, Σ(lab + clinic) + Σ earnings = basis. Per allocation, 0 ≤ earnings ≤ entitlements, and cumulative clinic share ≥ 0.
- Consultation money creates no entitlement or distribution.
- Commission = round_half_up(collected × rate in force at the collection). A reversal uses the original collection's rate. Walk-In = 0. Nothing is accrued after deactivation.
- Estimated Operating Result = Clinic share + Consultation collected − Sales compensation − Assistant payroll (Base + Overtime) − Expenses. Doctor payroll is not subtracted. Only Finalized / Paid payroll months fully inside the range count.

Money is `numeric(12,2)` or `decimal` everywhere. Architecture tests assert that no float or double type exists.

## 5. Privacy / security

- Sales DTOs carry no percentage, share, entitlement, earning or lab member. This is asserted by reflection and by OpenAPI token checks.
- `doctorPercentage` appears in the Doctor document only on `DoctorOwnWorkDto`, as FR-FIN-029 requires.
- Doctor finance, payroll and ortho DTOs carry no phone, WhatsApp, email, address, CRM notes, patient source or original Sales employee. Contact-bearing keys are never mapped to DOCTOR.
- Corrections, adjustments, lab %, Fixed Lab revision, ortho terms / schedule, payroll management, expenses management, attendance and reports are Manager-only. Each has a test.
- Every money command takes the patient advisory lock. Payments, credit application and corrections require `Idempotency-Key`.
- `clinic_app` has no DELETE on any Task 4 table. Ledger, history and item tables are insert-only. Updatable columns are exactly the L columns.
- `clinic_worker` reads only the J2 columns.

## 6. Worker

J2 `J2-ortho-installments-due-tomorrow` runs every 15 minutes under advisory lock 7302 and is registered in `RecurringJobCatalog`. It uses Finance's read-only ledger (`AddFinanceLedgerForJobs`) plus `AddOrthodonticsJobs`. J4 re-validates installment reminders through `InstallmentNotificationValidator`.

## 7. Engineering decisions (no business change)

- **Reports module:** read-only SQL over Finance, Payroll and Expenses rows. It owns no table. PDF export uses the browser print of the report page; CSV is streamed from the API.
- **Composite FKs whose parent key has a nullable column** are declared in migration SQL.
- **Clinical "charge freeze" (G6):** the existing Task 3 finalized-visit freeze already covers it, because charges exist only after finalization.
- **Defect fixed during testing:** an Assistant payroll draft was first saved with `final_amount = 0`, which G2's CHECK correctly rejected.

## 8. Frontend (Arabic RTL)

Implemented and building:

- **Patient finance panel (Sales / Manager):** balance, charges, payment with allocations and an idempotency key, credit application, credit history, payment history, outstanding follow-up, and the Manager correction / adjustment actions.
- **Home dashboards:** Manager live dashboard and the Sales eight items.
- **Doctor:** own work and own payroll.
- **Sales:** own payroll, outstanding list, operational expense entry.
- **Manager pages:**
  - finance (charges, Fixed Lab revisions, payments, credit, pending entitlements, lab %, payment methods);
  - payroll for all three types (drafts, adjustments, partial-month Base Pay, finalize, mark paid);
  - expenses and categories;
  - reports with presets, CSV export and print;
  - attendance and Assistant overtime;
  - Assistant salary / overtime-rate edits.
- All Task 4 error codes have Arabic messages.

**Orthodontic case screen** (`/ortho/:id`, FR-ORT-005..021).

Files: `pages/OrthoCasePage.tsx` and `components/ortho/` (`OrthoTables`, `OrthoApplyForm`, `OrthoTermsForm`, `OrthoScheduleForm`).

Each audience reads its own API contract:
- **Doctor:** `GET /api/doctor/orthodontic-cases/{id}`. Read-only. The response carries no collections, and no action is rendered.
- **Sales:** `GET /api/sales/orthodontic-cases/{id}`. Shows the schedule, statuses and collections. Manual installment application goes to `POST …/installment-payments`.
- **Manager:** the Sales view plus:
  - terms edit with `PUT …/terms` (If-Match, reason, Arabic message for `ortho.total_decrease_not_supported`);
  - schedule batch with `PUT …/installments` (amounts, unpaid due dates, add, retire). Paid installments are locked in the form and enforced again by G5.

Behaviour:
- Actions render only for the holder of `Orthodontics.Installments.ApplyPayment`, `.Terms.Edit` or `.Installments.Edit`. The server enforces every key again. No new DTO was added.
- Every write uses an inline confirmation step (no `alert` / `prompt`) and shows a success notice.
- After a save the screen takes the server's response. The forms are keyed on the case `version` (terms revision, installment count, revision count, application count), so they re-mount with no stale values.

Navigation entry points:
- the patient finance panel ("فتح حالة التقويم", Sales / Manager);
- the Doctor treatment-case list (cases with procedure `Orthodontics`; also visible to the Manager);
- Sales dashboard item 6 (tomorrow's installments).

All three pass `patientId` for the "back to patient" link.

**Frontend tests:** the repository has no frontend test infrastructure, so none were added. The flows behind the screen are covered by the API integration tests in §3:
- Doctor read without allocations; Doctor / Sales refused on Manager routes;
- Sales application;
- Manager terms increase and decrease refusal;
- schedule batch and paid-history locks.

## 9. Traceability cross-check

| Area | FRs | Backend + tests | UI |
|---|---|---|---|
| FR-FIN-001..031 | 31 | covered | covered (FR-FIN-011: intentionally no receipt / invoice) |
| FR-LAB-001..010 | 10 | covered | covered |
| FR-ORT-001..004, 007, 009–013, 019–021 | — | covered | Task 3 Visit screen covers creation and initial terms |
| FR-ORT-005, 006, 008, 014–018 | — | covered | covered (`/ortho/:id`) |
| FR-PAYROLL-001..016 | 16 | covered | covered |
| FR-STAFF-012..017 | 6 | covered | covered |
| FR-EXP-001..007 | 7 | covered | covered |
| FR-REPORT-001..009 | 9 | covered | covered |

## 10. Remaining blockers (genuine)

None. The orthodontic case screen is routed, reachable from the three entry points in §8, and refreshes from the server after each save.

No business question is open. The deferred items Q-ORT-3b and OPS-1 are unchanged and do not block Task 4.
