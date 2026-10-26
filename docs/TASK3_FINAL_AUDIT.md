# Task 3 — Final Audit

**Scope:** Appointments → Arrival / Doctor Queue → Visit → Clinical treatment → Prescription → Next Appointment → Referral → Feedback Request → Sales queues → Notifications → Patient timeline, plus the Task-3 portions of FIN (discount, pricing view), LAB (initial Fixed Lab), ORT (initial terms), SEC, AUDIT and NOTIF.

**Baseline:** Design Baseline v1.1 (frozen), with the Owner decisions of 2026-10-24:

1. Feedback has no expiry.
2. Pre-finalization fixed-EGP discounts can be set by the Doctor, Sales or the Manager.
3. Fixed Lab and Ortho initial terms are stored only.
4. There are exactly seven appointment statuses.
5. No Task 4 financial processing.

**Method:** the implementation itself was audited end to end:

- database
- migrations
- backend routes and permission seeds
- DTOs and privacy
- Worker
- React SPA
- tests

Every gate was run on 2026-10-24 against a real PostgreSQL 18 server, using throwaway `clinic_test_*` databases. Credentials came from pgpass only.

## 1. Status

**PASS — Task 3 complete.** Every required flow F3.1–F3.14 passes (§12). All gates pass (§3).

Nothing has been committed, pushed or tagged.

## 2. Scope audited

The scope was derived mechanically from the `Task` column of TRACEABILITY_MATRIX.md and the `**Task:**` field of ACCEPTANCE_CRITERIA.md.

**50 FRs allocated to Task 3 only:**

- FR-SCH-001..014
- FR-CLN-001..006, -008, -011, -013..015
- FR-RX-001..004
- FR-REF-001..004
- FR-FDB-001..011
- FR-NOTIF-001..003, -006..008

**27 FRs shared with other Tasks, Task-3 part only:**

- FR-CLN-007, -009, -010, -012
- FR-FIN-001, -021, -022, -023, -025
- FR-LAB-006, -007
- FR-ORT-001, -002, -004
- FR-NOTIF-005, -009
- FR-SEC-002..005, -008, -009, -011
- FR-AUDIT-002, -005, -006, -008

**ACs:** 99 ACs carry Task 3. These are:

- AC-SCH-001..014
- AC-CLN-001..015
- AC-RX-001..004
- AC-REF-001..004
- AC-FDB-001..011
- AC-NOTIF-001..009
- AC-FIN-001 / -021 / -022 / -023 / -025
- AC-LAB-006 / -007
- AC-ORT-001 / -002 / -004
- AC-SEC-002..005 / -008 / -009 / -011
- AC-AUDIT-002 / -005 / -006 / -008
- 18 AC-NEG-*

## 3. Build / test / migration results

| Gate | Command | Result |
|---|---|---|
| Backend build | `dotnet build Clinic.sln` | **0 warnings, 0 errors** (19 projects, including 6 new modules) |
| Backend tests | `dotnet test --solution Clinic.sln` (real PostgreSQL, `CLINIC_TEST_PG_ADMIN` + pgpass) | **206 / 206 passed** (Architecture 30, Integration 176), 0 skipped; the earlier full-suite runs during the audit were also green |
| Frontend typecheck | `npm.cmd run typecheck` (strict, `noUnusedLocals`) | **pass** |
| Frontend build | `npm.cmd run build` (`tsc -b && vite build`) | **pass** (86 modules) |
| Frontend lint | — | **no lint script is configured** in `package.json`; nothing to run |
| Migrations from zero | Test fixture: bootstrap roles → create DB → all 25 migrations as `clinic_migrator`. Also `dotnet ef database update` on an empty `clinic_test_worker` DB | **pass** |
| Migrations from the Task 2 DB | `Task3DatabaseTests.Task2_database_upgrades_to_task3` (details below) | **pass** |
| Worker host (live) | `Clinic.Worker` against the migrated `clinic_test_worker` DB as `clinic_worker` for 25 s | J1, J3, J4, J5, J7 all `succeeded`; J7 printed counts only |
| API host (live, Development) | `/health/live`, public feedback, CORS, anonymous Doctor route | 200; generic 404 for an unknown token; `Access-Control-Allow-Origin` only for the configured website origin; 401 anonymous; the token never appeared in the log |

The upgrade test works as follows:

1. Migrate to `Identity_TimelinePermissions` (Task 2 head).
2. Seed users, roles, Doctor, Sales, patient, contact, assignment and follow-up.
3. Apply the remaining 10 migrations.
4. Verify that row counts, the patient code and the Task-2 permission rows are unchanged.
5. Verify that a Task-3 appointment can be inserted for the legacy patient as `clinic_app`.

The throwaway databases were dropped afterwards.

## 4. Migrations (10 new, applied after the 15 Task-2 migrations)

| # | Migration | Content |
|---|---|---|
| 1 | `Crm_Referrals` | `crm.referrals` (CHECK type / status); FK `follow_ups.referral_id`; grants (app SELECT / INSERT, UPDATE(follow_up_status) only; worker SELECT(id)) |
| 2 | `Scheduling_Initial` | `availability_weeks`, `availability_ranges`, `appointments`, `appointment_arrivals`; `btree_gist` in `infra`; exclusion constraint `ex_availability_ranges_no_overlap` (live ranges only — **appointments are deliberately not constrained**); view `v_effective_booked_ranges` = LEAST(nominal end, visit end); column-restricted grants |
| 3 | `Clinical_Initial` | 10 clinical tables; catalog seed (19 procedure types with `option_definition`); **G1** shared guard `infra.assert_visit_editable_or_corrected` with triggers on visits / treatment_records / treatment_steps / root_canal_measurements / treatment_cases; **G6** triggers on case_work_items and pricing_discount_entries; grants |
| 4 | `Prescriptions_Initial` | prescriptions, prescription_items; G1 triggers; grants (no worker access) |
| 5 | `Orthodontics_Initial` | ortho_cases (`charge_id` nullable, no FK until Task 4), ortho_term_revisions (role / number CHECK); **G9(b)** no-decrease trigger; insert-only grants |
| 6 | `Finance_FixedLabInitial` | fixed_lab_expenses, fixed_lab_revisions (role / number CHECK); **G7(a)** Fixed-model-only trigger; insert-only grants |
| 7 | `Feedback_Initial` | feedback_requests, feedback_entries (shape CHECKs, unique per request); grants (worker: ids only) |
| 8 | `Xref_Task3ForeignKeys` | 39 cross-schema FKs, including composites §24 #10, #11, #13, #29 |
| 9 | `Identity_Task3Permissions` | 39 permission keys and 69 role mappings, exactly as in SECURITY_DESIGN.md §4 |
| 10 | `Notifications_Task3Grants` | `clinic_app` UPDATE(status, read_at) on notifications (inbox read, source-transaction cancel of pending reminders) |

Every migration has a reversible `Down`. Every identifier is ≤ 63 characters: one 66-character FK name was found by a test and shortened.

## 5. Schemas and tables

**New schemas (6):** `scheduling`, `clinical`, `prescriptions`, `orthodontics`, `finance`, `feedback`, plus `crm.referrals`.

**Tables (21):**

- `scheduling`: availability_weeks, availability_ranges, appointments, appointment_arrivals
- `clinical`: procedure_types, visits, treatment_cases, treatment_records, case_work_items, treatment_steps, root_canal_measurements, implant_details, pricing_discount_entries, visit_corrections
- `prescriptions`: prescriptions, prescription_items
- `orthodontics`: ortho_cases, ortho_term_revisions
- `finance`: fixed_lab_expenses, fixed_lab_revisions
- `feedback`: feedback_requests, feedback_entries

**View:** `scheduling.v_effective_booked_ranges`.

Verified by tests:

- Declared parent keys exist: `ak_visits_id_patient_id[_doctor_id]`, `ak_treatment_cases_id_patient_id`, `ak_feedback_requests_id_visit_patient_doctor`, `ak_fixed_lab_expenses_id_case`.
- Every composite FK exists.
- No DELETE / TRUNCATE grant exists for `clinic_app` / `clinic_worker`.
- Insert-only tables have no UPDATE grant: discount entries, visit corrections, treatment records, catalog, feedback entries, fixed lab, ortho revisions.
- Frozen appointment / work-item / visit / referral columns are not updatable.
- The Worker cannot read reasons, notes, ratings, tokens, visits or prescriptions.
- Objects are owned by `clinic_migrator`.

Still absent:

- No float / money column.
- No `branch_id`.
- No charge / payment / installment / payroll / attendance / expense / inventory table.

## 6. Routes

Prefixes are organizational only. The permission key is the check, and there is a scoped 404 for other Doctors' resources.

| Audience | Routes |
|---|---|
| Doctor `/api/doctor` | `GET appointments`, `GET queue`, `GET schedule`, `POST appointments/{id}/reschedule` (own follow-up), `POST appointments/follow-up`; `GET procedure-types`, `GET patients/{id}/visits`, `GET patients/{id}/treatment-cases`, `GET treatment-cases/{id}`, `GET/POST/PUT visits[/{id}]`, `POST visits/{id}/finalize`, `POST visits/{id}/corrections`, `POST/PUT visits/{id}/treatments[/{recordId}]`, `PUT visits/{id}/cases/{caseId}/status`, `POST …/steps/{code}/complete`, `PUT …/canal-measurements`, `PUT …/implant`, `PUT treatment-records/{id}/pricing`, `PUT work-items/{id}/pricing`, `PUT pricing-lines/{lineId}/discount`; `GET patients/{id}/prescriptions`, `GET prescriptions/{id}[/print]`, `POST visits/{id}/prescriptions`, `PUT prescriptions/{id}`, `POST prescriptions/{id}/corrections`; `POST visits/{id}/orthodontic-cases`, `GET orthodontic-cases/{id}`; `POST/GET fixed-lab/{caseId}`; `POST visits/{id}/referrals`, `GET patients/{id}/referrals` |
| Sales `/api/sales` (also used by the Manager through the same keys) | `GET doctors`, `GET availability/slots`, `GET/POST appointments`, `GET appointments/{id}`, `POST appointments/{id}/status` (Confirm / Cancel / NoShow only), `POST appointments/{id}/reschedule`, `POST appointments/{id}/arrival`, `POST …/arrival/void`; `GET pricing-lines`, `PUT pricing-lines/{lineId}/discount`, `GET patients/{id}/clinical-summary`; `GET referrals[/{id}]`, `PATCH referrals/{id}/status`; `GET feedback-requests`, `POST feedback-requests/{id}/link`, `POST …/mark-sent`, `POST feedback-entries` |
| Manager `/api/manager` | `GET/PUT doctors/{id}/availability/{weekStart}`, `POST …/publish`; appointment list / book / status / reschedule (no arrival — T5); `GET feedback` (results) |
| Authenticated `/api/me/notifications` | `GET`, `GET unread-count`, `PATCH {id}/read`, `POST read-all` (`Notifications.Read.Own`: Sales and Manager; **Doctor 403**) |
| Anonymous `/api/public/feedback/{token}` | `GET`, `POST`. These are on the allow-list, which now has **exactly six** anonymous routes. They are CSRF-exempt and token-bound, have no expiry, and have CORS for the website origin only. Rate limits apply per IP (host policy) and per token hash (in-endpoint). |

There is no DELETE route for appointments, visits or feedback. There is no reopen route, no feedback-status write route and no `CheckIn` key.

## 7. Frontend flows (Arabic, RTL; clinic timezone from `/api/me`; no browser storage)

| Role | Screens |
|---|---|
| Manager | **إتاحة الأطباء**: weekly ranges per Doctor, draft and publish. **المواعيد**: book, status, reschedule. **نتائج التقييمات**: Doctor / date / source filters. **الإشعارات**. Patient page: appointments, pricing lines and discount, clinical summary, read-only visit history. |
| Sales / Reception | **المواعيد**: day view; booking with patient search, Doctor, manual duration, published availability and booked ranges; arrival record / void; confirm / cancel / no-show; reschedule. **تحويلات الأطباء**: status work; no notes. **طلبات التقييم**: derived state, link prepared once, mark sent, Sales-entered feedback with or without a Visit. **الإشعارات** with unread count. Patient page: appointments, pricing lines with fixed-EGP discount, clinical summary (whitelist). |
| Doctor | **قائمة الانتظار**: derived queue, start Visit. **جدولي**: week, own follow-up reschedule. **Visit workspace** `/visits/:id`, covering: notes and consultation fee; catalog-driven treatment forms built from `option_definition`; continuing a case; case status; root canal steps, Post & Core and canals 1–5; implant details; price and discount; Fixed Lab entry; ortho case and initial terms; prescriptions with a printable page `/prescriptions/:id/print`; next appointment; referral; finalize; own-Doctor corrections after finalization. Patient page: visits, cases, referrals with notes, walk-in Visit. |
| Patient (anonymous) | `/feedback/:token`: rating and comment, no login, neutral "already submitted" and "invalid" states. |

Every Task 3 API error code is mapped to Arabic in `api/messages.ts`. The architecture test still forbids:

- `localStorage` / `sessionStorage`
- i18n switches
- `dangerouslySetInnerHTML`

## 8. Privacy results — PASS

| Check | Evidence |
|---|---|
| Doctor never receives phone / WhatsApp / email / address / CRM notes / Sales notes / Original Sales / percentage | Canary test across 10 Doctor routes: appointments, queue, visit, visit list, cases, case, prescriptions, print, referrals, timeline. Architecture tests: no Doctor namespace references `Patients.Contracts.Contact` or Sales features, and no `[ContactData]` type lives in a Doctor namespace. Doctor OpenAPI has no `phone` / `whatsapp` / `salesNotes` / `doctorPercentage` / `/api/sales/`. |
| Sales never receives clinical notes, referral notes, canal measurements, teeth / details, prescription medications, percentage or shares | Canary test across 7 Sales routes. Sales DTO member scan (architecture). Sales OpenAPI has no `clinicalNotes` / `medicationName` / `lengthMm` / `teeth`. Sales gets 403 on every clinical route. |
| Referral notes | `SalesReferralDto` has no `notes` member. A `notes` member in a Sales status request is rejected with 400. |
| Feedback results | Manager only (`Feedback.Results.Read`). Sales and Doctor get 403. The queue items and the Sales entry echo carry no rating or comment. |
| Public feedback | Returns only the Arabic form and the Doctor's name. Identifying input is rejected (400). Unknown and malformed tokens get the same generic 404. |
| Logs / audit | No clinical canary, phone or feedback token appears in the logs: `ClinicLogging.MaskText` scrubs `/api/public/feedback/<token>`. Clinical, prescription and referral rows are `[RestrictedAudit]`. Tokens are never audited (only the SHA-256 hash is stored). |
| Contact-bearing keys | Seven Task-3 keys were added to `PermissionKeys.ContactBearing`. None is mapped to `DOCTOR`; this is verified by a test. |

## 9. Security / integrity results — PASS

- Server-side authorization is enforced by permission keys, scoped 404 and audience DTOs.
- Cross-Doctor mutations return 404 on 10 Visit sub-routes, and nothing changes.
- The Manager has no initial price, fee, Fixed Lab or ortho terms authority, and no clinical correction.
- **G1** blocks direct `clinic_app` SQL on finalized Visit notes / fee, prescriptions and work-item price (`23514`). Only the own-Doctor correction path passes: `clinic.audited_correction` plus a `visit_corrections` row for this transaction.
- **G6** rejects a discount above the price, or after finalization, at the database.
- **G7(a)** and **G9(b)** are enforced.
- **Concurrency:**
  - If-Match is enforced (428 missing / 412 stale) on availability publish, appointments, visit, case status, price, prescriptions and referrals.
  - Concurrent discounts with the same `entrySeq` produce exactly one success and one 409 `discount.stale_entry`.
  - A public submission racing a Sales entry produces exactly one entry, through the unique `feedback_request_id`.
- **Finalize is one transaction:** Visit Finalized → appointment Completed + `visit_ended_at` → `VisitFinalized` event → feedback request + notifications. A failing precondition (`fixed_lab.initial_required`, `ortho.deposit_exceeds_effective_total`) writes nothing. Finalize is idempotent.

## 10. Notifications / Worker — PASS

- **Planners:** J1 (Standard → Appointment Tomorrow; ImplantFollowUp → Implant Follow-Up Tomorrow) and J3 (follow-ups).
- **Event-driven:** New Doctor Referral and Feedback Request Pending are enqueued in the source transaction, to every active Sales user. They never target a Doctor.
- **J4 dispatcher:** claims each row with `FOR UPDATE SKIP LOCKED`, re-validates the recipient and the source, and cancels a reminder whose appointment was cancelled. A poison row never blocks the others.
- **J5:** retries while `retry_count < 5`; the row stays Failed at 5.
- **J7:** reports counts only.
- **Not registered:** there is no J2 (Task 4) and no J6 (feedback never expires).
- **Tests:** they run the jobs through the Worker's own composition (`WorkerComposition.AddClinicWorker`, `ValidateOnBuild`) under the real `clinic_worker` grants.

## 11. Technical decisions and deviations (no business behavior changed)

1. **Recurring job runner.** The design names Hangfire, but B6 is marked TECHNICAL. The Worker uses an in-process `RecurringJobRunner`. Each job runs in its own DI scope, guarded by a PostgreSQL session advisory lock (`pg_try_advisory_lock`), which gives `DisableConcurrentExecution` semantics across instances. This avoids a Hangfire storage schema with its own grants. Frequencies match B6: J1 / J3 every 15 minutes, J4 every minute, J5 / J7 every 5 minutes.
2. **Prescription print.** It is a JSON print model rendered as a printable Arabic page in the SPA (browser print). No PDF library is used. The model has no contact member at all.
3. **Feedback form host.** `Feedback:PublicFormBaseUrl` builds the link: Development uses `http://localhost:5173/feedback`, and **production must configure it**. The SPA ships `/feedback/:token`, and the clinic website may instead call the two public routes, which have CORS for `Feedback:WebsiteOrigins`.
4. **Per-token limit.** It is an in-memory limiter per API instance, which is correct for the single-instance V1 topology.
5. **`delivery_channel`.** The design names no value list, so it is free text of at most 40 characters (default `واتساب` in the UI). Mark-sent requires a prepared link (422 `rule_violation`, field `link`) — a technical precondition.
6. **Catalog.**
   - Procedure codes are language-neutral; names are Arabic seed data.
   - "Pediatric Other / Other" use free-text `name` (required) and `details` (optional), as in the catalog mapping.
   - Orthodontics stores `area` / `appliance_type` (required only for Appliance).
7. **`Notifications_Task3Grants`.** `clinic_app` needs UPDATE(status, read_at) for inbox read and for cancelling its own pending reminders. Delivery and retry remain Worker-only.
8. **Manager on Doctor "own" routes.** Returns 404 (scope), except the consultation-fee rule, which answers 403. Either way the request is refused.

## 12. End-to-end flows

| Flow | Result | Evidence (integration tests) |
|---|---|---|
| F3.1 Manager availability | **PASS** | `SchedulingFlowTests.Manager_publishes_availability_and_sales_sees_published_slots` |
| F3.2 Sales booking | **PASS** | `…Sales_books_with_manual_duration_and_overlaps_are_allowed` (1–480 manual duration, overlap allowed, contact patterns refused, no Arrived / Waiting action, confirm / cancel transitions) |
| F3.3 Arrival → Doctor queue | **PASS** | `…Arrival_puts_the_patient_in_the_doctor_queue_without_changing_status` (event not status; Sales only; void; today only; other Doctor's queue empty) |
| F3.4 Start Visit | **PASS** | `VisitFlowTests.Start_visit_from_the_queue` (InTreatment, out of queue, 409 second start, cross-Doctor 404, walk-in) |
| F3.5 Clinical procedure workflow | **PASS** | `…Clinical_procedure_workflow`, `Task3AdditionalTests.Second_doctor_continues_a_case_with_own_work`, `…Treatment_case_lifecycle`, `…Pricing_invariants_and_discount_concurrency` |
| F3.6 Prescription | **PASS** | `…Prescription_lifecycle` (print without contact, Sales 403, G1 after finalization, own correction only) |
| F3.7 Doctor next appointment | **PASS** | `SchedulingFlowTests.Doctor_creates_and_reschedules_only_own_follow_up` |
| F3.8 Reschedule preserving history | **PASS** | `…Reschedule_preserves_history` (new row points back; old row frozen; second reschedule refused; `clinic_app` cannot rewrite or delete) |
| F3.9 Doctor referral → Sales | **PASS** | `ReferralFeedbackTests.Doctor_referral_reaches_sales_without_notes` |
| F3.10 Visit finalize | **PASS** | `VisitFlowTests.Finalize_visit_is_one_transaction_and_freezes_the_visit`, `…Finalize_failure_rolls_back_everything`, `…Orthodontic_initial_terms_are_stored_and_validated_at_finalize`, `Task3AdditionalTests.Finalize_releases_unused_time_without_extending` |
| F3.11 Feedback request → public submission | **PASS** | `…Feedback_request_to_public_submission` (no expiry at 400 days, link replacement, neutral states, Manager-only results, token never logged), `…Sales_entered_feedback_links_the_request_and_general_feedback_has_none`, `…Concurrent_submissions_produce_one_entry`, `Task3AdditionalTests.Public_feedback_is_rate_limited_per_token_and_per_ip` |
| F3.12 Notifications | **PASS** | `NotificationTimelineTests.Appointment_reminder_is_planned_dispatched_and_read`, `…Retry_requeues_failed_rows_below_the_limit`; referral / feedback enqueue assertions; live Worker run |
| F3.13 Patient timeline | **PASS** | `…Patient_timeline_respects_each_audience` (Doctor clinical only, Sales operational only, Manager both) |
| F3.14 Role / privacy / security matrix | **PASS** | `Task3PrivacyTests` (canaries, cross-Doctor 404, permission matrix, OpenAPI audiences), `Task3BoundaryTests` (architecture), `Task3DatabaseTests` (constraints, grants, frozen negatives, upgrade), `Task3AdditionalTests.Negative_contract_hygiene` |

## 13. Deferred Task 4 seams (in place, not implemented)

| Seam | Task 4 work |
|---|---|
| `IDomainEventHandler<VisitFinalized>` + `VisitFinalizationContext` (per line: kind, work item, case, procedure, lab model, price, discount snapshot) | Finance creates Treatment / Consultation charges in the finalize transaction (FR-FIN-001); `workitem.charge_exists` (422) is already wired into the price / discount commands |
| `IVisitFinalizationGuard` (`OrthoDepositGuard`, `FixedLabInitialGuard`) | stays; G7(b) deferred trigger and G9(c, d) / G10 arrive with `patient_charges` / installments |
| `orthodontics.ortho_cases.charge_id` (nullable, no FK) | composite FK §24 #16 to `finance.patient_charges`; initial installment generation (FR-ORT-004 Task-4 part), Manager terms edit, J2 |
| `fixed_lab_revisions` revision > 1 (Manager role CHECK already present) | `Finance.FixedLab.Revise` and Lab-First distribution |
| `SalesClinicalSummary.paid / remaining / orthoInstallments` = null; patient credit "غير متاح بعد" | Finance read ports |
| Notification type `OrthodonticInstallmentDueTomorrow` | J2 planner in Orthodontics |

## 14. Open requirements not invented

No business question remained open. Technical interpretations are listed in §11. The only deferred items are those of the frozen baseline:

- **Q-ORT-3b:** an ortho total decrease is rejected with `ortho.total_decrease_not_supported`; the database G9(b) backstop is already in place.
- **OPS-1:** neither blocks Task 3.

## 15. Repository / secret hygiene — PASS

- No password, connection secret or token appears in source, configuration, tests or this report. Test and smoke runs used pgpass and `Options=-c role=…`.
- Throwaway databases (`clinic_test_*`) were dropped.
- No build output (`bin`, `obj`, `dist`, `node_modules`) is tracked.
- The git index is empty: nothing staged, nothing committed, nothing pushed, no tags.

## 16. Blockers

None.

## 17. Final decision

**TASK 3 COMPLETE**
