# Task 2 / Step 2.3 — CRM: Report

**Status:** DRAFT COMPLETE (local, uncommitted). Task 2 is **not** marked complete; the Task-2 final audit is next.
**Scope:** Leads, Follow-Ups, Lead → Patient conversion, Manager responsibility assignment, Sales-deactivation redistribution, the minimal Notifications foundation with the CRM J3 planner, Arabic RTL CRM screens.

## 1. Implemented capabilities

- **CRM module** (`Clinic.Modules.Crm`, level L4): uses lower modules through their Contracts only (Patients, Staff, Identity, Configuration, Notifications). Schema `crm`:
  - `crm.lead_statuses`: catalogue of exactly the 8 frozen statuses, each with its open / closed flag (seed data).
  - `crm.leads`, `crm.follow_ups`, `crm.follow_up_contact_attempts` (insert-only), `crm.responsibility_history` (insert-only).
- **Leads (FR-CRM-001):**
  - Sales creates a Lead and becomes both Original and Responsible Sales.
  - Status must be one of the 8 at three layers: validator, catalogue FK, frontend list.
  - Updates require If-Match: missing → 428, stale → 412.
  - Attribution, responsibility and conversion fields are not update members, so sending one is an unknown member → 400. The DB also has no UPDATE grant on `original_sales_employee_id`.
- **My Leads (FR-CRM-002):** `GET /api/sales/leads/my`, scoped only by the principal's Sales staff link. Query parameters cannot widen it. Another employee's lead, or an unassigned one, returns a scoped 404.
- **All Leads (FR-CRM-003):** `GET /api/manager/leads` with `unassigned=true` and `open=true` filters. Sales gets 403. There is no `/api/unassigned/*` route.
- **Conversion (FR-CRM-004, completes FR-PAT-002):** `POST /api/sales/leads/{id}/convert`. One transaction:
  1. Locks the Lead row (`SELECT … FOR UPDATE`).
  2. Refuses an already-converted Lead (409 `already_converted`).
  3. Requires If-Match.
  4. Creates the patient through `IPatientRegistration.AddLeadSourcedPatientAsync`, copying the Lead's **Original** Sales once.
  5. Sets `converted_patient_id` and status `Converted`.
  6. Writes audit `crm.lead.converted`.

  No commission or ledger row is created. The request has no Original Sales member.
- **Follow-Ups (FR-CRM-005..008):**
  - Exactly 6 types and 4 statuses.
  - Open = `status <> 'FollowedUp'`, one predicate used everywhere (`CrmPredicates`).
  - Validation: patient or lead required; at most one source reference (DB CHECK); If-Match on update.
  - Contact attempts are append-only: there is no update or delete route and no UPDATE/DELETE grant.
  - Sales sees only its own follow-ups; another employee's or an unassigned one returns a scoped 404.
  - Nothing creates follow-ups automatically.
- **Manager assignment (FR-CRM-009):**
  - `POST /api/manager/leads/{id}/reassign` and `POST /api/manager/follow-ups/{id}/assign`, both with If-Match. The target must be an active Sales employee.
  - A `responsibility_history` row is written in the same transaction: `ManagerReassigned`, or `ManagerAssignedUnassigned` when the item had no owner. A+ audit `crm.responsibility.changed`.
- **Sales deactivation (FR-CRM-010 / FR-CRM-011):** the `SalesEmployeeDeactivated` handler runs **inside the Staff deactivation transaction**:
  - It locks the employee's leads and follow-ups and selects only **open** items.
  - It orders the batch by creation time and assigns round-robin, starting with the active employee who has the fewest open items.
  - It writes one history row per item with reason `SalesDeactivated` and a shared batch id, plus A+ audit `crm.responsibility.redistributed`.
  - With no other active Sales employee, items become unassigned (NULL, history `to = NULL`) and the deactivation still succeeds.
  - Any failure rolls back everything.
- **Original Sales permanence (FR-CRM-012):** reassignment, redistribution, unassignment and later assignment change only the responsible / assigned column. No UPDATE grant exists on `crm.leads.original_sales_employee_id`, `patient_contacts.original_sales_employee_id` or `patient_source`. A trigger makes `converted_patient_id` write-once.
- **Notifications foundation (`Clinic.Modules.Notifications`, level L2):**
  - Table `notifications.notifications` holds ids and a dedupe key only (no name, phone or clinical value). CHECKs cover the 7 frozen types, the 4 statuses, the retry count and the error length.
  - `INotificationQueue` does a durable enqueue with `ON CONFLICT (dedupe_key) DO NOTHING`. The key is `{type}:{entity}:{targetDate}:{recipient}`.
  - `IOperationsHealthProbe` is the J7 port.
- **CRM J3 planner (`IFollowUpDuePlanner`):**
  - Selects open follow-ups with `due_date ≤ clinic-local today` and an active owner.
  - `RemainingBalance` → Outstanding Payment Follow-Up; every other type → Pending Follow-Up.
  - NULL or inactive owners are skipped. The planner is idempotent, and it never reads notes, so it runs under `clinic_worker`'s column grant.
- **CRM J7 probe:** reports open follow-ups owned by inactive Sales (should be 0), plus unassigned open leads and follow-ups. Counts only.
- **Frontend (Arabic RTL only):**
  - My Leads / All Leads with the Unassigned and Open filters and reassignment.
  - Create Lead (the Manager picks an active Sales owner).
  - Lead detail: edit, plus Convert to Patient with the DOB / recorded-age rule. Original Sales is shown, not chosen, and Medical History is not part of conversion.
  - Own / All Follow-Ups with the Unassigned filter and assign / reassign; create a follow-up with patient search.
  - Follow-up detail: edit, append-only attempt history, add attempt.
  - Exact frozen labels for statuses and types. Nav entries appear by permission.

## 2. FR IDs

- **Fully implemented:** FR-CRM-001, -002, -003, -004, -005, -006, -007, -008, -009, -010, -011, -012; **FR-PAT-002** (completed end to end).
- **Extended:**
  - FR-STAFF-009: Sales deactivation now redistributes in the same transaction.
  - FR-NOTIF-005: the J3 planner plus durable enqueue (see §8).
  - FR-SEC-002 / -006 / -007: CRM canaries, OpenAPI scans and log scans.

## 3. Acceptance criteria

**Fully passing:** AC-CRM-001, -002, -003, -004, -005, -006, -007, -008, -009, -010, -011, -012; **AC-PAT-002 (a)–(d)**, with (a) now through the real conversion route. Test map:

| AC | Tests |
|---|---|
| AC-CRM-001 | `LeadTests.Sales_creates_a_lead_and_every_one_of_the_eight_statuses_is_accepted`, `…Lead_update_requires_if_match_and_original_sales_is_immutable`; (e) `CrmPrivacyAndDatabaseTests.Doctors_never_receive_crm_data` |
| AC-CRM-002 | `LeadTests.My_leads_is_scoped_by_the_principal` |
| AC-CRM-003 | `LeadTests.Manager_sees_all_leads_and_the_unassigned_filter` |
| AC-CRM-004 / AC-PAT-002 | `LeadTests.Conversion_creates_a_lead_sourced_patient_with_the_leads_original_sales`, `…Concurrent_conversions_create_exactly_one_patient`, `…Only_conversion_can_create_a_patient_with_original_sales`; plus Step 2.2 `RegistrationTests` (b), (c) |
| AC-CRM-005 | `FollowUpTests.Follow_ups_are_created_for_each_of_the_six_types_and_validated` |
| AC-CRM-006 | `FollowUpTests.Follow_up_statuses_and_the_open_predicate`; DB CHECK in `Crm_database_gate` |
| AC-CRM-007 | `FollowUpTests.Contact_attempts_are_append_only` |
| AC-CRM-008 | `FollowUpTests.Sales_sees_only_own_follow_ups` |
| AC-CRM-009 | `FollowUpTests.Manager_assignment_writes_responsibility_history_and_keeps_original_sales` |
| AC-CRM-010 | `RedistributionTests.Deactivation_redistributes_only_open_work_evenly_in_the_same_transaction` |
| AC-CRM-011 | `RedistributionTests.Last_active_sales_deactivation_leaves_open_work_unassigned_for_the_manager` |
| AC-CRM-012 | conversion, reassignment and redistribution tests above; DB grants |
| Atomicity | `RedistributionTests.A_failure_after_redistribution_rolls_back_the_whole_deactivation` |
| AC-NOTIF-005 (Task 2 part) | `FollowUpTests.Due_follow_up_planner_enqueues_for_the_active_owner_only`, `CrmPrivacyAndDatabaseTests.Worker_role_runs_the_crm_planner_and_probe_with_least_privilege` |

**Dependency-bound sub-scenarios** (the behaviour is in place and tested as far as the dependency allows; re-run in the named task):

| Scenario | What passes now | Waits for |
|---|---|---|
| AC-CRM-005 (e): an outstanding Charge or a due installment creates no follow-up | No code path creates follow-ups. Asserted: none exist without a creator. | Finance / Ortho, Task 4 (re-check that those modules do not create follow-ups) |
| AC-CRM-006 (b): the dashboard counts use the same predicate | Lists, redistribution, J3 and J7 all use `CrmPredicates` | Sales dashboard, Task 4 |
| AC-CRM-010 / -012: "no commission is transferred" | Original Sales is unchanged everywhere; no commission object exists | Commission items, Task 4 (AC-PAYROLL-005) |
| AC-CRM-010 / -011 / AC-NOTIF-005: notification *delivery* to the (new) owner | Enqueue to the correct active owner, deduplicated, re-targeted after redistribution, none for unassigned or inactive owners | J4 dispatcher, inbox, Hangfire scheduling of J3 / J7 in the Worker, Task 3 |

**Delivery is not claimed.** Only durable enqueue exists, so no test asserts delivery.

## 4. Owner decisions recorded in this step

Asked during Step 2.3 because the frozen sources leave them open:

1. **Lead open / closed classification** (catalogue seed): **closed = Converted, NotInterested**. New, Contacted, Interested, CallLater, NoAnswer and Booked are open. A lead with `converted_patient_id` is always closed.
2. **Conversion sets status Converted automatically.** All 8 statuses remain settable by a normal update, because AC-CRM-001 (b) requires it. An early draft blocked manual `Converted` and coupled status to `converted_patient_id`; it was **removed** because it contradicted AC-CRM-001 (b) and would have been an invented transition rule.
3. **Manager-created Lead / Follow-Up:** the Manager names an active Sales employee, who becomes Original and Responsible Sales for a Lead. A Sales caller sending `salesEmployeeId` → 400 `not_allowed`; a Manager omitting it → 400 `required`.

These should be folded into the next controlled baseline revision.

## 5. Migrations and DB gate

New migrations, in order:

| Migration | Content |
|---|---|
| `Notifications_Initial` | `notifications` schema, table, CHECKs, unique dedupe index. Grants: app SELECT / INSERT; worker SELECT / INSERT / UPDATE(status, sent_at, retry_count, last_error); backup SELECT |
| `Crm_Initial` | `crm` schema, 5 tables, CHECKs, partial index on open follow-ups. 8-status catalogue seed. Write-once trigger on `converted_patient_id`. Column-restricted grants with no DELETE anywhere; worker gets column-level SELECT without notes, result, name or phone |
| `Xref_CrmForeignKeys` | 13 cross-schema FKs (crm / notifications → identity, staff, patients), all `NO ACTION` |
| `Staff_SalesActivityUserId` | `staff.v_sales_employee_activity` gains `user_id` (appended column); SELECT for `clinic_app` |
| `Identity_CrmPermissions` | 5 CRM keys mapped to SALES_RECEPTION / MANAGER exactly as SECURITY_DESIGN.md §4; none to DOCTOR |

- **From zero:** all 14 migrations apply to an empty PostgreSQL database, in every test run (throwaway `clinic_test_*`) and through `dotnet ef database update` as `clinic_migrator` for the smoke DB.
- **Gate assertions:**
  - Schemas, tables, cross-FK list and migration names: `DatabaseGateTests`, updated.
  - CRM constraints, unique `converted_patient_id`, the unique dedupe key, exact status / type values in the CHECKs, nullability (`responsible` nullable, `original` not), the insert-only history tables, no DELETE / TRUNCATE for `clinic_app`.
  - No referral, scheduling, clinical, finance, payroll, lab, ortho, expense, attendance or feedback schema or table.
- **Not created:** no Referral, Appointment, Installment, Charge or Payroll table. The follow-up source columns `referral_id`, `appointment_id`, `installment_id` and `charge_id` are nullable with **no FK**; their owning steps add the xrefs.

## 6. Build and test results

- `dotnet build Clinic.sln`: **0 warnings, 0 errors** (TreatWarningsAsErrors).
- `dotnet test --solution Clinic.sln`: **162 passed, 0 failed.** That is 21 architecture and 141 integration tests on real PostgreSQL, with throwaway `clinic_test_*` databases, pgpass credentials, and no password printed.
  - **New in this step: 26.** 5 architecture (`CrmBoundaryTests`) and 21 integration (`Crm/LeadTests` 7, `FollowUpTests` 6, `RedistributionTests` 3, `CrmPrivacyAndDatabaseTests` 5).
  - **Regression:** all 136 Step 2.1 / 2.2 tests still pass (Auth, Staff, Patients, Medical History, canaries, permissions, audit, DB gate).
- **Frontend:** `npm run typecheck` clean; `npm run build` succeeded (68 modules).
- **Real-process HTTPS smoke test:** **22 of 22 checks passed**, on the real API process (`https://localhost:5081`) against `clinic_test_smoke_crm`.
  - **Lead and conversion:**
    - Sales login, create Lead, My Leads.
    - 428 without If-Match.
    - Convert (patient source `Lead`, Original Sales = S1); second conversion → 409.
  - **Follow-up:** create; two contact attempts recorded.
  - **Manager assignment:** Manager reassigns (Original unchanged); Sales on All Leads → 403.
  - **Deactivation:**
    - Deactivate S1: open lead and follow-up go to S2, the converted lead stays with S1.
    - Deactivate the last Sales employee: 200, and open items become unassigned in the Manager filters.
    - `/api/unassigned/*` → 404.
  - **Doctor:** CRM route → 403; patient view has no CRM canary or Original Sales; Doctor OpenAPI has no CRM path.
  - **Log and cleanup:** the JSON console log had no canary, no phone and no error entries. The smoke DB was dropped afterwards and no `clinic_test_*` database remains.
- **Lead conversion result:** atomic. Two concurrent conversions give exactly one 201 and one 409, and exactly one patient. The Original Sales copied is the Lead's original, even after a responsibility change.
- **Redistribution result:**
  - With S1 owning 7 open items, S2 owning 2 and S3 none, the assignment order is S3, S2, S3, S2, S3, S2, S3. Final open counts are 5 and 4.
  - 7 history rows share one batch id. Closed and converted items stay with S1; Original Sales is unchanged.
  - J7 reports 0 open follow-ups owned by inactive Sales.
  - The last-Sales case and the forced-failure rollback case both pass.
- **Privacy-canary result: zero hits.** The canaries were lead phone, lead notes, follow-up notes, attempt notes, the Original and Responsible Sales names, the `Lead` source value and `Crm.` keys. They were checked against:
  - all 14 CRM routes called by a Doctor (each 403);
  - every Doctor patient route for the converted patient, and `/api/me`;
  - the Doctor OpenAPI document;
  - captured logs.

  CRM entity audit rows are `is_restricted`.

## 7. Technical decisions (no business behaviour changed)

1. **Staff view extended with `user_id`.** J3 must address the owner's user. This is a `CREATE OR REPLACE` with an appended column; it still exposes no salary, commission or contact data. `clinic_app` was granted SELECT so the API resolves active owners through the same directory as the Worker.
2. **`clinic_app` may INSERT notifications.** Event-driven notifications (Task 3) are enqueued in the business transaction. Inbox read-state grants arrive with the inbox.
3. **Locking:** every Lead / Follow-Up mutation, conversion, assignment and redistribution locks its rows `FOR UPDATE` inside a transaction. The xmin ETag catches stale writes, and the unique index plus trigger guard conversion at the DB level.
4. **The Manager uses the same Sales lead / follow-up routes** for create / detail / edit / convert, because the frozen matrix gives the Manager the same keys. Scope checks pass for the Manager.
5. **Lead follow-up ownership:** a Sales employee can attach a Lead follow-up only to a lead they are responsible for (otherwise 400). This applies the resource scope; it is not a new business rule.
6. **Test infrastructure:**
   - `TestDatabase.ExecuteAsync` for committed setup (the existing `TryExecuteAsync` always rolls back).
   - `IsolatedClinic`: a private DB and host, used where the exact set of active Sales employees matters.
   - A `configureServices` hook on the test factory (failing-handler atomicity test).
   - `StaffFixtures.CreateSalesAsync`, extracted for reuse.
7. **Worker host:** not changed. Scheduling J3 / J7 (Hangfire) belongs to the Notifications delivery work in Task 3. A test composes the Worker's module set under `clinic_worker` and runs the planner and probe successfully, which proves the grants are sufficient.

**Observation (no behaviour chosen):** the status of an already-converted Lead can still be edited, and the Lead stays closed whatever its status, by the open predicate. No frozen rule addresses this.

## 8. Notification behaviour: implemented vs deferred

| Implemented and tested now | Deferred (Task 3) |
|---|---|
| Notifications module and contracts boundary, durable queue table, 7-type CHECK, dedupe | J4 dispatcher (Pending → Sent / Failed, retries) |
| J3 planner: open + due ≤ clinic today + active owner; RemainingBalance → OutstandingPaymentFollowUp, others → PendingFollowUp | Hangfire scheduling of J3 / J7 in the Worker |
| No row for NULL or inactive owners; FollowedUp produces nothing; a due-date change produces a new row | In-app inbox routes and UI, read state |
| After redistribution, J3 targets the new active owner | Cancelling pending rows of a deactivated user (dispatcher concern) |
| J7 CRM counts (inactive-owned open follow-ups, unassigned open leads / follow-ups) | J7 freshness / lag / Hangfire metrics |
| Worker least privilege: follow-up columns without notes / result; leads without name / phone | Event-driven types (referral etc.) |

No SMS, WhatsApp or email.

## 9. Files changed

- **New:**
  - `src/Modules/Clinic.Modules.Notifications/`: csproj, Domain, Contracts, Infrastructure (model, queue), module.
  - `src/Modules/Clinic.Modules.Crm/`:
    - csproj, `Contracts/CrmContracts.cs`, `Domain/CrmEntities.cs`
    - `Infrastructure/` (`CrmModel`, `CrmInfrastructure`, `SalesDeactivationRedistribution`, `CrmJobs`)
    - `Features/` (`CrmDtos`, `Sales/SalesCrmEndpoints`, `Manager/ManagerCrmEndpoints`)
    - `CrmModule.cs`
  - 5 migrations: `Notifications_Initial`, `Crm_Initial`, `Xref_CrmForeignKeys`, `Staff_SalesActivityUserId`, `Identity_CrmPermissions`, with 4 SQL files.
  - Tests: `tests/Clinic.IntegrationTests/Crm/*` (5 files), `tests/Clinic.ArchitectureTests/CrmBoundaryTests.cs`.
  - SPA: `api/crm.ts`, `components/SalesEmployeePicker.tsx`, `pages/LeadsPage.tsx`, `LeadPage.tsx`, `FollowUpsPage.tsx`, `FollowUpPage.tsx`.
  - This report.
- **Modified:**
  - SharedKernel: `PermissionKeys` (5 CRM constants).
  - Staff: `IActiveSalesEmployeeDirectory` plus the view projection with `user_id`.
  - Patients: `IPatientDirectory` (`PatientRef`).
  - Persistence: csproj, `ClinicDbContext` scopes, `CrossModuleReferences`, model snapshot.
  - Api: `ApiComposition` (modules and routes).
  - `Clinic.sln`.
  - Tests: `ClinicApiFactory`, `ApiFixture`, `TestDatabase`, `DatabaseGateTests`.
  - SPA: `App.tsx`, `AppShell.tsx`, `messages.ts`.

## 10. Blockers

None.

## 11. Task-2 readiness check (Task 2 requirements only)

| Area | Implementation work remaining in Task 2 |
|---|---|
| Foundation | None functional. |
| Auth | None. |
| Users / Staff | None in Task 2. FR-STAFF-007 / -008 (Assistant salary and overtime-rate routes) and the Doctor % + Lab % check are assigned to Task 4 by the matrix. |
| Patients | None in Task 2. Credit figure (Task 4), Doctor chief-complaint write with an editable Visit (Task 3) and timelines (Tasks 3–4) are dependency-bound by design. |
| Medical History | None in Task 2. AC-MED-007 end to end needs Scheduling / Clinical (Task 3). |
| CRM | None. The dependency-bound sub-scenarios are listed in §3. |

**Non-business items carried from Step 2.2 for the audit to decide:**

- Generating the per-audience TypeScript clients from the OpenAPI documents. The SPA types are hand-written today, and the OpenAPI property scans are tested.
- A CI pipeline with containerised PostgreSQL. Tests currently run against a local server through pgpass.

Neither changes business behaviour.

## 12. Remaining before the Task-2 final audit

1. Owner review of the three Step 2.3 decisions (§4) and the open observations from Steps 2.1 / 2.2 (role change, Manager chief-complaint write, change-own-password sessions) plus §7's converted-lead status observation.
2. Decide whether the two CI / tooling items in §11 are in scope for the Task-2 close or for Task 5.
3. Run the Task-2 final audit: traceability FR → code → test for FR-STAFF / AUTH / PAT / MED / CRM / SEC / OPS / AUDIT, AC coverage, and a re-run of the full suite and migration from zero.
4. Commit and push only on your explicit instruction. Nothing has been committed or pushed.
