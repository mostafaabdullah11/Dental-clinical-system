# Task 2 — Final Audit

**Scope:** Foundation, Auth, Users / Staff, Patients, Medical History, CRM, and the Task-2 portions of OPS, SEC, AUDIT and NOTIF.
**Method:** the implementation itself was audited: routes, permission seeds, migrations, grants, DTOs, tests and frontend. Step reports were cross-checked, not trusted.

## 1. Status

**PASS — Task 2 complete.**

The audit found one genuine Task-2 gap and fixed it: **FR-PAT-010 / AC-PAT-010**, patient timeline routes (§6).

It also added tests for Task-2 clauses that had no direct assertion:

- AC-AUDIT-002 / -003
- AC-NEG-031
- AC-NEG-006
- AC-STAFF-010 ("redistributed work stays")

All gates pass after the fixes. Nothing has been committed or pushed.

## 2. Scope audited

Derived mechanically from the `Task` column of TRACEABILITY_MATRIX.md and the `**Task:**` field of ACCEPTANCE_CRITERIA.md:

| | Task 2 only | Task 2 + later Task | Total |
|---|---|---|---|
| FRs | 46 | 28 | **74** |
| ACs | 49 | 39 | **88** |

For ACs that span later Tasks, only the Task-2 portion was verified; the later dependency is preserved (§13). The matrix assigns FR-STAFF-007, -008 and -012..-017 to Task 4; they stay deferred.

## 3. Build / test results

| Gate | Result |
|---|---|
| `dotnet build Clinic.sln` | 0 warnings, 0 errors (TreatWarningsAsErrors) |
| `dotnet test --solution` | **167 passed, 0 failed**: 24 architecture and 143 integration on real PostgreSQL 18, throwaway `clinic_test_*` databases, pgpass credentials |
| Previous baseline | 162. The audit added 5 tests (TimelineTests, AuditCoverageTests, 3 UnconfirmedProposalTests) and extended 3 |
| `npm run typecheck` / `npm run build` | clean / built |
| Real-process HTTPS smoke test (CRM and timelines) | **25 of 25 checks**. Log scan: 0 canary or phone hits, 0 error entries |
| Leftover test databases | 0 |

## 4. Migration / DB gate

- **Migration history (verified, not assumed):** **15** migrations. They are the 14 reported in Step 2.3 plus `Identity_TimelinePermissions` from this audit. All apply from zero in every test run and through `dotnet ef database update` as `clinic_migrator`.
- **Schemas, and nothing else:** `audit`, `configuration`, `crm`, `identity`, `infra`, `notifications`, `patients`, `staff`. Every table is owned by `clinic_migrator`.
- **`clinic_app`:**
  - no DELETE or TRUNCATE anywhere;
  - column-restricted UPDATE;
  - insert-only: audit log, commission-rate history, responsibility history, contact attempts;
  - no UPDATE on Original Sales, patient source or `patient_code`.
- **`clinic_worker`:** reads only:
  - `identity.v_active_user_roles` and `staff.v_sales_employee_activity` (no salary, commission or profile data);
  - `configuration.settings`;
  - follow-up columns without notes or result;
  - lead columns without name or phone.

  It has insert / lifecycle-update on notifications, and no access to `patients.*` (contacts and intake included). Proven by grant tests and by running the J3 planner and J7 probe under the worker role.
- **`clinic_backup`:** SELECT only, and none on `infra.data_protection_keys`.
- **No premature tables:** no scheduling, clinical, prescription, referral, finance, lab, ortho, payroll, expense, attendance or feedback table or schema. Future follow-up source columns are nullable with no FK.

## 5. FR coverage (74 Task-2 FRs)

| Class | FRs |
|---|---|
| **Fully implemented (Task-2 portion)** | FR-OPS-001..006 and -009 (Task-2 portion); FR-AUTH-001..010; FR-STAFF-001..006, -009..-011 (Task-2 portion); FR-PAT-001..005, -007, -009; FR-MED-001..006, -008; FR-CRM-001..012; FR-NOTIF-005 (Task-2 part); FR-SEC-001..007, -009, -011, -012 (Task-2 portion); FR-AUDIT-001..006, -008 (Task-2 portion) |
| **Implemented; part waits for a later module** | FR-PAT-006: Patient Credit figure (Finance, Task 4). FR-PAT-008: Doctor write needing an editable Visit (Clinical, Task 3). FR-PAT-010: visit / treatment / prescription / appointment / payment timeline sources (Tasks 3–4). FR-MED-007: booking / arrival / Visit start (Task 3) |
| **Missing** | none. FR-PAT-010 routes were missing and are now fixed |
| **Contradictory** | none |

## 6. AC coverage (88 Task-2 ACs)

- **Fully passing:** every Task-2-only AC except the four listed below, and the Task-2 portion of every spanning AC.
- **Partly passing, only because a later module does not exist yet:**
  - AC-PAT-006: credit figure is `null`, never 0.
  - AC-PAT-008 (a): Doctor write with an editable Visit.
  - AC-PAT-010: non-CRM timeline sources.
  - AC-MED-007: full booking → arrival → Visit flow.
- **Failing:** none.
- **Before the audit, 11 of the 88 ACs were not referenced by any test:**
  - **Covered by new tests:** AC-PAT-010 (`TimelineTests`), AC-AUDIT-002 / -003 (`AuditCoverageTests`), AC-NEG-031 / -006 (`UnconfirmedProposalTests`).
  - **Task-2 portion already covered by existing tests under other IDs:** AC-SEC-004 (Sales patient whitelist: PAT / MED tests), AC-SEC-005 (scoping: CRM-002 / -008 tests), AC-SEC-011 (Sales on Manager staff / finance routes → 403; no Doctor dashboard or notification route), AC-AUDIT-006 (insert-only grants: DB gate and CRM tests), AC-AUDIT-008 (assignment history, responsibility history, Original Sales tests), AC-NEG-039 (Sales → commission-history 403; percentage properties absent from the Sales OpenAPI).

**Defect fixed — FR-PAT-010 / AC-PAT-010 (Task 2 in the matrix).** Step 2.2 had built only unused contract interfaces and reported the requirement as "seam only".

- **Routes:** `GET /api/{doctor,sales,manager}/patients/{id}/timeline`.
- **Permission keys:** `Patients.Timeline.Clinical` (DOCTOR, MANAGER) and `Patients.Timeline.Operational` (SALES_RECEPTION, MANAGER), exactly as in SECURITY_DESIGN.md §4. The Manager route requires both.
- **Contract:** one audience-aware source contract (`IPatientTimelineSource`); CRM supplies the follow-up entries.
- **Projections:**
  - The Doctor sees follow-ups as **type, due date and status only** (SECURITY_DESIGN.md §7).
  - Lead-stage follow-ups stay on the Lead, so no Lead data or source reaches a Doctor.
  - Sales and Manager entries carry the reference, and never notes or attempts.
- **Frontend:** the timeline appears on the patient page for each audience.
- **Tests:** cross-audience 403s, 404s and canaries.

## 7. Auth / Staff result — PASS

- **Roles and accounts:**
  - CHECK `ck_roles_name` allows exactly MANAGER / DOCTOR / SALES_RECEPTION; the seed matches.
  - One role per user (unique `user_roles.user_id`).
  - Doctor / Sales staff rows have unique NOT NULL `user_id`, and `RoleStaffFit` binds role to staff kind.
  - Assistants have no user column or FK. There is no patient account column or patient / assistant audience.
- **Auth:** CLI bootstrap with no default credentials; login with an enumeration-resistant generic error; `__Host-` cookie; idle / absolute expiry; CSRF; rate limit; lockout; logout; `/api/me`; PBKDF2; Manager account creation; Manager-set password (stamp rotation); force sign-out; no reset routes; break-glass is CLI-only, not HTTP. All covered by AC-AUTH-001..010 tests.
- **Staff:**
  - Doctor / Sales creation with linked accounts, and commission required on Sales creation (revision 1).
  - Deactivation rotates the stamp and disables login. Reactivation keeps the lockout until the Manager sets a new password.
  - No DELETE routes.
  - Sales deactivation runs CRM redistribution in the same transaction.
  - Reactivation leaves redistributed work with the new owners (new assertion).
- **Observation A (role change): PASS.** FR-AUTH-008 says "refused if the target's staff row does not fit the new role". The literal implementation means cross-role changes are refused (422) and the same role is a no-op. No staff records are fabricated.
- **Observation C (change own password): PASS.** FR-AUTH-007 / AC-AUTH-007 require no session invalidation, so no rule was added.

## 8. Patient / Medical result — PASS

- **Walk-In:** `WalkIn`, Original Sales NULL (CHECK); the registrar is recorded only as `created_by`. DOB or recorded age, with no DOB inference. No medical fields at registration.
- **Lead:** conversion is the only path to `Lead` attribution; an Original Sales or source member on any patient route → 400.
- **Storage separation:** contacts live in `patients.patient_contacts`; Doctor code uses only `IPatientClinicalReader` (architecture test).
- **Search and edits:** search is audience-specific (Doctor by code / name only, rate-limited). Identity and contact edits are separate, with ETags. Reassignment keeps history, one current Doctor (partial unique index), If-Match, a reason, and no financial effect.
- **Observation B (Manager chief complaint): PASS.** The editable-Visit precondition in FR-PAT-008 is stated for the Doctor; the Manager holds `Patients.Clinical.Write`.
- **Observation E (first assignment with previous Doctor NULL): PASS.** The column is nullable by design and no rule requires a previous Doctor.
- **Medical History:**
  - exactly the confirmed intake fields; tri-state nullable booleans, where NULL is never FALSE;
  - last dental visit ≤ 200 characters;
  - first PUT creates, later PUTs require an ETag;
  - Doctor read-only; Sales / Manager write;
  - audit stores field names only (re-verified by `AuditCoverageTests`);
  - no values in logs, audit or errors; the Worker is denied access.

## 9. CRM result — PASS

- **Statuses and Owner decisions:**
  - Exactly the 8 Lead statuses; Owner classification seeded (closed = Converted, NotInterested); a converted lead is always closed.
  - All 8 statuses are settable by update (AC-CRM-001 b), with no invented transition rule. Conversion sets Converted.
  - Manager-created items name an active Sales employee (Owner decision), consistent with FRS / AC. A Sales caller naming an owner → 400.
- **Leads:** Original Sales is frozen (no member, no grant); Responsible Sales is operational. My Leads is principal-scoped; Manager All Leads has `unassigned=true`; there is no `/api/unassigned/*`.
- **Conversion:** ETag; locked and atomic. Concurrent conversion yields one patient. No commission recognition.
- **Follow-ups:**
  - Exactly 6 types and 4 statuses; one canonical open predicate (`CrmPredicates`) used by lists, redistribution, J3 and J7.
  - Patient or lead required; at most one source (CHECK); no automatic creation.
  - Attempts are append-only (no route, no grant). Own items only; another employee's item → 404; unassigned items are hidden from Sales.
- **Observation D (converted lead status edit): PASS.** It stays closed by `converted_patient_id`, and no frozen rule forbids the edit.

## 10. Privacy / security result — PASS

- **Doctor:**
  - 403 on all 14 CRM routes and the operational / Manager timelines.
  - Zero hits for contact, CRM, Original Sales, source, responsibility, follow-up note and attempt canaries in Doctor JSON (timeline included), the Doctor OpenAPI document, error bodies and logs.
  - No Crm key mapped to DOCTOR.
- **Sales:** no clinical canary; no percentage / share / lab properties in the Sales OpenAPI; 403 on Manager staff / finance routes.
- **No Assistant or Patient principal or audience:** the API documents are exactly auth / doctor / sales / manager.
- **Defence in depth:** permission keys, then scoped 404, then audience DTOs, then architecture tests, then the log scrubber. Frontend hiding is never the control.
- **Errors:** RFC 7807 with `traceId` and stable codes; no stack traces, SQL or echoed PII (OPS / SEC tests).

## 11. Redistribution atomicity result — PASS

- The handler runs inside the Staff deactivation transaction and locks rows `FOR UPDATE`. It moves only open, unconverted Leads and open Follow-Ups.
- Assignment is round-robin by creation time, starting with the fewest-open employee. With 7 items over S2 / S3 holding 2 / 0: S3, S2, S3, S2, S3, S2, S3, giving final counts 5 and 4.
- One history row per item with a shared batch id and reason `SalesDeactivated`.
- Last-Sales case: NULL owner, history `to = NULL`, deactivation succeeds.
- A forced failure rolls everything back.
- Original Sales never changes, and no commission transfer exists.

## 12. Notification Task-2 boundary

- **Implemented:** Notifications module and contracts; durable queue with a dedupe key; CRM J3 planner (open, due ≤ clinic-local today, active owner; RemainingBalance → OutstandingPaymentFollowUp, others → PendingFollowUp; none for NULL or inactive owners; a due-date change re-notifies; redistributed items target the new owner); CRM J7 counts. No contact or clinical values are stored.
- **Not implemented, and not claimed:** J4 dispatcher, retry lifecycle, inbox, Hangfire recurring registration, Appointment / Ortho planners, Referral events. These belong to Task 3. **No delivery is claimed.**

## 13. Deferred dependency scenarios (not Task-2 blockers; seams in place and documented)

| Scenario | Waits for |
|---|---|
| AC-PAT-006 credit figure | Finance (T4) |
| AC-PAT-008 (a) Doctor chief-complaint write with an editable Visit | Clinical (T3) |
| AC-PAT-010 visit / treatment / prescription / appointment / payment entries | T3 / T4 sources plugging into `IPatientTimelineSource` |
| AC-MED-007 booking → arrival → Visit | Scheduling / Clinical (T3) |
| AC-CRM-005 (e), AC-CRM-006 (b) dashboard, "no commission transferred", AC-STAFF-010 "no commission during inactivity" | Finance / Payroll / Reports (T4) |
| AC-NOTIF-005 delivery; J3 / J7 scheduling | T3 |
| T3–T5 parts of SEC / AUDIT / NEG ACs (prints, exports, notifications, reports, finance canaries) | T3–T5 |

## 14. CI / generated-client classification

Neither generated TypeScript clients nor a containerised-PostgreSQL CI pipeline is required by any Task-2 FR or AC; a search of the Task-2 FR / AC set found no such requirement. Both are classified as **technical delivery / CI work for Task 5**.

Local PostgreSQL integration tests exercise every Task-2 behaviour, and the OpenAPI property scans are tested.

## 15. Repository / secret hygiene — PASS

- **Commits:** none since Task 1 (`440b384`). No tracked file is modified; all Task-2 work is untracked.
- **Ignored:** `bin/`, `obj/`, `node_modules/` and `dist/`.
- **No secret material:** no pgpass, `.env`, certificate, key or API key in the tree.
  - `.env.example` holds only `change-me-*` placeholders.
  - `deploy/db/*.sql` use `{{…}}` placeholders.
  - Tests generate random role passwords and read the admin connection from pgpass; the PostgreSQL password was never printed.
  - The smoke DBs were dropped.

## 16. Files changed by the audit

- **Patients:**
  - `Contracts/PatientContracts.cs`: the timeline contract `IPatientTimelineSource` replaces two unused interfaces.
  - `Features/Doctor/DoctorPatientEndpoints.cs`, `Features/Sales/SalesPatientEndpoints.cs`, `Features/Manager/ManagerPatientEndpoints.cs`: timeline routes.
- **CRM:** `Infrastructure/CrmJobs.cs` (`CrmTimelineSource`), `CrmModule.cs`.
- **SharedKernel:** `Security/PermissionKeys.cs` (2 timeline constants).
- **Persistence:** migration `*_Identity_TimelinePermissions` and `Sql/Identity_TimelinePermissions.up.sql`.
- **SPA:** `components/PatientTimeline.tsx`, `pages/PatientPage.tsx`.
- **Tests:**
  - New: `Patients/TimelineTests.cs`, `Platform/AuditCoverageTests.cs`, `ArchitectureTests/UnconfirmedProposalTests.cs`.
  - Extended: `Crm/RedistributionTests.cs`, `Crm/CrmPrivacyAndDatabaseTests.cs`, `Platform/DatabaseGateTests.cs`, `Auth/SessionTests.cs` (Doctor keys now include `Patients.Timeline.Clinical`).
- This report.

**Step-report claims corrected by this audit:**

- Step 2.2 called FR-PAT-010 "composition seam only / dependency-bound". The routes and keys were Task-2 work and are now implemented.
- Step 2.3's "14 migrations / 162 tests" is superseded by 15 / 167.

No other step-report claim was contradicted by the code, migrations, seeds, routes, grants or tests.

## 17. Blockers

None.

## 18. Final decision

**TASK 2 COMPLETE.** All Task-2 implementation work is done, and every Task-2 acceptance scenario that does not depend on a later module passes. Dependency-bound scenarios are listed in §13 with their seams.

Nothing has been committed, pushed or started for Task 3.
