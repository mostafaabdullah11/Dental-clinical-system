# Task 2 / Step 2.2 — Patients + Structured Medical History

**Status:** DRAFT COMPLETE for this step. Task 2 is **not** complete. Nothing has been committed or pushed; Step 2.1 is still uncommitted as well.
**Baseline:** Design Baseline 1.1 (frozen).

## 1. Implemented capabilities

| Area | What exists |
|---|---|
| Patients module (L3) | Schema `patients` with four tables. `patients` holds identity and clinical fields only: no contact field, no generic medical text, no sex field. `patient_contacts` holds contact data, `patient_source` and the Original Sales Employee. `patient_medical_histories` holds the intake (0..1 per patient). `patient_doctor_assignments` keeps assignment history. The module depends downward only, on the Staff and Configuration contracts. |
| Identity / contact separation | Doctor features use only `IPatientClinicalReader`, which never touches `patient_contacts`. The contact reader, the contact domain types and every `[ContactData]` type live on the Sales / Manager side. Architecture tests enforce this. |
| Walk-In registration | `POST /api/sales/patients` and `POST /api/manager/patients` need both `Patients.Identity.Write` and `Patients.Contact.Write`. Each assigns a `P-nnnnnn` code and `registered_at`, sets source `WalkIn` with no Original Sales Employee, and records the registrar only as `created_by_user_id`. Original Sales, medical, sex and unknown members → 400. A DOB is never inferred from an age; age must be 0..120; a future DOB is rejected. |
| Source invariants | DB CHECK: `WalkIn ⇔ original_sales_employee_id IS NULL` and `Lead ⇔ NOT NULL`. `patient_source`, `original_sales_employee_id` and `patient_code` are frozen because `clinic_app` has no UPDATE grant on them. The Lead-sourced seam `IPatientRegistration.AddLeadSourcedPatientAsync` is not routed; CRM will call it inside its own transaction. |
| Age display | One rule (`AgeDisplays.For`): age is computed from DOB, or shown as recorded with its recorded-on date and never aged forward. The same rule will serve the Doctor, Sales and Manager DTOs and the future print model. `doctor_dob_visible` (default visible) hides the DOB from the Doctor DTO; Sales always sees it. |
| Search | Always a POST body; page size ≤ 50; rate limit 60 per minute per user. Doctors search by code or name only, and a phone number matches nothing. Sales and Manager can also search by phone prefix. The name search uses a trigram index, and LIKE wildcards in input are escaped. |
| Identity / contact edits | Two separate capabilities, each with its own ETag (428 when missing, 412 when stale). Contact changes are audited with previous / new values flagged `is_restricted`. Doctors get 403. |
| Views | **Sales view:** contact data, DOB, source, Original Sales (Lead-sourced only), current Doctor and a credit balance (see §5), with no chief complaint; every view writes an access event. **Doctor view:** no contact data or source; DOB only if visible; every open audited. **Manager view:** the full Sales view plus the chief complaint. |
| Chief complaint | A separate clinical field, never part of the intake and never shown to Sales. A Doctor write needs an editable Visit, supplied through `IEditableVisitPort`; until Clinical exists the port answers "no Visit", so Doctors are refused with 422 `editable_visit_required`. The Manager may write it (see §6). |
| Reassignment | Manager only, with a required reason and `If-Match`. One transaction ends the current row and inserts the successor (previous Doctor, new Doctor, time, Manager, reason). A partial unique index guarantees one current Doctor. Nothing references the assignment table, and no hold exists. |
| Timeline seam | `IClinicalTimelineSource` and `IOperationalTimelineSource` contracts only. There are no routes and no rows (§4). |
| Medical History | Exactly the 14 confirmed questions. The 13 flags are `boolean NULL` with no DEFAULT. `last_dental_visit` is free text ≤ 200 (varchar(200) plus a CHECK). The first PUT creates without `If-Match`; later PUTs need it (428 / 412). GET returns 404 when no intake exists. The Doctor read is read-only (no ETag, no write route, no `EditIntake` key) and audited. Audit stores changed field **names** only; the entity is deliberately not auto-audited. |
| Privacy hardening | **Logging (Serilog):** request bodies are never logged; `[ContactData]` and `[MedicalData]` objects are redacted; phones, `+20` numbers and emails are masked. **OpenAPI:** one document each for doctor, sales, manager and auth, served in Development / Testing only; the Doctor document has no contact property and no intake write. **Canary tests:** run over every Doctor endpoint, the OpenAPI documents, error bodies, audit and logs. |
| Frontend (Arabic RTL) | Patient search (audience-aware), Walk-In registration (DOB or age), Sales / Manager patient page (identity and contact edits, intake editor with tri-state selects, current Doctor), Doctor profile with read-only intake, Manager reassignment with history. |

## 2. FR IDs

- **Fully implemented:** FR-PAT-001, FR-PAT-003, FR-PAT-004, FR-PAT-005, FR-PAT-007, FR-PAT-009; FR-MED-001, -002, -003, -004, -005, -006, -008.
- **Partially implemented / dependency-bound:**
  - **FR-PAT-002:** seam, CHECK, frozen fields and privacy are done; the Lead conversion route arrives with CRM, Step 2.3.
  - **FR-PAT-006:** everything except the Patient Credit figure (Finance, Task 4).
  - **FR-PAT-008:** field, precondition and refusal are done; a Doctor write with an editable Visit arrives with Clinical, Task 3.
  - **FR-PAT-010:** composition seam only; full timelines need Scheduling, Clinical, Finance and CRM.
  - **FR-MED-007:** independence from registration is done; booking, arrival and Visit start arrive in Task 3.
- **Extended in this step:** FR-SEC-002, -003, -006 and -007, for the routes that exist now.

## 3. Acceptance criteria

**Fully passing:**

- **PAT:** AC-PAT-001 (a–f), AC-PAT-003, AC-PAT-004 (a–d, including the rate limit), AC-PAT-005, AC-PAT-007, AC-PAT-009.
- **MED:** AC-MED-001, -002, -003, -004, -005, -006, -008.
- **Negative:** AC-NEG-032 (full).
- **Negative, for every surface that exists now:**
  - AC-NEG-034: Doctor JSON, Doctor OpenAPI, error bodies, logs.
  - AC-NEG-035: chief-complaint canary in Sales JSON and the Sales OpenAPI.
  - AC-NEG-037: audit, logs, error bodies.
  - AC-NEG-040 and AC-NEG-041: no Assistant or Patient audience, and no patient account columns.

**Scenarios deferred only because the dependent module does not exist yet** (to re-run then):

| Scenario | Waits for |
|---|---|
| AC-PAT-002 (a) via `POST /api/sales/leads/{id}/convert` | CRM, Step 2.3. The seam is tested directly. |
| AC-PAT-006: credit balance shows 300.00 | Finance, Task 4. Until then it returns `null` ("not available"), never 0. |
| AC-PAT-008 (a): a Doctor with an editable Visit updates the chief complaint | Clinical, Task 3 (supplies `IEditableVisitPort`). |
| AC-PAT-010: Doctor / Sales / Manager timelines | Scheduling, Clinical, Prescriptions, Finance, CRM. |
| AC-MED-007: book → arrival → Visit start with no intake | Scheduling and Clinical, Task 3. |
| Rest of AC-NEG-034, -035 and -037: printed prescription, CSV exports, notifications, reports, measurements and prescriptions in canary scans | Tasks 3–4. |

## 4. Migrations and DB gate

New migrations, following the DB §2 naming rule:

- `Patients_Initial`: tables, sequence, CHECKs, partial unique index, `pg_trgm` trigram index, phone-prefix index, grants.
- `Xref_PatientsForeignKeys`: 6 cross-schema FKs to identity and staff, all NO ACTION.
- `Identity_PatientsPermissions`: 10 patient keys mapped exactly per SEC §4. No contact key goes to DOCTOR, and DOCTOR never gets `EditIntake`.

**DB gate: PASS.** All 9 migrations apply from zero, both through the test harness and through `dotnet ef database update`, as `clinic_migrator`. Verified:

- 6 schemas and the exact 17 tables.
- The CHECK constraints: source, age pair / range / at-least-one, `last_dental_visit` length.
- UNIQUE constraints and the partial unique index for the current Doctor.
- FKs, with the cross-module list matching `CrossModuleReferences.cs`.
- Intake flags nullable with no default.
- No sex, generic-medical, contact, branch, insurance or imaging column in `patients.patients`.
- No Scheduling, Clinical, CRM, Finance or Payroll tables.
- **Grants:**
  - `clinic_app` has no DELETE and cannot UPDATE the frozen columns.
  - `clinic_worker` cannot read `patient_medical_histories` or `patient_contacts`.
  - `clinic_backup` can read but not write.

## 5. Build and test results

- **Backend:** `dotnet build`: 0 warnings, 0 errors (warnings are errors).
- **Frontend:** `npm run typecheck` OK; `npm run build` OK.
- **Tests:** **136 passed, 0 failed.** That is 16 architecture tests and 120 integration tests on real PostgreSQL 18, with throwaway `clinic_test_*` databases, credentials from your pgpass file, and nothing printed.
  - **New in this step: 51** (8 architecture, 43 integration).
  - All 85 Step 2.1 tests still pass. Five of their assertions were updated only because this slice adds a schema, tables, FKs, migrations, and the Doctor patient keys.
- **Privacy canaries: PASS.**
  - **Contact canaries:** phone, WhatsApp, email, address, CRM notes, the Original Sales name and the source value. Zero hits in Doctor responses, Doctor errors and the Doctor OpenAPI document.
  - **Chief-complaint canary:** zero hits in Sales responses and the Sales OpenAPI document.
  - **Medical canary:** zero hits in audit, logs and error bodies.
  - The log capture is checked to be receiving events, so the log scans are not vacuous.
- **Real-process smoke test (HTTPS):**
  - Walk-In registration and the intake round-trip with Arabic data.
  - A Doctor phone search returns nothing, and the Doctor view has no contact data.
  - The Doctor OpenAPI document has no contact property.
  - The console log is JSON with no contact or medical values.
- **Defect found and fixed:** `AddSerilog` was replacing the static logger, so when a secondary host was disposed, the main host's logs were silently dropped. Each host now owns its logger (`preserveStaticLogger: true`).

## 6. Decisions and observations (no business behavior invented)

1. **Manager chief-complaint write (please confirm).** FR-PAT-008 names the Doctor and the Manager as actors, and the frozen matrix grants the Manager `Patients.Clinical.Write`. The editable-Visit precondition is stated for the Doctor. The Manager is therefore not subject to it; Doctors are refused until Clinical exists.
2. **First assignment.** A new patient has no current Doctor. The first assignment goes through the reassignment route with no previous Doctor. No other assignment step is defined.
3. **Patient Credit:** returns `null` ("not available") until Finance registers its reader. A zero would misstate money.
4. **Duplicate-phone check:** the FRS mentions it but defines no behavior, so nothing was built.
5. **Technical choices:**
   - `patient_code` format `P-` plus 6 digits, from a sequence.
   - `identityVersion` and `contactVersion` ETag tokens in the Sales DTO, because the two edits are separate capabilities.
   - `ManagerPatientDto` nests the Sales view and adds the chief complaint.
   - Intake reads by Sales are not record-access audited, matching FR-AUDIT-002's list (Doctor opens, Sales contact views, Manager views).
6. **Auth rate limit:** 10 requests per minute per IP also covers `/api/auth/csrf` (API A2). The SPA fetches a token only at login and on demand.

## 7. Files changed

- **New:**
  - `src/Modules/Clinic.Modules.Patients/` (domain, contracts, infrastructure, Doctor / Sales / Manager / Shared features)
  - `src/Clinic.SharedKernel/Security/ContactDataAttribute.cs`, `Paging/Page.cs`
  - `src/Clinic.Api/Composition/ClinicLogging.cs`, `ApiDocuments.cs`
  - 3 migrations and 2 SQL files
  - Tests: `tests/Clinic.IntegrationTests/Patients/*` (4 files), `tests/Clinic.ArchitectureTests/PatientBoundaryTests.cs`
  - SPA: `PatientsSearchPage`, `RegisterPatientPage`, `PatientPage`, `components/MedicalIntake`, `lib/patients`
  - This report
- **Modified:**
  - SharedKernel: permission keys, rate-limit policy, field codes.
  - Configuration: DOB visibility, clinic-local today.
  - Staff: `IStaffDirectory`.
  - Persistence: scope, xrefs, snapshot.
  - Api: composition, csproj.
  - `Directory.Packages.props`: Serilog.AspNetCore.
  - Test factory and fixture, `DatabaseGateTests`, `SessionTests`, `ModuleBoundaryTests`.
  - SPA: types, client, App, AppShell.

## 8. Blockers

None.

## 9. Remaining Task 2 work

- **Step 2.3 CRM:**
  - Leads (8 statuses), My Leads / All Leads, conversion through `IPatientRegistration.AddLeadSourcedPatientAsync`.
  - Follow-Ups (6 sources, 4 statuses, contact attempts).
  - Manager assignment.
  - Sales-deactivation redistribution (subscribes to `SalesEmployeeDeactivated`).
  - Re-run AC-PAT-002 end to end.
- **Cross-cutting:** generated per-audience TypeScript clients from the OpenAPI documents (CI), and Testcontainers in CI.
- **Later Tasks:** the dependency-bound scenarios in §3.
