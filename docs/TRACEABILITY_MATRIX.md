# TRACEABILITY_MATRIX.md

**Design Baseline:** 1.1 — OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)
**Specification traced:** docs/FRS.md (TASK 1 / STEP 1.1 — FRS COMPLETE)
**Document status:** TASK 1 / STEP 1.2 — TRACEABILITY MATRIX COMPLETE

---

## 1. Purpose

This matrix connects every functional requirement of docs/FRS.md to its authoritative source, its end-to-end flow, its API capability, its data / schema area, its acceptance-criteria placeholder and the implementation Task that builds it. It exists so that no requirement is lost during implementation. It adds no business behavior: every value is taken from FRS.md (which in turn traces the frozen Design Baseline v1.1 and the explicit post-freeze Owner decisions listed in the FRS header). Acceptance Criteria themselves are written in Step 1.3; this document only allocates their IDs.

## 2. Traceability Rules

```
Frozen Source / Owner Decision
  → FR Requirement (docs/FRS.md)
    → FLOW (FRS §29)
      → API Capability (FR 'Related API Capability')
        → Data Area (FR 'Related Data')
          → Acceptance / Test ID (AC-*, filled in Step 1.3)
```

1. **Source.** Q-IDs, OPS-IDs, `OWN Change n`, `[T]` / `[Tn]` / `[DEFAULT]` markers are copied from the FR text. The module-document reference (BASE, GRD, SEC, FIN, DB, API, OQ, OWN, FRZ) is the section of the frozen package the FR's FRS section was derived from. "Post-freeze Owner decision (FRS header)" marks the six FRs the FRS header lists as overriding an older rule where stated (FR-SCH-008, FR-CRM-001, FR-CRM-005, FR-REPORT-009, FR-EXP-001, FR-EXP-002). `[T]` / `[DEFAULT]` markers are preserved: they are technical interpretations / defaults, not business rules.
2. **FLOW.** FLOW mapping includes every requirement directly referenced by the FRS §29 FLOW table (ranges such as `FR-ORT-014..019` are expanded) plus tightly coupled supporting requirements required to execute, validate, secure or calculate that flow. Direct references must never be omitted. Supporting requirements may be included only where the relationship is explicit from the FRS. A dash (`—`) means the requirement has no meaningful relationship to a named FLOW and is verified independently by its own acceptance criterion. In this revision every FR listed in §4 is a direct §29 reference; no supporting-only mapping has been added.
3. **API / Data.** Copied verbatim from the FR's *Related API Capability* and *Related Data* fields. `N/A` is kept where the FRS says N/A; negative / structural FRs are verified by the negative IDs in §7 instead of an invented endpoint.
4. **Acceptance IDs.** Each FR has exactly one primary ID with the same family and number (`FR-XXX-nnn` → `AC-XXX-nnn`); no ID covers a whole module. Negative / privacy acceptance IDs (`AC-NEG-nnn`, §5 and §7) are added to every FR they verify. Where the FR text cites a frozen test (SEC #, API A11 #, B9 #, FIN #, GRD #) it is listed as *linked frozen tests*; Step 1.3 must cover it.
5. **Task.** 2 = Foundation + Auth + Users + Patients + CRM; 3 = Appointments + Doctor Workflow + Clinical + Referrals + Feedback; 4 = Finance + Labs + Orthodontics + Payroll + Expenses + Attendance; 5 = Integration / Security / Deployment verification. Task 1 (documentation) is never an implementation Task. Every FLOW is additionally verified end-to-end in Task 5 (§4).
6. **Deferred items** remain exactly Q-ORT-3b (V1 rejection traced at FR-ORT-010 / AC-NEG-021) and OPS-1 (FR-OPS-007). No other open item exists.

## 3. Requirement Traceability Matrix

| FR ID | Requirement | Source | FLOW | API / Capability | Data / Schema | Acceptance IDs | Task |
|---|---|---|---|---|---|---|---|
| **§4 Global Functional Rules** | | | | | | | |
| FR-OPS-001 | Arabic-only, right-to-left user interface | Q-UI-1; BASE §3; GRD §9 | — | `GET /api/me`; SPA RTL contract (API A11 #37) | `configuration.settings` (clinic header details in Arabic) | AC-OPS-001, AC-NEG-029 (linked frozen tests: API A11 #37) | 2, 5 |
| FR-OPS-002 | Single clinic, single branch | Q-BRN-1; BASE §3; GRD §9 | — | No branch route (API A9) | No table has `branch_id` (DB test (12)) | AC-OPS-002, AC-NEG-003, AC-NEG-031 | 2, 5 |
| FR-OPS-003 | Configurable clinic timezone and clinic-local calendar dates | BASE §3; GRD §9; [DEFAULT] | — | `GET/PUT /api/manager/settings`; `GET /api/me` (returns clinic timezone) | `configuration.settings` | AC-OPS-003 | 2 |
| FR-OPS-004 | Money representation | BASE §3; GRD §9 | — | API A1 (money as strings), A4, A11 #32 | All money columns | AC-OPS-004, AC-NEG-020 (linked frozen tests: A11 #32) | 2, 4 |
| FR-OPS-005 | Optimistic concurrency and idempotency on writes | BASE §3; GRD §9 | — | API A7 | `xmin` row version; `payments.idempotency_key` (unique) | AC-OPS-005 | 2 |
| FR-OPS-006 | Error responses with stable codes | BASE §3; GRD §9 | — | API A6 (status codes 400/401/403/404/409/412/428/422/429/500) | N/A | AC-OPS-006 | 2 |
| FR-OPS-007 | Encrypted backups and recovery-key custody | OPS-1a; OPS-1; OPS-4; BASE §3; GRD §9 | — | N/A (operations) | Backup dump (key-table data excluded) | AC-OPS-007 | 5 |
| FR-OPS-008 | Post-restore account lockdown | BASE §3; GRD §9 | — | Operator command (SEC §17) | `identity.users` | AC-OPS-008 | 5 |
| FR-OPS-009 | Health endpoints and controlled migrations | BASE §3; GRD §9 | — | `GET /health/live`, `GET /health/ready` | `infra.__EFMigrationsHistory` | AC-OPS-009 | 2, 5 |
| **§5 Authentication and Accounts** | | | | | | | |
| FR-AUTH-001 | Exactly three application roles; Assistant has no account | SEC §2–§4 | — | N/A (seed + invariants; SEC #26, #42, #46) | `identity.roles`, `identity.user_roles`, `staff.assistants` (no `user_id`) | AC-AUTH-001, AC-NEG-004, AC-NEG-040 (linked frozen tests: SEC #26, #42, #46) | 2 |
| FR-AUTH-002 | Manager creates user accounts | Q-ACC-1; SEC §2–§4; [DEFAULT] | — | `POST /api/manager/users` | `identity.users`, `identity.user_roles`, `staff.doctors` / `staff.sales_employees` | AC-AUTH-002 | 2 |
| FR-AUTH-003 | Manager sets a NEW password for a forgotten password | Q-ACC-1; SEC §2–§4 | — | `POST /api/manager/users/{id}/password` | `identity.users` (password_hash, security_stamp, lockout fields) | AC-AUTH-003 | 2 |
| FR-AUTH-004 | No self-service password reset | Q-ACC-1; SEC §2–§4 | — | None (API A9; SEC #13) | No reset-token column | AC-AUTH-004, AC-NEG-018 (linked frozen tests: SEC #13) | 2 |
| FR-AUTH-005 | Login | SEC §2–§4; [DEFAULT] | — | `GET /api/auth/csrf`, `POST /api/auth/login` | `identity.users`, `audit.audit_log` | AC-AUTH-005 | 2 |
| FR-AUTH-006 | Session validity and logout | SEC §2–§4 | — | `POST /api/auth/logout` | `identity.users.security_stamp` | AC-AUTH-006 | 2 |
| FR-AUTH-007 | Change own password | SEC §2–§4 | — | `POST /api/auth/change-password` | `identity.users` | AC-AUTH-007 | 2 |
| FR-AUTH-008 | Manager changes a user's role or forces sign-out | SEC §2–§4 | — | `PUT /api/manager/users/{id}/role`, `POST /api/manager/users/{id}/force-sign-out` | `identity.user_roles` | AC-AUTH-008 | 2 |
| FR-AUTH-009 | Initial Manager and break-glass Manager password | SEC §2–§4 | — | Operator command (SEC §2) | `identity.users` | AC-AUTH-009 | 2, 5 |
| FR-AUTH-010 | Current-user context | SEC §2–§4 | — | `GET /api/me` | `identity.*`, `configuration.settings` | AC-AUTH-010 | 2 |
| **§6 Staff Management** | | | | | | | |
| FR-STAFF-001 | Create Doctor staff record | SEC §4, §8; DB §6 | — | `GET/POST /api/manager/staff/doctors` | `staff.doctors` | AC-STAFF-001 | 2 |
| FR-STAFF-002 | Configure Doctor percentage | SEC §4, §8; DB §6; [T6] | — | `PUT /api/manager/doctors/{id}/percentage` | `staff.doctors.current_percentage`, `clinical.case_work_items.doctor_percentage_snapshot` | AC-STAFF-002 | 2 |
| FR-STAFF-003 | Create Sales / Reception employee with required commission rate | Q-FIN-2; SEC §4, §8; DB §6 | FLOW-10 | `GET/POST /api/manager/staff/sales-employees` | `staff.sales_employees`, `staff.sales_commission_rate_history` | AC-STAFF-003 | 2 |
| FR-STAFF-004 | Change Sales commission rate (future collections only) | Q-COMM-1; SEC §4, §8; DB §6 | FLOW-10 | `PUT /api/manager/sales-employees/{id}/commission`; `GET /api/manager/sales-employees/{id}/commission-history` | `staff.sales_commission_rate_history` | AC-STAFF-004, AC-NEG-024, AC-NEG-039 | 2, 4 |
| FR-STAFF-005 | Change Sales base salary | SEC §4, §8; DB §6 | — | `PUT /api/manager/sales-employees/{id}/base-salary` | `staff.sales_employees.base_monthly_salary`, `payroll.sales_payrolls.base_salary_snapshot` | AC-STAFF-005 | 2, 4 |
| FR-STAFF-006 | Create and edit Assistant staff record | SEC §4, §8; DB §6 | FLOW-11 | `GET/POST /api/manager/staff/assistants`, `PUT /api/manager/staff/assistants/{id}` | `staff.assistants` | AC-STAFF-006, AC-NEG-004, AC-NEG-025, AC-NEG-040 | 2, 4 |
| FR-STAFF-007 | Change Assistant salary | SEC §4, §8; DB §6 | FLOW-11 | `PUT /api/manager/staff/assistants/{id}/salary` | `staff.assistants.current_salary` | AC-STAFF-007 | 4 |
| FR-STAFF-008 | Configure Assistant overtime hourly rate | SEC §4, §8; DB §6 | FLOW-11 | `PUT /api/manager/staff/assistants/{id}/overtime-rate` | `staff.assistants.overtime_hourly_rate` | AC-STAFF-008 | 4 |
| FR-STAFF-009 | Deactivate an employee | SEC §4, §8; DB §6 | FLOW-16 | `POST /api/manager/staff/{id}/deactivate`, `POST /api/manager/staff/assistants/{id}/deactivate` | `staff.*`, `identity.users`, `crm.responsibility_history` | AC-STAFF-009 | 2, 4 |
| FR-STAFF-010 | Reactivate an employee | SEC §4, §8; DB §6 | — | `POST /api/manager/staff/{id}/reactivate`, `POST /api/manager/staff/assistants/{id}/reactivate` | `staff.*`, `identity.users` | AC-STAFF-010 | 2 |
| FR-STAFF-011 | Employees and Manager accounts are never deleted | SEC §4, §8; DB §6 | — | None (no delete route) | `staff.*`, `identity.users` | AC-STAFF-011 | 2 |
| **§7 Patients** | | | | | | | |
| FR-PAT-001 | Register a Walk-In / Direct patient | Q-PAT-1; BASE §4; SEC §4, §7; DB §7 | FLOW-02 | `POST /api/sales/patients`, `POST /api/manager/patients` | `patients.patients`, `patients.patient_contacts` | AC-PAT-001, AC-NEG-015 | 2 |
| FR-PAT-002 | Lead-sourced patient creation (via Lead conversion) | BASE §4; SEC §4, §7; DB §7 | FLOW-01 | `POST /api/sales/leads/{id}/convert` | `patients.patient_contacts`, `crm.leads.converted_patient_id` | AC-PAT-002 | 2 |
| FR-PAT-003 | Patient identity and age display | Q-PRV-1; BASE §4; SEC §4, §7; DB §7; [DEFAULT] | — | Patient DTOs (API A3) | `patients.patients` (date_of_birth, age_years_recorded, age_recorded_on) | AC-PAT-003 | 2 |
| FR-PAT-004 | Patient search by audience | BASE §4; SEC §4, §7; DB §7; [DEFAULT] | — | `POST /api/doctor/patients/search`, `POST /api/sales/patients/search`, `POST /api/manager/patients/search` | `patients.patients`, `patients.patient_contacts` | AC-PAT-004, AC-NEG-034 | 2 |
| FR-PAT-005 | Edit patient identity and contact data | BASE §4; SEC §4, §7; DB §7 | — | `PUT /api/sales/patients/{id}/identity`, `PUT /api/sales/patients/{id}/contact` | `patients.patients`, `patients.patient_contacts` | AC-PAT-005 | 2 |
| FR-PAT-006 | Sales patient view | BASE §4; SEC §4, §7; DB §7 | — | `GET /api/sales/patients/{id}` | `patients.*`, `finance.patient_credit_entries` | AC-PAT-006 | 2 |
| FR-PAT-007 | Doctor patient view (no contact data) | BASE §4; SEC §4, §7; DB §7 | FLOW-03 | `GET /api/doctor/patients/{id}` | `patients.patients` | AC-PAT-007, AC-NEG-034 | 2 |
| FR-PAT-008 | Chief Complaint maintenance | BASE §4; SEC §4, §7; DB §7 | — | `PUT /api/doctor/patients/{id}/clinical-profile` | `patients.patients.chief_complaint` | AC-PAT-008 | 2 |
| FR-PAT-009 | Manager reassigns a patient's Doctor | Q-FIN-9; BASE §4; SEC §4, §7; DB §7 | — | `POST /api/manager/patients/{id}/reassign`, `GET /api/manager/patients/{id}/assignments` | `patients.patient_doctor_assignments` | AC-PAT-009 | 2, 4 |
| FR-PAT-010 | Patient timelines by audience | BASE §4; SEC §4, §7; DB §7 | — | `GET /api/doctor/patients/{id}/timeline`, `GET /api/sales/patients/{id}/timeline`, `GET /api/manager/patients/{id}/timeline` | Multiple modules (read) | AC-PAT-010 | 2 |
| **§8 Structured Medical History** | | | | | | | |
| FR-MED-001 | Structured Patient Medical History Intake content | GRD §4a; DB §7; SEC §4 | — | Intake DTOs (API A3) | `patients.patient_medical_histories` | AC-MED-001, AC-NEG-032 | 2 |
| FR-MED-002 | Sales / Reception collects and edits the intake | GRD §4a; DB §7; SEC §4 | FLOW-02 | `GET/PUT /api/sales/patients/{id}/medical-history` (GET → 404 when no intake exists) | `patients.patient_medical_histories` | AC-MED-002, AC-NEG-037 | 2 |
| FR-MED-003 | Doctor reads the intake (read-only) | GRD §4a; DB §7; SEC §4 | FLOW-03 | `GET /api/doctor/patients/{id}/medical-history` | `patients.patient_medical_histories` | AC-MED-003, AC-NEG-034, AC-NEG-037 | 2 |
| FR-MED-004 | Unanswered is never "No" | GRD §4a; DB §7; SEC §4 | — | API A11 #38 | Flag columns have no DEFAULT and no NOT NULL | AC-MED-004 (linked frozen tests: API A11 #38) | 2 |
| FR-MED-005 | Pregnancy and breastfeeding questions | GRD §4a; DB §7; SEC §4 | — | Intake PUT | `is_pregnant`, `is_breastfeeding` | AC-MED-005 | 2 |
| FR-MED-006 | Intake privacy and audit | GRD §4a; DB §7; SEC §4 | — | N/A | `audit.audit_log` | AC-MED-006, AC-NEG-037 (linked frozen tests: SEC #51) | 2 |
| FR-MED-007 | Intake never blocks booking | GRD §4a; DB §7; SEC §4 | FLOW-02 | N/A | N/A | AC-MED-007 | 2 |
| FR-MED-008 | Intake access grants no other clinical access to Sales | GRD §4a; DB §7; SEC §4 | — | SEC #50 | N/A | AC-MED-008, AC-NEG-035 (linked frozen tests: SEC #50) | 2 |
| **§9 CRM / Leads / Follow-Ups** | | | | | | | |
| FR-CRM-001 | Create and manage Leads | Post-freeze Owner decision (FRS header); DB §12; SEC §4; [T] | FLOW-01 | `POST /api/sales/leads`, `GET/PUT /api/sales/leads/{id}` | `crm.leads` | AC-CRM-001 | 2 |
| FR-CRM-002 | My Leads (Sales) | DB §12; SEC §4 | FLOW-01 | `GET /api/sales/leads/my` | `crm.leads` | AC-CRM-002 | 2 |
| FR-CRM-003 | All Leads (Manager) | DB §12; SEC §4 | — | `GET /api/manager/leads` | `crm.leads` | AC-CRM-003 | 2 |
| FR-CRM-004 | Convert a Lead to a Patient | DB §12; SEC §4 | FLOW-01 | `POST /api/sales/leads/{id}/convert` | `crm.leads`, `patients.*` | AC-CRM-004 | 2 |
| FR-CRM-005 | Create and manage Follow-Ups | Post-freeze Owner decision (FRS header); DB §12; SEC §4 | FLOW-01, FLOW-12 | `GET/POST /api/sales/follow-ups`, `PUT /api/sales/follow-ups/{id}` | `crm.follow_ups` | AC-CRM-005 | 2 |
| FR-CRM-006 | Exact Follow-Up statuses and open predicate | Q-CRM-1; DB §12; SEC §4 | FLOW-01, FLOW-12 | API A11 #15; B9 #9 | `crm.follow_ups.status` | AC-CRM-006 (linked frozen tests: API A11 #15; B9 #9) | 2 |
| FR-CRM-007 | Follow-Up contact attempts | DB §12; SEC §4 | FLOW-01 | `POST /api/sales/follow-ups/{id}/contact-attempts` | `crm.follow_up_contact_attempts` | AC-CRM-007 | 2 |
| FR-CRM-008 | Sales sees only own Follow-Ups | DB §12; SEC §4 | — | `GET /api/sales/follow-ups` | `crm.follow_ups` | AC-CRM-008 | 2 |
| FR-CRM-009 | Manager assigns / reassigns Leads and Follow-Ups | DB §12; SEC §4 | FLOW-16 | `POST /api/manager/leads/{id}/reassign`, `GET /api/manager/follow-ups?unassigned=true`, `POST /api/manager/follow-ups/{id}/assign` | `crm.leads`, `crm.follow_ups`, `crm.responsibility_history` | AC-CRM-009 | 2 |
| FR-CRM-010 | Inactive Sales — even redistribution of open Leads and Follow-Ups | Q-EMP-1; DB §12; SEC §4 | FLOW-16 | `POST /api/manager/staff/{id}/deactivate` | `crm.leads`, `crm.follow_ups`, `crm.responsibility_history` | AC-CRM-010 | 2 |
| FR-CRM-011 | No Active Sales left — items become unassigned | DB §12; SEC §4 | FLOW-16 | As FR-CRM-009 | As FR-CRM-010 | AC-CRM-011 | 2 |
| FR-CRM-012 | Original Sales attribution is permanent | DB §12; SEC §4 | FLOW-16 | N/A | `crm.leads`, `patients.patient_contacts` | AC-CRM-012 | 2 |
| **§10 Scheduling / Appointments / Arrival Queue** | | | | | | | |
| FR-SCH-001 | Manager publishes weekly Doctor availability | DB §8; API A8; SEC §4 | FLOW-03 | `GET/PUT /api/manager/doctors/{id}/availability/{weekStart}`, `POST …/publish` | `scheduling.availability_weeks`, `scheduling.availability_ranges` | AC-SCH-001 | 3 |
| FR-SCH-002 | View availability and schedules | DB §8; API A8; SEC §4 | FLOW-03 | `GET /api/sales/doctors`, `GET /api/sales/availability/slots`, `GET /api/doctor/schedule` | `scheduling.*`, `scheduling.v_effective_booked_ranges` | AC-SCH-002 | 3 |
| FR-SCH-003 | Book an appointment with manual duration | Q-APP-1; DB §8; API A8; SEC §4 | FLOW-02, FLOW-03 | `POST /api/sales/appointments`, `POST /api/manager/appointments` | `scheduling.appointments` | AC-SCH-003 | 3 |
| FR-SCH-004 | Overlapping appointments allowed | Q-APP-1; DB §8; API A8; SEC §4 | FLOW-03 | API A11 #9 | `scheduling.appointments` | AC-SCH-004 (linked frozen tests: API A11 #9) | 3 |
| FR-SCH-005 | Exactly seven appointment statuses | Q-APP-4; DB §8; API A8; SEC §4 | — | API A8 "Appointment statuses and arrival" | `scheduling.appointments.status` | AC-SCH-005, AC-NEG-013, AC-NEG-014 | 3 |
| FR-SCH-006 | Confirm, Cancel, No Show | Q-APP-3; DB §8; API A8; SEC §4 | FLOW-03 | `POST /api/sales/appointments/{id}/status`, `POST /api/manager/appointments/{id}/status` | `scheduling.appointments` | AC-SCH-006 | 3 |
| FR-SCH-007 | Reschedule preserves history | DB §8; API A8; SEC §4 | FLOW-03 | `POST /api/sales/appointments/{id}/reschedule`, `POST /api/manager/appointments/{id}/reschedule` | `scheduling.appointments` (unique `previous_appointment_id`) | AC-SCH-007 | 3 |
| FR-SCH-008 | Doctor creates the next (follow-up) appointment and reschedules it | Post-freeze Owner decision (FRS header); DB §8; API A8; SEC §4 | FLOW-04 | `POST /api/doctor/appointments/follow-up` (body `{ visitId, startAt, durationMinutes, reason }`), `POST /api/doctor/appointments/{id}/reschedule` | `scheduling.appointments` (`doctor_id` = authoring Doctor, `duration_minutes` NOT NULL CHECK 1..480, `created_by_user_id`, `kind`) | AC-SCH-008 | 3 |
| FR-SCH-009 | Record patient arrival (event, not status) | Q-APP-4; DB §8; API A8; SEC §4; [T5] | FLOW-03 | `POST /api/sales/appointments/{id}/arrival` | `scheduling.appointment_arrivals` | AC-SCH-009, AC-NEG-013 | 3 |
| FR-SCH-010 | Void a mistaken arrival | DB §8; API A8; SEC §4 | — | `POST /api/sales/appointments/{id}/arrival/void` | `scheduling.appointment_arrivals` | AC-SCH-010 | 3 |
| FR-SCH-011 | Doctor waiting queue (derived) | DB §8; API A8; SEC §4 | FLOW-03 | `GET /api/doctor/queue` | `scheduling.appointments`, `scheduling.appointment_arrivals`, `clinical.visits` | AC-SCH-011, AC-NEG-014, AC-NEG-034 | 3 |
| FR-SCH-012 | Visit start moves the appointment to In Treatment | DB §8; API A8; SEC §4 | FLOW-03 | `POST /api/doctor/visits` | `scheduling.appointments`, `clinical.visits.appointment_id` (unique) | AC-SCH-012 | 3 |
| FR-SCH-013 | Visit finalization completes the appointment and releases unused booked time | DB §8; API A8; SEC §4 | FLOW-04 | `POST /api/doctor/visits/{id}/finalize` | `scheduling.appointments.visit_ended_at`, `scheduling.v_effective_booked_ranges` | AC-SCH-013 | 3 |
| FR-SCH-014 | Appointment views by audience | DB §8; API A8; SEC §4 | — | `GET /api/doctor/appointments`, `GET /api/sales/appointments`, `GET /api/manager/appointments` | `scheduling.*` | AC-SCH-014, AC-NEG-034 | 3 |
| **§11 Clinical Cases / Visits / Work** | | | | | | | |
| FR-CLN-001 | Start a Visit | BASE §4; DB §9; SEC §4, §6 | FLOW-03 | `POST /api/doctor/visits`, `PUT /api/doctor/visits/{id}` | `clinical.visits` | AC-CLN-001 | 3 |
| FR-CLN-002 | Record treatment: start or continue a treatment case | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `POST /api/doctor/visits/{visitId}/treatments`, `PUT /api/doctor/visits/{visitId}/treatments/{recordId}` | `clinical.treatment_cases`, `clinical.treatment_records` | AC-CLN-002 | 3 |
| FR-CLN-003 | Procedure catalog and option validation | BASE §4; DB §9; SEC §4, §6; [T] | FLOW-04 | API A4 | `clinical.procedure_types`, `clinical.treatment_cases.details` | AC-CLN-003 | 3 |
| FR-CLN-004 | Root canal steps and canal measurements | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `POST /api/doctor/visits/{visitId}/cases/{caseId}/steps/{stepCode}/complete`, `PUT …/canal-measurements` | `clinical.treatment_steps`, `clinical.root_canal_measurements` | AC-CLN-004 | 3 |
| FR-CLN-005 | Implant details | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `PUT /api/doctor/visits/{visitId}/cases/{caseId}/implant` | `clinical.implant_details` | AC-CLN-005, AC-NEG-002 | 3 |
| FR-CLN-006 | Clinical notes on the Visit | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `PUT /api/doctor/visits/{id}` | `clinical.visits.clinical_notes` | AC-CLN-006 | 3 |
| FR-CLN-007 | Doctor enters the initial price of own work | BASE §4; DB §9; SEC §4, §6 | FLOW-04, FLOW-07 | `PUT /api/doctor/work-items/{id}/pricing` | `clinical.case_work_items` | AC-CLN-007, AC-NEG-023 | 3, 4 |
| FR-CLN-008 | Per-Doctor priced work (multiple Doctors in one case) | Q-CLN-2; BASE §4; DB §9; SEC §4, §6 | FLOW-04 | API A8 "Treatments and work items" | `clinical.case_work_items` (unique `treatment_record_id`) | AC-CLN-008, AC-NEG-017 | 3 |
| FR-CLN-009 | Doctor percentage snapshot on work | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | N/A | `case_work_items.doctor_percentage_snapshot`, `patient_charges.doctor_percentage_snapshot` | AC-CLN-009 | 3, 4 |
| FR-CLN-010 | Consultation Fee | BASE §4; DB §9; SEC §4, §6; [T11] | FLOW-04 | `PUT /api/doctor/visits/{id}` | `clinical.visits.consultation_fee` | AC-CLN-010, AC-NEG-023 | 3, 4 |
| FR-CLN-011 | Treatment case lifecycle | BASE §4; DB §9; SEC §4, §6 | — | Treatment routes (API A8) | `clinical.treatment_cases` | AC-CLN-011, AC-NEG-031 | 3 |
| FR-CLN-012 | Finalize a Visit | BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `POST /api/doctor/visits/{id}/finalize` | `clinical.visits`, `finance.patient_charges`, `orthodontics.*`, `scheduling.appointments`, `feedback.feedback_requests` | AC-CLN-012 | 3, 4 |
| FR-CLN-013 | Finalized Visit protection and read breadth | BASE §4; DB §9; SEC §4, §6 | — | `GET /api/doctor/patients/{id}/visits`, `GET /api/doctor/visits/{id}`, `GET /api/doctor/treatment-cases/{id}` | G1 on treatment records, steps, measurements, prescriptions, items, Visit notes | AC-CLN-013, AC-NEG-027 | 3 |
| FR-CLN-014 | Controlled correction of own finalized Visit | Q-CLN-1; BASE §4; DB §9; SEC §4, §6 | FLOW-04 | `POST /api/doctor/visits/{visitId}/corrections` | `clinical.visit_corrections` (composite FK to the Visit's own Doctor) | AC-CLN-014, AC-NEG-027 | 3 |
| FR-CLN-015 | Sales clinical summary (whitelist) | BASE §4; DB §9; SEC §4, §6 | — | `GET /api/sales/patients/{id}/clinical-summary` | Clinical (read via `ISalesClinicalSummary`) | AC-CLN-015, AC-NEG-035 | 3 |
| **§12 Prescriptions** | | | | | | | |
| FR-RX-001 | Write a prescription | BASE §4; DB §10; SEC §10 | FLOW-04 | `POST /api/doctor/visits/{visitId}/prescriptions`, `PUT /api/doctor/prescriptions/{id}` | `prescriptions.prescriptions`, `prescriptions.prescription_items` | AC-RX-001 | 3 |
| FR-RX-002 | View all prescriptions | BASE §4; DB §10; SEC §10 | — | `GET /api/doctor/patients/{id}/prescriptions`, `GET /api/doctor/prescriptions/{id}` | `prescriptions.*` | AC-RX-002, AC-NEG-035 | 3 |
| FR-RX-003 | Print prescription | BASE §4; DB §10; SEC §10 | — | `GET /api/doctor/prescriptions/{id}/print` | Print model (no contact fields) | AC-RX-003, AC-NEG-034 | 3 |
| FR-RX-004 | Controlled correction of own finalized prescription | Q-CLN-1; BASE §4; DB §10; SEC §10 | FLOW-04 | `POST /api/doctor/prescriptions/{id}/corrections` | `prescriptions.*`, `clinical.visit_corrections` | AC-RX-004, AC-NEG-027 | 3 |
| **§13 Referrals** | | | | | | | |
| FR-REF-001 | Doctor creates a referral | DB §12; SEC §7; API A3 | FLOW-04, FLOW-12 | `POST /api/doctor/visits/{visitId}/referrals` | `crm.referrals` | AC-REF-001 | 3 |
| FR-REF-002 | Doctor / Manager referral view | DB §12; SEC §7; API A3 | — | `GET /api/doctor/patients/{id}/referrals` | `crm.referrals` | AC-REF-002, AC-NEG-036 | 3 |
| FR-REF-003 | Sales referral view — reason yes, Doctor notes never | Q-PRV-1; DB §12; SEC §7; API A3 | FLOW-12 | `GET /api/sales/referrals`, `GET /api/sales/referrals/{id}` | `crm.referrals` (projection without `notes`) | AC-REF-003, AC-NEG-036 (linked frozen tests: SEC #64) | 3 |
| FR-REF-004 | Referral follow-up status | DB §12; SEC §7; API A3 | FLOW-12 | `PATCH /api/sales/referrals/{id}/status` | `crm.referrals.follow_up_status`, `crm.follow_ups.referral_id` | AC-REF-004 | 3 |
| **§14 Charges / Payments / Patient Credit** | | | | | | | |
| FR-FIN-001 | Charges are established at Visit finalization | Q-FIN-W1; FIN §2–§9; DB §13 | FLOW-04 | Internal `Finance.EstablishCharges` called by `POST /api/doctor/visits/{id}/finalize` | `finance.patient_charges` | AC-FIN-001 | 3, 4 |
| FR-FIN-002 | Charge content and immutability | FIN §2–§9; DB §13 | — | N/A | `finance.patient_charges` | AC-FIN-002 | 4 |
| FR-FIN-003 | Derived Billed, Collected, Outstanding | FIN §2–§9; DB §13 | — | Balance queries (FR-FIN-010) | Finance ledger tables | AC-FIN-003 | 4 |
| FR-FIN-004 | Record a payment | FIN §2–§9; DB §13 | FLOW-05, FLOW-08 | `POST /api/sales/payments` (Manager uses the same route) | `finance.payments`, `finance.payment_allocations`, `finance.patient_credit_entries` | AC-FIN-004, AC-NEG-007, AC-NEG-009 | 4 |
| FR-FIN-005 | Explicit payment allocations | FIN §2–§9; DB §13; [T1] | FLOW-05 | `POST /api/sales/payments` | `finance.payment_allocations` | AC-FIN-005 | 4 |
| FR-FIN-006 | Overpayment and advance payment become Patient Credit | Q-PAY-3; FIN §2–§9; DB §13 | FLOW-05, FLOW-06 | `POST /api/sales/payments` | `finance.patient_credit_entries` | AC-FIN-006 | 4 |
| FR-FIN-007 | Apply Patient Credit to a charge | FIN §2–§9; DB §13; [T1] | FLOW-06 | `POST /api/sales/patients/{id}/credit/apply` | `finance.payment_allocations`, `finance.patient_credit_entries` | AC-FIN-007 | 4 |
| FR-FIN-008 | Patient Credit is a liability, never revenue | FIN §2–§9; DB §13 | FLOW-06 | N/A | `finance.patient_credit_entries` | AC-FIN-008 | 4 |
| FR-FIN-009 | Payment conservation | FIN §2–§9; DB §13 | FLOW-05 | N/A | G8 deferred trigger | AC-FIN-009 | 4 |
| FR-FIN-010 | Patient balance, payment history and outstanding list | FIN §2–§9; DB §13 | — | `GET /api/sales/patients/{id}/balance`, `GET /api/sales/patients/{id}/payments`, `GET /api/sales/outstanding-balances` | Finance ledger | AC-FIN-010, AC-NEG-039 | 4 |
| FR-FIN-011 | No printed receipt or invoice | Q-DOC-1; FIN §2–§9; DB §13 | — | None (API A9) | None | AC-FIN-011, AC-NEG-009 | 4 |
| FR-FIN-012 | Manager Refund | Q-PAY-1; FIN §2–§9; DB §13 | FLOW-06, FLOW-15 | `POST /api/manager/payments/{id}/refund` | `finance.payment_corrections`, `payments`, `payment_allocations`, `patient_credit_entries` | AC-FIN-012 | 4 |
| FR-FIN-013 | Manager Payment Reversal | FIN §2–§9; DB §13 | FLOW-15 | `POST /api/manager/payments/{id}/reversal` | As FR-FIN-012, `orthodontics.installment_payments` | AC-FIN-013 | 4 |
| FR-FIN-014 | Manager Reallocation | FIN §2–§9; DB §13 | FLOW-15 | `POST /api/manager/payments/{id}/reallocate` | As FR-FIN-012 | AC-FIN-014 | 4 |
| FR-FIN-015 | Manager Payment Replacement (Payment Correction) | FIN §2–§9; DB §13 | FLOW-15 | `POST /api/manager/payments/{id}/replace` | As FR-FIN-012 | AC-FIN-015 | 4 |
| FR-FIN-016 | Correction propagation | FIN §2–§9; DB §13 | FLOW-15 | N/A | Distribution tables | AC-FIN-016 | 4 |
| FR-FIN-017 | Manager price / discount correction of a non-orthodontic charge | FIN §2–§9; DB §13 | FLOW-15 | `POST /api/manager/charges/{id}/adjustments` | `finance.charge_adjustments` | AC-FIN-017 | 4 |
| FR-FIN-018 | Doctor entitlement on every collection | FIN §2–§9; DB §13 | FLOW-05 | N/A | `finance.doctor_entitlements`, `finance.distribution_events` | AC-FIN-018 | 4 |
| FR-FIN-019 | Consultation charges produce no Doctor commission | FIN §2–§9; DB §13; [T3] | FLOW-05 | N/A | `finance.patient_charges.charge_kind = 'Consultation'` | AC-FIN-019 | 4 |
| FR-FIN-020 | Distribution identity and non-negative Clinic Share | FIN §2–§9; DB §13; [T6] | FLOW-05 | N/A | `finance.distribution_events`, `doctor_earnings`, `revenue_distributions` | AC-FIN-020 | 4 |
| **§15 Discounts** | | | | | | | |
| FR-FIN-021 | Set a fixed-EGP Discount before Visit finalization | Q-FIN-3; FIN §3; DB §9; SEC §4; [T11] | FLOW-04, FLOW-08 | `PUT /api/doctor/pricing-lines/{lineId}/discount`, `PUT /api/sales/pricing-lines/{lineId}/discount` (Manager uses the Sales route) | `clinical.pricing_discount_entries` | AC-FIN-021, AC-NEG-020 | 3, 4 |
| FR-FIN-022 | Sales pricing view without clinical data | FIN §3; DB §9; SEC §4 | FLOW-04 | `GET /api/sales/pricing-lines?patientId=` | Clinical via `ISalesPricingLines` | AC-FIN-022, AC-NEG-035, AC-NEG-039 | 3, 4 |
| FR-FIN-023 | Discount concurrency | FIN §3; DB §9; SEC §4 | FLOW-04 | As FR-FIN-021 | `pricing_discount_entries` partial unique indexes | AC-FIN-023 | 3, 4 |
| FR-FIN-024 | Discount snapshot at finalization; Manager-only afterwards | FIN §3; DB §9; SEC §4 | FLOW-04 | N/A | `patient_charges.discount_*` | AC-FIN-024 | 4 |
| FR-FIN-025 | Price may not drop below the existing discount | FIN §3; DB §9; SEC §4; [T11] | — | `PUT /api/doctor/work-items/{id}/pricing` | `case_work_items.entered_price`, `pricing_discount_entries` | AC-FIN-025 | 3, 4 |
| **§16 Doctor Financial Ownership** | | | | | | | |
| FR-FIN-026 | Financial ownership stays with the performing Doctor | Q-FIN-9; FIN §8, §14 | — | N/A | `patient_charges.performing_doctor_id` | AC-FIN-026 | 4 |
| FR-FIN-027 | Reassignment never moves money | FIN §8, §14 | — | N/A | N/A | AC-FIN-027, AC-NEG-016 | 4 |
| FR-FIN-028 | Earning belongs to the payroll month of the collection | FIN §8, §14 | FLOW-05, FLOW-09 | N/A | `finance.doctor_earnings.recognized_at` | AC-FIN-028 | 4 |
| FR-FIN-029 | Doctor own-work financial view | FIN §8, §14 | — | `GET /api/doctor/me/work-items`, `GET /api/doctor/me/work-items/{id}` | Charges, allocations, entitlements, earnings, distributions | AC-FIN-029, AC-NEG-038 | 4 |
| FR-FIN-030 | Inactive Doctor keeps entitlement on earlier work | FIN §8, §14 | FLOW-09 | `POST /api/manager/payroll/doctors/drafts` | `finance.doctor_earnings`, `payroll.doctor_payrolls` | AC-FIN-030 | 4 |
| FR-FIN-031 | No attribution holds | FIN §8, §14 | — | None (API A9) | No hold tables | AC-FIN-031, AC-NEG-016 | 4 |
| **§17 General Lab** | | | | | | | |
| FR-LAB-001 | General lab model applicability | FIN §6; [T] | — | N/A | `procedure_types.lab_model`, `treatment_cases.lab_model_snapshot`, `patient_charges.lab_model` | AC-LAB-001 | 4 |
| FR-LAB-002 | General lab distribution at the allocation-time rate | Q-FIN-W2; FIN §6 | FLOW-05 | N/A | `finance.revenue_distributions`, `finance.doctor_earnings`, `configuration.lab_percentage_history` | AC-LAB-002 | 4 |
| FR-LAB-003 | Configure the General Lab percentage | FIN §6; [T6] | — | `GET/PUT /api/manager/settings/lab-percentage` | `configuration.lab_percentage_history` | AC-LAB-003 | 4 |
| FR-LAB-004 | General-lab corrections use the recorded rate | FIN §6 | FLOW-15 | N/A | `revenue_distributions.lab_percentage_applied` | AC-LAB-004 | 4 |
| **§18 Fixed Lab** | | | | | | | |
| FR-LAB-005 | One Fixed Lab obligation per case | FIN §7; DB §13 | FLOW-07 | N/A | `finance.fixed_lab_expenses`, `finance.fixed_lab_revisions` | AC-LAB-005, AC-NEG-039 | 4 |
| FR-LAB-006 | Doctor enters the initial Fixed Lab expense | FIN §7; DB §13 | FLOW-04, FLOW-07 | `POST /api/doctor/fixed-lab/{caseId}` | `finance.fixed_lab_expenses`, `finance.fixed_lab_revisions` (revision 1 role CHECK) | AC-LAB-006, AC-NEG-023 | 3, 4 |
| FR-LAB-007 | Unknown Fixed Lab is never zero — finalize precondition | FIN §7; DB §13; [T13] | FLOW-07 | `POST /api/doctor/visits/{id}/finalize` | G7 deferred trigger | AC-LAB-007 | 3, 4 |
| FR-LAB-008 | Lab-First collection order (waterfall) | Q-FIN-1; FIN §7; DB §13 | FLOW-05, FLOW-07 | N/A | `doctor_entitlements`, `doctor_earnings`, `revenue_distributions`, `distribution_events` | AC-LAB-008 | 4 |
| FR-LAB-009 | Pending Doctor entitlement | FIN §7; DB §13 | FLOW-07 | `GET /api/manager/finance/pending-entitlements`; `DoctorOwnWorkDto.pendingEntitlement` | Derived | AC-LAB-009 | 4 |
| FR-LAB-010 | Manager revises the Fixed Lab | Q-FIN-6; FIN §7; DB §13 | FLOW-07 | `PUT /api/manager/fixed-lab/{caseId}` | `finance.fixed_lab_revisions`, distribution tables | AC-LAB-010 | 4 |
| **§19 Orthodontics** | | | | | | | |
| FR-ORT-001 | Create an Orthodontic case | FIN §13; DB §11; API A8 | FLOW-08 | `POST /api/doctor/visits/{visitId}/orthodontic-cases` | `orthodontics.ortho_cases`, `clinical.case_work_items` | AC-ORT-001 | 3, 4 |
| FR-ORT-002 | Doctor enters initial Ortho terms (stored only) | Q-ORT-4; FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-001 | `orthodontics.ortho_term_revisions` | AC-ORT-002, AC-NEG-023 | 3, 4 |
| FR-ORT-003 | Effective Ortho Total | FIN §13; DB §11; API A8 | FLOW-08 | Ortho read (`grossTotal`, `discount`, `effectiveOrthoTotal`) | Charge original / discount / final; terms revisions | AC-ORT-003 | 4 |
| FR-ORT-004 | Charge and initial installments generated at Visit finalization | FIN §13; DB §11; API A8; [T] | FLOW-08 | `POST /api/doctor/visits/{id}/finalize` | `finance.patient_charges`, `orthodontics.ortho_installments`, `ortho_installment_revisions` | AC-ORT-004 | 3, 4 |
| FR-ORT-005 | Ortho read model | FIN §13; DB §11; API A8 | — | `GET /api/{doctor\|sales}/orthodontic-cases/{id}` | Orthodontics tables (derived values) | AC-ORT-005 | 4 |
| FR-ORT-006 | Manual installment payment application | Q-ORT-2; FIN §13; DB §11; API A8 | FLOW-08 | `POST /api/sales/orthodontic-cases/{id}/installment-payments` | `orthodontics.installment_payments` | AC-ORT-006, AC-NEG-022 | 4 |
| FR-ORT-007 | Derived installment status | FIN §13; DB §11; API A8 | FLOW-08 | Ortho read | Derived | AC-ORT-007 | 4 |
| FR-ORT-008 | Manager edits Ortho terms (the only route) | FIN §13; DB §11; API A8 | FLOW-08 | `PUT /api/manager/orthodontic-cases/{id}/terms` | `orthodontics.ortho_term_revisions` | AC-ORT-008 | 4 |
| FR-ORT-009 | Ortho total increase adds one difference installment | Q-ORT-3; FIN §13; DB §11; API A8; [T] | FLOW-08 | As FR-ORT-008 | `ortho_term_revisions`, `finance.charge_adjustments`, `ortho_installments` | AC-ORT-009 | 4 |
| FR-ORT-010 | Ortho total decrease rejected in V1 | Q-ORT-3b; FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-008 | N/A | AC-ORT-010, AC-NEG-021 | 4 |
| FR-ORT-011 | Deposit change after installments exist | Q-ORT-5; FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-008 | `ortho_term_revisions` | AC-ORT-011, AC-NEG-022 | 4 |
| FR-ORT-012 | Number of Months change after installments exist | FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-008 | `ortho_term_revisions` | AC-ORT-012, AC-NEG-022 | 4 |
| FR-ORT-013 | Derived schedule validity flag | FIN §13; DB §11; API A8 | FLOW-08 | Ortho read | Derived | AC-ORT-013 | 4 |
| FR-ORT-014 | Manager schedule batch — revise installment amount | FIN §13; DB §11; API A8 | FLOW-08 | `PUT /api/manager/orthodontic-cases/{id}/installments` | `ortho_installment_revisions` | AC-ORT-014 | 4 |
| FR-ORT-015 | Manager schedule batch — revise due date of an unpaid installment | FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-014 | `ortho_installment_revisions` | AC-ORT-015 | 4 |
| FR-ORT-016 | Manager schedule batch — add an installment manually | FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-014 | `ortho_installments`, `ortho_installment_revisions` | AC-ORT-016 | 4 |
| FR-ORT-017 | Manager schedule batch — retire an unpaid installment | FIN §13; DB §11; API A8 | FLOW-08 | As FR-ORT-014 | `ortho_installment_revisions` | AC-ORT-017, AC-NEG-022 | 4 |
| FR-ORT-018 | Schedule batch is atomic and must end valid | FIN §13; DB §11; API A8 | FLOW-08 | `PUT /api/manager/orthodontic-cases/{id}/installments` | Orthodontics tables | AC-ORT-018 | 4 |
| FR-ORT-019 | Paid history protection and no generic adjustments on ortho charges | FIN §13; DB §11; API A8 | FLOW-08, FLOW-15 | `POST /api/manager/charges/{id}/adjustments` (refuses ortho) | G5(b)–(d), G9, G10 | AC-ORT-019, AC-NEG-022 | 4 |
| FR-ORT-020 | Refund / reversal / correction of ortho payments | FIN §13; DB §11; API A8 | FLOW-08 | Manager correction routes | Finance ledger, `installment_payments` | AC-ORT-020 | 4 |
| FR-ORT-021 | Doctor and Sales cannot edit terms or schedule | FIN §13; DB §11; API A8 | — | `PUT …/terms`, `PUT …/installments` | N/A | AC-ORT-021 (linked frozen tests: SEC #34, #71, #72) | 4 |
| **§20 Sales Commission** | | | | | | | |
| FR-PAYROLL-001 | Commission base = collected money of Lead-sourced patients | Q-FIN-2; FIN §10; DB §6, §15; [T3] | FLOW-01, FLOW-10 | `/api/manager/payroll/sales` drafts | `payroll.sales_payroll_commission_items` | AC-PAYROLL-001 | 4 |
| FR-PAYROLL-002 | Rate at collection time, snapshotted per item | FIN §10; DB §6, §15; [T12] | FLOW-01, FLOW-10 | N/A | `staff.sales_commission_rate_history`, commission items | AC-PAYROLL-002 | 4 |
| FR-PAYROLL-003 | New commission rate applies to future collections only | Q-COMM-1; FIN §10; DB §6, §15 | FLOW-01, FLOW-10 | None for recalculation (API A9) | As FR-PAYROLL-002 | AC-PAYROLL-003, AC-NEG-024 | 4 |
| FR-PAYROLL-004 | Walk-In / Direct patients generate zero commission | Q-PAT-1; FIN §10; DB §6, §15 | FLOW-01, FLOW-02, FLOW-10 | None (no pool routes) | `patient_contacts.patient_source` | AC-PAYROLL-004, AC-NEG-015 | 4 |
| FR-PAYROLL-005 | Inactive Sales gets no new commission | Q-EMP-1; FIN §10; DB §6, §15; [T4] | FLOW-01, FLOW-10, FLOW-16 | N/A | `staff.sales_employees.deactivated_at` | AC-PAYROLL-005 | 4 |
| FR-PAYROLL-006 | Reversal / refund commission at the original rate | FIN §10; DB §6, §15 | FLOW-10, FLOW-15 | N/A | Commission items | AC-PAYROLL-006 | 4 |
| FR-PAYROLL-007 | Unapplied Patient Credit is not commission base | FIN §10; DB §6, §15; [T1] | FLOW-10 | N/A | N/A | AC-PAYROLL-007 | 4 |
| **§21 Expenses** | | | | | | | |
| FR-EXP-001 | Expense categories | Post-freeze Owner decision (FRS header); DB §14; API A8; SEC §4 | — | `GET/POST/PUT /api/manager/expense-categories` | `expenses.expense_categories` | AC-EXP-001, AC-NEG-001 | 4 |
| FR-EXP-002 | Sales records Materials / Petty expenses (create only) | Post-freeze Owner decision (FRS header); Q-EXP-3; DB §14; API A8; SEC §4 | — | `POST /api/sales/expenses` — request `{ categoryId, amount, specification }` (all required); response `ExpenseCreatedDto` | `expenses.clinic_expenses` (category, amount, specification text, `created_by_user_id`, `created_by_role`, `created_at`) | AC-EXP-002, AC-NEG-001, AC-NEG-042 | 4 |
| FR-EXP-003 | Sales allowed-categories lookup | DB §14; API A8; SEC §4 | — | `GET /api/sales/expenses/allowed-categories` | `expenses.expense_categories` | AC-EXP-003, AC-NEG-042 | 4 |
| FR-EXP-004 | Manager expense management | DB §14; API A8; SEC §4 | — | `GET/POST /api/manager/expenses` | `expenses.clinic_expenses` | AC-EXP-004 | 4 |
| FR-EXP-005 | Manager edits an expense directly, audited | DB §14; API A8; SEC §4 | — | `PUT /api/manager/expenses/{id}` | `expenses.clinic_expenses` | AC-EXP-005 | 4 |
| FR-EXP-006 | No Expense Finalized state, no attachments, no deletion | Q-EXP-2; Q-EXP-1; DB §14; API A8; SEC §4 | — | None (API A9) | N/A | AC-EXP-006, AC-NEG-010, AC-NEG-019 | 4 |
| FR-EXP-007 | Expenses reduce only the Clinic result | DB §14; API A8; SEC §4 | — | N/A | N/A | AC-EXP-007 | 4 |
| **§22 Attendance / Assistant Overtime** | | | | | | | |
| FR-STAFF-012 | Manager records attendance for Assistants and Sales / Reception | Q-ATT-1; DB §6; FIN §11.3; API A8; [T9] | FLOW-11 | `GET/POST /api/manager/staff/attendance` | `staff.attendance_records` | AC-STAFF-012, AC-NEG-026 | 4 |
| FR-STAFF-013 | Edit attendance | DB §6; FIN §11.3; API A8 | FLOW-11 | `PUT /api/manager/staff/attendance/{id}` | `staff.attendance_records` | AC-STAFF-013 | 4 |
| FR-STAFF-014 | Attendance alone changes no pay | DB §6; FIN §11.3; API A8 | FLOW-11 | N/A | N/A | AC-STAFF-014, AC-NEG-026 | 4 |
| FR-STAFF-015 | Record approved Assistant overtime | DB §6; FIN §11.3; API A8 | FLOW-11 | `GET /api/manager/staff/assistants/{id}/overtime?month`, `POST /api/manager/staff/assistants/{id}/overtime` | `staff.assistant_overtime_entries` | AC-STAFF-015, AC-NEG-026 | 4 |
| FR-STAFF-016 | Void an overtime entry (void-only lifecycle) | DB §6; FIN §11.3; API A8 | FLOW-11 | `POST /api/manager/staff/assistants/overtime/{entryId}/void` | `staff.assistant_overtime_entries` | AC-STAFF-016 | 4 |
| FR-STAFF-017 | Sales has no overtime | DB §6; FIN §11.3; API A8 | — | N/A | N/A | AC-STAFF-017, AC-NEG-026 | 4 |
| **§23 Payroll** | | | | | | | |
| FR-PAYROLL-008 | Monthly payroll lifecycle | Q-PAY-2; FIN §11; DB §15; API A8 | FLOW-09, FLOW-10 | `/api/manager/payroll/{doctors\|sales\|assistants}` (`drafts`, `finalize`, `mark-paid`) | `payroll.*` | AC-PAYROLL-008 | 4 |
| FR-PAYROLL-009 | Doctor payroll | FIN §11; DB §15; API A8 | FLOW-09 | `GET /api/manager/payroll/doctors`, `POST …/drafts`, `POST …/{id}/finalize`, `POST …/{id}/mark-paid` | `payroll.doctor_payrolls`, `doctor_payroll_items` | AC-PAYROLL-009 | 4 |
| FR-PAYROLL-010 | Doctor payroll adjustments | FIN §11; DB §15; API A8 | FLOW-09 | `POST /api/manager/payroll/doctors/{id}/adjustments` | `payroll.doctor_payroll_adjustments` | AC-PAYROLL-010 | 4 |
| FR-PAYROLL-011 | Doctor reads own payroll | FIN §11; DB §15; API A8 | FLOW-09 | `GET /api/doctor/me/payroll` | `payroll.doctor_payrolls` | AC-PAYROLL-011, AC-NEG-038 | 4 |
| FR-PAYROLL-012 | Sales payroll | FIN §11; DB §15; API A8; [T8] | FLOW-10 | `/api/manager/payroll/sales` (drafts, adjustments, finalize, mark-paid) | `payroll.sales_payrolls`, `sales_payroll_commission_items`, `sales_payroll_adjustments` | AC-PAYROLL-012 | 4 |
| FR-PAYROLL-013 | Sales reads own payroll only | FIN §11; DB §15; API A8 | FLOW-10 | `GET /api/sales/me/payroll` | `payroll.sales_payrolls` | AC-PAYROLL-013, AC-NEG-039 | 4 |
| FR-PAYROLL-014 | Assistant payroll draft | FIN §11; DB §15; API A8 | FLOW-11 | `GET /api/manager/payroll/assistants`, `GET …/{id}`, `POST /api/manager/payroll/assistants/drafts` | `payroll.assistant_payrolls`, `assistant_payroll_overtime_items` | AC-PAYROLL-014 | 4 |
| FR-PAYROLL-015 | Assistant partial-month manual Base Pay | Q-PAY-ASST-2; FIN §11; DB §15; API A8 | FLOW-11 | `PUT /api/manager/payroll/assistants/{id}/base-pay` | `payroll.assistant_payrolls` | AC-PAYROLL-015, AC-NEG-025 | 4 |
| FR-PAYROLL-016 | Assistant Final = Base Pay + Overtime; finalize and pay | FIN §11; DB §15; API A8 | FLOW-11 | `POST /api/manager/payroll/assistants/{id}/finalize`, `POST …/{id}/mark-paid` | `payroll.assistant_payrolls` | AC-PAYROLL-016, AC-NEG-025 | 4 |
| **§24 Feedback** | | | | | | | |
| FR-FDB-001 | Feedback request created at Visit finalization | DB §16; API A2, A8; SEC §3 | FLOW-04, FLOW-13 | `POST /api/doctor/visits/{id}/finalize` | `feedback.feedback_requests` | AC-FDB-001 | 3 |
| FR-FDB-002 | Sales feedback queue (derived state) | DB §16; API A2, A8; SEC §3; [T14] | FLOW-13 | `GET /api/sales/feedback-requests?status=` | `feedback.feedback_requests`, `feedback.feedback_entries` | AC-FDB-002, AC-NEG-043 | 3 |
| FR-FDB-003 | Prepare the unique feedback link | DB §16; API A2, A8; SEC §3; [T] | FLOW-13 | `POST /api/sales/feedback-requests/{id}/link` | `feedback_requests.link_token_hash` | AC-FDB-003 | 3 |
| FR-FDB-004 | Mark the link as sent | DB §16; API A2, A8; SEC §3 | FLOW-13 | `POST /api/sales/feedback-requests/{id}/mark-sent` | `link_sent_at`, `sent_by_user_id`, `delivery_channel` | AC-FDB-004, AC-NEG-030 | 3 |
| FR-FDB-005 | Patient opens the feedback form on the clinic website | DB §16; API A2, A8; SEC §3 | FLOW-13 | `GET /api/public/feedback/{token}` | `feedback_requests` | AC-FDB-005, AC-NEG-041 | 3 |
| FR-FDB-006 | Patient submits website feedback | Q-FDB-2; DB §16; API A2, A8; SEC §3 | FLOW-13 | `POST /api/public/feedback/{token}` | `feedback.feedback_entries` | AC-FDB-006, AC-NEG-041 | 3 |
| FR-FDB-007 | Feedback links never expire | Q-FDB-1; DB §16; API A2, A8; SEC §3 | FLOW-13 | N/A | N/A | AC-FDB-007, AC-NEG-012 | 3 |
| FR-FDB-008 | Sales-entered feedback about a Visit | DB §16; API A2, A8; SEC §3 | FLOW-14 | `POST /api/sales/feedback-entries` | `feedback.feedback_entries` | AC-FDB-008 | 3 |
| FR-FDB-009 | Sales-entered general feedback (no Visit) | DB §16; API A2, A8; SEC §3 | FLOW-14 | `POST /api/sales/feedback-entries` (without `visitId`) | `feedback.feedback_entries` | AC-FDB-009 | 3 |
| FR-FDB-010 | At most one accepted result per request | DB §16; API A2, A8; SEC §3 | FLOW-14 | API A11 #50 | Unique index | AC-FDB-010 (linked frozen tests: API A11 #50) | 3 |
| FR-FDB-011 | Only the Manager reads feedback results | DB §16; API A2, A8; SEC §3 | FLOW-13, FLOW-14 | `GET /api/manager/feedback?doctorId&from&to&source` | `feedback.feedback_entries` | AC-FDB-011, AC-NEG-028, AC-NEG-043 | 3 |
| **§25 Notifications** | | | | | | | |
| FR-NOTIF-001 | Seven notification types | API Part B; GRD §8 | — | N/A | `notifications.notifications` | AC-NOTIF-001, AC-NEG-030 | 3 |
| FR-NOTIF-002 | Recipients | API Part B; GRD §8 | — | N/A | Views | AC-NOTIF-002, AC-NEG-004, AC-NEG-040 | 3 |
| FR-NOTIF-003 | J1 appointment and implant reminders | API Part B; GRD §8; [DEFAULT] | FLOW-03 | Hangfire J1 | `scheduling.appointments` | AC-NOTIF-003 | 3 |
| FR-NOTIF-004 | J2 orthodontic installment reminders | API Part B; GRD §8; [DEFAULT] | FLOW-08 | Hangfire J2 | Orthodontics, Finance (read) | AC-NOTIF-004 | 4 |
| FR-NOTIF-005 | J3 follow-up due notifications | API Part B; GRD §8; [DEFAULT] | FLOW-01 | Hangfire J3 | `crm.follow_ups` | AC-NOTIF-005 | 2, 3 |
| FR-NOTIF-006 | Event-driven notifications | API Part B; GRD §8 | FLOW-12 | N/A | `notifications.notifications` | AC-NOTIF-006 | 3 |
| FR-NOTIF-007 | Notification inbox | API Part B; GRD §8 | — | `GET /api/me/notifications`, `GET …/unread-count`, `PATCH …/{id}/read`, `POST …/read-all` | `notifications.notifications` | AC-NOTIF-007, AC-NEG-028 | 3 |
| FR-NOTIF-008 | Idempotent, timezone-aware, retry-safe processing | API Part B; GRD §8; [DEFAULT] | — | Hangfire J4, J5 | `notifications.notifications` | AC-NOTIF-008 | 3 |
| FR-NOTIF-009 | J7 operations health | API Part B; GRD §8 | — | Hangfire J7 | Views / probes | AC-NOTIF-009, AC-NEG-012 | 3, 5 |
| **§26 Reports** | | | | | | | |
| FR-REPORT-001 | Manager live daily dashboard | Q-FIN-DC; FIN §12; API A8 | — | `GET /api/manager/dashboard?date=` | Read-only over modules | AC-REPORT-001, AC-NEG-011 | 4 |
| FR-REPORT-002 | Financial report | FIN §12; API A8 | — | `GET /api/manager/reports/financial` | Read-only | AC-REPORT-002 | 4 |
| FR-REPORT-003 | Estimated Clinic Operating Result | FIN §12; API A8 | — | `GET /api/manager/reports/financial`, `GET /api/manager/finance/summary` | Read-only | AC-REPORT-003 | 4 |
| FR-REPORT-004 | Memo lines | FIN §12; API A8 | — | As FR-REPORT-002 | Read-only | AC-REPORT-004 | 4 |
| FR-REPORT-005 | Assistant and Sales payroll appear only in full months | Q-PAY-ASST-1; FIN §12; API A8 | FLOW-11 | As FR-REPORT-002 | Read-only | AC-REPORT-005 | 4 |
| FR-REPORT-006 | Finance summary and finance lists | FIN §12; API A8 | — | `GET /api/manager/finance/summary`, `/finance/pending-entitlements`, `/finance/charges`, `/finance/payments`, `/finance/credit` | Read-only | AC-REPORT-006 | 4 |
| FR-REPORT-007 | Doctor-performance, Sales-performance, orthodontic-receivables, expenses and attendance reports | FIN §12; API A8 | — | `GET /api/manager/reports/doctor-performance`, `/sales-performance`, `/orthodontic-receivables`, `/expenses`, `/attendance` | Read-only | AC-REPORT-007, AC-NEG-037 | 4 |
| FR-REPORT-008 | Report exports | FIN §12; API A8 | — | `GET /api/manager/reports/…/export` | N/A | AC-REPORT-008 | 4 |
| FR-REPORT-009 | Sales dashboard | Post-freeze Owner decision (FRS header); FIN §12; API A8 | — | `GET /api/sales/dashboard` | Read-only | AC-REPORT-009, AC-NEG-014, AC-NEG-028 | 4 |
| **§27 Privacy / Authorization** | | | | | | | |
| FR-SEC-001 | Four-layer backend authorization on every request | SEC §1, §4–§7, §11; GRD §4 | — | All routes | `identity.permissions`, `identity.role_permissions` | AC-SEC-001 | 2, 5 |
| FR-SEC-002 | Doctors never receive patient contact data | SEC §1, §4–§7, §11; GRD §4 | — | All `/api/doctor/*` | `patients.patient_contacts`, `crm.*` | AC-SEC-002, AC-NEG-034 | 2, 3, 5 |
| FR-SEC-003 | Sales never receives Doctor-authored clinical detail or Doctor finance | SEC §1, §4–§7, §11; GRD §4 | — | All `/api/sales/*` | N/A | AC-SEC-003, AC-NEG-035, AC-NEG-039 | 2, 3, 4, 5 |
| FR-SEC-004 | Sales whitelist and deliberate exceptions | Q-PRV-1; SEC §1, §4–§7, §11; GRD §4 | — | Sales DTOs (API A3) | N/A | AC-SEC-004 | 2, 3, 4, 5 |
| FR-SEC-005 | Resource scoping — identity from the principal | SEC §1, §4–§7, §11; GRD §4 | — | Own routes | N/A | AC-SEC-005, AC-NEG-038 | 2, 3, 4, 5 |
| FR-SEC-006 | Audience DTOs and strict request binding | SEC §1, §4–§7, §11; GRD §4 | — | API A3 | N/A | AC-SEC-006 | 2, 5 |
| FR-SEC-007 | No personal data in URLs, logs, alerts or job arguments | SEC §1, §4–§7, §11; GRD §4 | — | N/A | N/A | AC-SEC-007, AC-NEG-037 | 2, 5 |
| FR-SEC-008 | Sales-authored Doctor-visible text check | SEC §1, §4–§7, §11; GRD §4 | — | Appointment booking | `scheduling.appointments.reason` | AC-SEC-008 | 3, 5 |
| FR-SEC-009 | Exactly six anonymous endpoints | SEC §1, §4–§7, §11; GRD §4 | — | API A2 | N/A | AC-SEC-009, AC-NEG-005, AC-NEG-006, AC-NEG-041 | 2, 3, 5 |
| FR-SEC-010 | Assistant, attendance and payroll data are Manager-only | SEC §1, §4–§7, §11; GRD §4 | — | `/api/manager/staff/*`, `/api/manager/payroll/*` | `staff.assistants`, `staff.attendance_records`, `staff.assistant_overtime_entries`, `payroll.assistant_*` | AC-SEC-010, AC-NEG-004, AC-NEG-040 | 4, 5 |
| FR-SEC-011 | Denied-by-default capabilities | SEC §1, §4–§7, §11; GRD §4; [T5] | — | Generated role-matrix test | N/A | AC-SEC-011, AC-NEG-028, AC-NEG-042 | 2, 3, 4, 5 |
| FR-SEC-012 | Web security controls | SEC §1, §4–§7, §11; GRD §4; [DEFAULT] | — | SEC §3, §12, §13 | N/A | AC-SEC-012 | 2, 5 |
| **§28 Audit / Historical Integrity** | | | | | | | |
| FR-AUDIT-001 | Audit written atomically and insert-only | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | `audit.audit_log` | AC-AUDIT-001 | 2, 5 |
| FR-AUDIT-002 | Audited events | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | `audit.audit_log` | AC-AUDIT-002 | 2, 3, 4, 5 |
| FR-AUDIT-003 | Never-stored audit content | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | N/A | AC-AUDIT-003, AC-NEG-037 | 2, 5 |
| FR-AUDIT-004 | Manager-only audit read | SEC §9; DB §1, §18, §20; GRD §7 | — | `GET /api/manager/audit`, `GET /api/manager/audit/{id}` | `audit.audit_log` | AC-AUDIT-004 | 2, 5 |
| FR-AUDIT-005 | No physical deletion of business history | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | All business schemas | AC-AUDIT-005 | 2, 3, 4, 5 |
| FR-AUDIT-006 | Insert-only ledgers; corrections are new rows | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | R1 | AC-AUDIT-006 | 2, 3, 4, 5 |
| FR-AUDIT-007 | Snapshots are never recalculated | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | DB §20 snapshots | AC-AUDIT-007 | 4, 5 |
| FR-AUDIT-008 | Historical relationships preserved | SEC §9; DB §1, §18, §20; GRD §7 | — | N/A | `patient_doctor_assignments`, `appointments` chain, `responsibility_history` | AC-AUDIT-008 | 2, 3, 5 |

## 4. FLOW Traceability

| FLOW ID | Name | FR IDs | Actors | Main Modules | Implementation Task | Acceptance IDs |
|---|---|---|---|---|---|---|
| FLOW-01 | Lead → Patient | FR-CRM-001, FR-CRM-002, FR-CRM-005, FR-CRM-006, FR-CRM-007, FR-NOTIF-005, FR-CRM-004, FR-PAT-002, FR-PAYROLL-001, FR-PAYROLL-002, FR-PAYROLL-003, FR-PAYROLL-004, FR-PAYROLL-005 | Sales, System | Crm, Patients, Payroll | 2, 4, 5 | AC-CRM-001, AC-CRM-002, AC-CRM-005, AC-CRM-006, AC-CRM-007, AC-NOTIF-005, AC-CRM-004, AC-PAT-002, AC-PAYROLL-001, AC-PAYROLL-002, AC-PAYROLL-003, AC-PAYROLL-004, AC-PAYROLL-005 |
| FLOW-02 | Walk-In → Patient | FR-PAT-001, FR-MED-002, FR-MED-007, FR-SCH-003, FR-PAYROLL-004 | Sales, Manager, System | Patients, Scheduling, Payroll | 2, 4, 5 | AC-PAT-001, AC-MED-002, AC-MED-007, AC-SCH-003, AC-PAYROLL-004 |
| FLOW-03 | Appointment → Arrival → Doctor Queue → Visit | FR-SCH-001, FR-SCH-002, FR-SCH-003, FR-SCH-004, FR-NOTIF-003, FR-SCH-006, FR-SCH-007, FR-SCH-009, FR-SCH-011, FR-MED-003, FR-PAT-007, FR-CLN-001, FR-SCH-012 | Manager, Sales, Doctor | Scheduling, Clinical, Patients, Notifications | 3, 5 | AC-SCH-001, AC-SCH-002, AC-SCH-003, AC-SCH-004, AC-NOTIF-003, AC-SCH-006, AC-SCH-007, AC-SCH-009, AC-SCH-011, AC-MED-003, AC-PAT-007, AC-CLN-001, AC-SCH-012 |
| FLOW-04 | Visit → Priced Work → Finalize → Charge | FR-CLN-002, FR-CLN-003, FR-CLN-004, FR-CLN-005, FR-CLN-006, FR-CLN-007, FR-CLN-008, FR-CLN-009, FR-CLN-010, FR-LAB-006, FR-FIN-021, FR-FIN-022, FR-FIN-023, FR-RX-001, FR-REF-001, FR-SCH-008, FR-CLN-012, FR-FIN-001, FR-FIN-024, FR-SCH-013, FR-FDB-001, FR-CLN-014, FR-RX-004 | Doctor, Sales, Manager | Clinical, Finance, Scheduling, Prescriptions, Crm, Feedback | 3, 4, 5 | AC-CLN-002, AC-CLN-003, AC-CLN-004, AC-CLN-005, AC-CLN-006, AC-CLN-007, AC-CLN-008, AC-CLN-009, AC-CLN-010, AC-LAB-006, AC-FIN-021, AC-FIN-022, AC-FIN-023, AC-RX-001, AC-REF-001, AC-SCH-008, AC-CLN-012, AC-FIN-001, AC-FIN-024, AC-SCH-013, AC-FDB-001, AC-CLN-014, AC-RX-004 |
| FLOW-05 | Payment → Allocation → Distribution | FR-FIN-004, FR-FIN-005, FR-FIN-006, FR-FIN-018, FR-LAB-002, FR-FIN-028, FR-LAB-008, FR-FIN-019, FR-FIN-009, FR-FIN-020 | Sales, Manager, System | Finance | 4, 5 | AC-FIN-004, AC-FIN-005, AC-FIN-006, AC-FIN-018, AC-LAB-002, AC-FIN-028, AC-LAB-008, AC-FIN-019, AC-FIN-009, AC-FIN-020 |
| FLOW-06 | Patient Credit | FR-FIN-006, FR-FIN-008, FR-FIN-007, FR-FIN-012 | Sales, Manager | Finance | 4, 5 | AC-FIN-006, AC-FIN-008, AC-FIN-007, AC-FIN-012 |
| FLOW-07 | Fixed Lab | FR-CLN-007, FR-LAB-005, FR-LAB-006, FR-LAB-007, FR-LAB-008, FR-LAB-009, FR-LAB-010 | Doctor, Sales, Manager | Clinical, Finance | 3, 4, 5 | AC-CLN-007, AC-LAB-005, AC-LAB-006, AC-LAB-007, AC-LAB-008, AC-LAB-009, AC-LAB-010 |
| FLOW-08 | Orthodontics | FR-ORT-001, FR-ORT-002, FR-FIN-021, FR-ORT-003, FR-ORT-004, FR-FIN-004, FR-ORT-006, FR-ORT-007, FR-NOTIF-004, FR-ORT-008, FR-ORT-009, FR-ORT-010, FR-ORT-011, FR-ORT-012, FR-ORT-013, FR-ORT-014, FR-ORT-015, FR-ORT-016, FR-ORT-017, FR-ORT-018, FR-ORT-019, FR-ORT-020 | Doctor, Sales, Manager | Orthodontics, Finance, Clinical, Notifications | 3, 4, 5 | AC-ORT-001, AC-ORT-002, AC-FIN-021, AC-ORT-003, AC-ORT-004, AC-FIN-004, AC-ORT-006, AC-ORT-007, AC-NOTIF-004, AC-ORT-008, AC-ORT-009, AC-ORT-010, AC-ORT-011, AC-ORT-012, AC-ORT-013, AC-ORT-014, AC-ORT-015, AC-ORT-016, AC-ORT-017, AC-ORT-018, AC-ORT-019, AC-ORT-020 |
| FLOW-09 | Doctor Payroll | FR-FIN-028, FR-PAYROLL-009, FR-FIN-030, FR-PAYROLL-010, FR-PAYROLL-008, FR-PAYROLL-011 | System, Manager | Finance, Payroll | 4, 5 | AC-FIN-028, AC-PAYROLL-009, AC-FIN-030, AC-PAYROLL-010, AC-PAYROLL-008, AC-PAYROLL-011 |
| FLOW-10 | Sales Payroll | FR-STAFF-003, FR-STAFF-004, FR-PAYROLL-001, FR-PAYROLL-002, FR-PAYROLL-003, FR-PAYROLL-004, FR-PAYROLL-005, FR-PAYROLL-006, FR-PAYROLL-007, FR-PAYROLL-012, FR-PAYROLL-008, FR-PAYROLL-013 | Manager | Staff, Finance, Payroll | 2, 4, 5 | AC-STAFF-003, AC-STAFF-004, AC-PAYROLL-001, AC-PAYROLL-002, AC-PAYROLL-003, AC-PAYROLL-004, AC-PAYROLL-005, AC-PAYROLL-006, AC-PAYROLL-007, AC-PAYROLL-012, AC-PAYROLL-008, AC-PAYROLL-013 |
| FLOW-11 | Assistant Attendance → Overtime → Payroll | FR-STAFF-006, FR-STAFF-007, FR-STAFF-008, FR-STAFF-012, FR-STAFF-013, FR-STAFF-014, FR-STAFF-015, FR-STAFF-016, FR-PAYROLL-014, FR-PAYROLL-015, FR-PAYROLL-016, FR-REPORT-005 | Manager | Staff, Payroll, Reporting | 4, 5 | AC-STAFF-006, AC-STAFF-007, AC-STAFF-008, AC-STAFF-012, AC-STAFF-013, AC-STAFF-014, AC-STAFF-015, AC-STAFF-016, AC-PAYROLL-014, AC-PAYROLL-015, AC-PAYROLL-016, AC-REPORT-005 |
| FLOW-12 | Referral → Follow-Up | FR-REF-001, FR-NOTIF-006, FR-REF-003, FR-CRM-005, FR-CRM-006, FR-REF-004 | Doctor, Sales | Crm, Clinical, Notifications | 2, 3, 5 | AC-REF-001, AC-NOTIF-006, AC-REF-003, AC-CRM-005, AC-CRM-006, AC-REF-004 |
| FLOW-13 | Patient Website Feedback | FR-FDB-001, FR-FDB-002, FR-FDB-003, FR-FDB-004, FR-FDB-005, FR-FDB-006, FR-FDB-007, FR-FDB-011 | System, Sales, Patient | Feedback, Clinical, Notifications | 3, 5 | AC-FDB-001, AC-FDB-002, AC-FDB-003, AC-FDB-004, AC-FDB-005, AC-FDB-006, AC-FDB-007, AC-FDB-011 |
| FLOW-14 | Sales-Entered Feedback | FR-FDB-008, FR-FDB-010, FR-FDB-009, FR-FDB-011 | Sales, Manager | Feedback | 3, 5 | AC-FDB-008, AC-FDB-010, AC-FDB-009, AC-FDB-011 |
| FLOW-15 | Refund / Financial Correction | FR-FIN-012, FR-FIN-013, FR-FIN-014, FR-FIN-015, FR-FIN-016, FR-LAB-004, FR-PAYROLL-006, FR-FIN-017, FR-ORT-019 | Manager, System | Finance, Payroll, Orthodontics | 4, 5 | AC-FIN-012, AC-FIN-013, AC-FIN-014, AC-FIN-015, AC-FIN-016, AC-LAB-004, AC-PAYROLL-006, AC-FIN-017, AC-ORT-019 |
| FLOW-16 | Inactive Sales Redistribution | FR-STAFF-009, FR-CRM-010, FR-CRM-011, FR-CRM-009, FR-PAYROLL-005, FR-CRM-012 | Manager, System | Staff, Identity, Crm, Payroll | 2, 4, 5 | AC-STAFF-009, AC-CRM-010, AC-CRM-011, AC-CRM-009, AC-PAYROLL-005, AC-CRM-012 |

## 5. Privacy Traceability

| Privacy Rule | FR IDs | Allowed Audience | Forbidden Audience | Required Negative Test IDs |
|---|---|---|---|---|
| Doctor contact-data denial | FR-SEC-002, FR-PAT-004, FR-PAT-007, FR-SCH-011, FR-SCH-014, FR-MED-003, FR-RX-003 | Sales / Reception, Manager | Doctor (DTOs, OpenAPI, search, PDF, CSV, notifications, logs, errors) | AC-NEG-034; AC-SEC-002, AC-PAT-004, AC-PAT-007, AC-SCH-011, AC-SCH-014, AC-MED-003, AC-RX-003 |
| Sales clinical-note denial | FR-SEC-003, FR-CLN-015, FR-FIN-022, FR-MED-008, FR-RX-002 | Doctor, Manager | Sales / Reception | AC-NEG-035; AC-SEC-003, AC-CLN-015, AC-FIN-022, AC-MED-008, AC-RX-002 |
| Sales referral-notes denial | FR-REF-003, FR-REF-002 | Doctor, Manager (reason / destination also Sales) | Sales / Reception (`notes`) | AC-NEG-036; AC-REF-003, AC-REF-002 |
| Medical History value protection | FR-MED-006, FR-MED-002, FR-MED-003, FR-AUDIT-003, FR-SEC-007, FR-REPORT-007 | Sales / Reception (read / edit), Doctor (read), Manager | Audit values, logs, notifications, reports, exports, Worker, Assistant | AC-NEG-037; AC-MED-006, AC-MED-002, AC-MED-003, AC-AUDIT-003, AC-SEC-007, AC-REPORT-007 |
| Doctor own-finance restriction | FR-FIN-029, FR-PAYROLL-011, FR-SEC-005 | Doctor (own work / own payroll), Manager | Doctor for other Doctors' items, case / patient / clinic totals | AC-NEG-038; AC-FIN-029, AC-PAYROLL-011, AC-SEC-005 |
| Sales protected-finance restriction | FR-SEC-003, FR-FIN-010, FR-FIN-022, FR-LAB-005, FR-PAYROLL-013, FR-STAFF-004 | Manager | Sales / Reception (Doctor %, shares, entitlement, lab, others' compensation, rate history) | AC-NEG-039; AC-SEC-003, AC-FIN-010, AC-FIN-022, AC-LAB-005, AC-PAYROLL-013, AC-STAFF-004 |
| Assistant no-access rule | FR-AUTH-001, FR-STAFF-006, FR-SEC-010, FR-NOTIF-002 | Manager (maintains records) | Assistant (no principal); Doctor and Sales (no Assistant data) | AC-NEG-040; AC-AUTH-001, AC-STAFF-006, AC-SEC-010, AC-NOTIF-002 |
| Patient no-login rule | FR-SEC-009, FR-FDB-005, FR-FDB-006 | Patient via public feedback token only | Any patient login, account, portal or app | AC-NEG-041; AC-SEC-009, AC-FDB-005, AC-FDB-006 |
| Sales expense / Manager analytics denial | FR-EXP-002, FR-EXP-003, FR-SEC-011 | Manager | Sales / Reception (list, read, edit, categories, reports) | AC-NEG-042; AC-EXP-002, AC-EXP-003, AC-SEC-011 |
| Feedback results Manager-only | FR-FDB-011, FR-FDB-002 | Manager | Doctor, Sales / Reception | AC-NEG-043; AC-FDB-011, AC-FDB-002 |

Negative tests above must include the canary scans named in the FRs (contact, clinical, referral-notes and medical-value canaries over every Doctor / Sales response, OpenAPI document, PDF, CSV, notification, log and error body) and the generated role-matrix test (FR-SEC-001).

## 6. Financial Traceability

| Rule | FR IDs | Source | Finance Invariant / Expected Result | Required Acceptance IDs |
|---|---|---|---|---|
| Charge at Visit Finalization | FR-FIN-001, FR-FIN-002, FR-CLN-012 | Q-FIN-W1; FIN §3 | No charge before finalize; one Treatment charge per work item + one Consultation charge, same transaction; Total = Price − Discount | AC-FIN-001, AC-FIN-002, AC-CLN-012 |
| Fixed-EGP Discount | FR-FIN-021, FR-FIN-023, FR-FIN-024, FR-FIN-025 | Q-FIN-3; FIN §3 | 0 ≤ discount ≤ price; no percentage; entry (actor, role, time); snapshot at finalize; Manager-only afterwards; stale entrySeq → 409 | AC-FIN-021, AC-FIN-023, AC-FIN-024, AC-FIN-025 |
| Patient Credit | FR-FIN-006, FR-FIN-007, FR-FIN-008 | Q-PAY-3; FIN §5 | Remainder → credit; credit never negative; unapplied credit = no revenue / entitlement / commission; Cash received = Collected + Credit | AC-FIN-006, AC-FIN-007, AC-FIN-008 |
| Payment allocation | FR-FIN-004, FR-FIN-005, FR-FIN-009 | FIN §5; G8 | payment = Σ allocations + Σ credit; allocation ≤ Outstanding; no oldest-first; idempotent | AC-FIN-004, AC-FIN-005, AC-FIN-009 |
| Refund / Reversal / Reallocation / Replacement | FR-FIN-012, FR-FIN-013, FR-FIN-014, FR-FIN-015, FR-FIN-016 | Q-PAY-1; FIN §9 | Manager only; new rows only; propagation to entitlement, lab, clinic, credit, current open payroll; sealed payroll unchanged | AC-FIN-012, AC-FIN-013, AC-FIN-014, AC-FIN-015, AC-FIN-016 |
| Doctor entitlement | FR-FIN-018, FR-FIN-019, FR-FIN-020 | FIN §6; G8b | entitled = allocated × snapshot %; lab not removed first; consultation none; Σ(lab + clinic + earnings) = basis; 0 ≤ earnings ≤ entitlements | AC-FIN-018, AC-FIN-019, AC-FIN-020 |
| Performing Doctor ownership | FR-FIN-026, FR-FIN-027, FR-FIN-031, FR-CLN-008 | Q-FIN-9; Q-CLN-2 | Ownership = performing_doctor_id forever; reassignment moves nothing; no hold object exists | AC-FIN-026, AC-FIN-027, AC-FIN-031, AC-CLN-008 |
| Inactive Doctor future collections | FR-FIN-030, FR-FIN-028 | Q-FIN-9; FIN §8 | Inactive Doctor still earns on old work; earning in month of collection; draft payroll possible | AC-FIN-030, AC-FIN-028 |
| Sales commission | FR-PAYROLL-001, FR-PAYROLL-004, FR-PAYROLL-007 | Q-FIN-2; Q-PAT-1; FIN §10 | Base = net allocations of Lead-sourced patients; Walk-In = 0, no pool; unapplied credit not base | AC-PAYROLL-001, AC-PAYROLL-004, AC-PAYROLL-007 |
| Commission rate snapshot / history | FR-STAFF-003, FR-STAFF-004, FR-PAYROLL-002, FR-PAYROLL-003, FR-PAYROLL-006 | Q-COMM-1; FIN §10 | 5 % on 1,000 = 50; after 7 %, 1,000 = 70; total 120; reversal of the 5 % item = −50; no recalculation | AC-STAFF-003, AC-STAFF-004, AC-PAYROLL-002, AC-PAYROLL-003, AC-PAYROLL-006 |
| Inactive Sales no future commission | FR-PAYROLL-005, FR-STAFF-009 | Q-EMP-1; T4 | No item for allocated_at ≥ deactivated_at; not redirected; reported as not accrued | AC-PAYROLL-005, AC-STAFF-009 |
| General Lab | FR-LAB-001, FR-LAB-002, FR-LAB-003, FR-LAB-004 | Q-FIN-W2; FIN §6 | Lab % at allocated_at, recorded per row; 10,000 / 40 % / 15 % / 2,000 → 800 + 300 + 900; Doctor % + Lab % ≤ 100 (T6) | AC-LAB-001, AC-LAB-002, AC-LAB-003, AC-LAB-004 |
| Fixed Lab Lab-First | FR-LAB-005, FR-LAB-006, FR-LAB-007, FR-LAB-008, FR-LAB-009 | Q-FIN-1; FIN §7 | L never defaulted to 0; Lab → pending entitlement FIFO → Clinic ≥ 0; Owner case 2,000 / 3,000 / 5,000 → Lab 3,000, Doctor 4,000, Clinic 3,000 | AC-LAB-005, AC-LAB-006, AC-LAB-007, AC-LAB-008, AC-LAB-009 |
| Fixed Lab revision / reconciliation | FR-LAB-010, FR-FIN-016 | Q-FIN-6; FIN §7.4 | Delta rows only; idempotent; entitlements and % unchanged; deltas in current open payroll month | AC-LAB-010, AC-FIN-016 |
| Orthodontic Effective Total | FR-ORT-003, FR-FIN-021 | Q-ORT-4; FIN §13 | Effective = Gross − Discount = Charge Billed; 20,000 − 2,000 = 18,000; no extra 2,000 outstanding | AC-ORT-003, AC-FIN-021 |
| Initial schedule generation | FR-ORT-001, FR-ORT-002, FR-ORT-004 | Q-ORT-1; Q-ORT-4; G10 | No charge / installment before finalize; Months equal installments from Effective − Deposit once; 5 × 2,600 = 13,000; Deposit > Effective → 422 | AC-ORT-001, AC-ORT-002, AC-ORT-004 |
| Installment application | FR-ORT-005, FR-ORT-006, FR-ORT-007 | Q-ORT-2; G5 | Manual one→many / partial / early / future; capped by allocation and installment; no oldest-first; status derived | AC-ORT-005, AC-ORT-006, AC-ORT-007 |
| Gross Total increase | FR-ORT-008, FR-ORT-009 | Q-ORT-3 | +Δ OrthoTermsChange and one PriceChange installment of Δ; discount not re-applied; 5,000 + 13,000 + 4,000 = 22,000 | AC-ORT-008, AC-ORT-009 |
| Total decrease rejection | FR-ORT-010, FR-ORT-019 | Q-ORT-3b (DEFERRED) | Rejected `ortho.total_decrease_not_supported`; nothing written; generic adjustments refused `ortho.use_terms_edit` | AC-ORT-010, AC-ORT-019 |
| Deposit / Months edit | FR-ORT-011, FR-ORT-012, FR-ORT-013 | Q-ORT-5 | Revision only; no regeneration; requires_schedule_adjustment until fixed; Months below paid regular installments → 422 | AC-ORT-011, AC-ORT-012, AC-ORT-013 |
| Schedule batch | FR-ORT-014, FR-ORT-015, FR-ORT-016, FR-ORT-017, FR-ORT-018, FR-ORT-019 | Q-ORT-5; G5(b)–(d) | Atomic; ends with Deposit + Σ active = Effective and active regular = Months; amount ≥ paid; paid installments never retired / re-dated / deleted | AC-ORT-014, AC-ORT-015, AC-ORT-016, AC-ORT-017, AC-ORT-018, AC-ORT-019 |
| Payroll sealing | FR-PAYROLL-008, FR-PAYROLL-009, FR-PAYROLL-012, FR-PAYROLL-016, FR-STAFF-016 | Q-PAY-2; G2 | Calendar month; finalize after month end; sealed columns frozen; items / adjustments blocked; Final = Base + Overtime | AC-PAYROLL-008, AC-PAYROLL-009, AC-PAYROLL-012, AC-PAYROLL-016, AC-STAFF-016 |

## 7. Out-of-Scope / Negative Traceability

One row per FRS §30 item (same order and numbering).

| # | Forbidden Item | Source | Related FR | Negative Acceptance ID | Verification |
|---|---|---|---|---|---|
| 1 | Inventory | BASE §5; GRD §2 | FR-EXP-001, FR-EXP-002 | AC-NEG-001 | no table, no column (stock, quantity, warehouse, reorder, valuation), no route, no permission, no enum, no UI control; Medical Supplies exists only as an expense-category row |
| 2 | X-ray / imaging | BASE §5; GRD §2 | FR-CLN-005 | AC-NEG-002 | no table, no column (no imaging field on implant details), no route, no job, no UI control |
| 3 | Multi-branch | Q-BRN-1 | FR-OPS-002 | AC-NEG-003 | no `branch_id` column in any schema, no branch table, no route, no UI selector |
| 4 | Assistant login / account / role / audience / self-check-in / notification | GRD §2 | FR-AUTH-001, FR-STAFF-006, FR-SEC-010, FR-NOTIF-002 | AC-NEG-004 | no ASSISTANT role, no `user_id` on `staff.assistants`, no Assistant OpenAPI document or route, no notification recipient |
| 5 | Patient portal / patient accounts | BASE §5 | FR-SEC-009 | AC-NEG-005 | no patient account table, no patient auth route; anonymous list = exactly six endpoints |
| 6 | Patient mobile app | BASE §5 | FR-SEC-009 | AC-NEG-006 | no mobile API audience, no patient route other than the public feedback token pair |
| 7 | Insurance integration | BASE §5 | FR-FIN-004 | AC-NEG-007 | no table, no column, no route; payment methods seeded only Cash, Visa, InstaPay |
| 8 | Digital consent / e-signature / consent storage | Q-DOC-2 | — (no FR; FRS §30 only) | AC-NEG-008 | no route, no table, no print endpoint, no UI control |
| 9 | Printed invoice / receipt / tax invoice | Q-DOC-1 | FR-FIN-011, FR-FIN-004 | AC-NEG-009 | no print route, no receipt / invoice table; payment confirmation is data only |
| 10 | Expense attachments | Q-EXP-1 | FR-EXP-006 | AC-NEG-010 | no attachment table, no file column, no upload route (request limit 1 MB, no uploads) |
| 11 | Daily Closing / Close Day | Q-FIN-DC | FR-REPORT-001 | AC-NEG-011 | no route, no sealed daily state column, no background job |
| 12 | Feedback expiry | Q-FDB-1 | FR-FDB-007, FR-NOTIF-009 | AC-NEG-012 | no `expires_at` column, no Expired status, no J6 job (recurring jobs = J1–J5, J7) |
| 13 | Arrived appointment status | Q-APP-4 | FR-SCH-005, FR-SCH-009 | AC-NEG-013 | no enum value (CHECK rejects), API rejects `Arrived` (400), no UI status |
| 14 | Waiting appointment status / Checked-In / CheckIn permission | Q-APP-4 | FR-SCH-005, FR-SCH-011, FR-REPORT-009 | AC-NEG-014 | no enum value, no `Scheduling.Appointments.CheckIn` permission, no check-in route, no Waiting widget |
| 15 | Walk-In commission pool | Q-PAT-1 | FR-PAYROLL-004, FR-PAT-001 | AC-NEG-015 | no pool table, no pool payroll line, no route, no job; Walk-In collection produces no commission item |
| 16 | Doctor attribution holds | Q-FIN-9 | FR-FIN-031, FR-FIN-027 | AC-NEG-016 | no hold / pending-attribution table, no PatientReassigned / DoctorDeactivated finance handler, no route |
| 17 | `separatelyPricedWork` flag | Q-CLN-2 | FR-CLN-008 | AC-NEG-017 | no column, no request member (400), no OpenAPI property |
| 18 | Self-service password reset | Q-ACC-1 | FR-AUTH-004 | AC-NEG-018 | no `/api/auth/password-reset/*` route, no reset-token column, no reset message |
| 19 | Expense Finalized state | Q-EXP-2 | FR-EXP-006 | AC-NEG-019 | no `finalized_at` column, no finalize / adjustment route |
| 20 | Percentage discounts | Q-FIN-3 | FR-FIN-021, FR-OPS-004 | AC-NEG-020 | no percentage column on discount entries, API rejects percentage input |
| 21 | Ortho Total decrease handling | Q-ORT-3b | FR-ORT-010 | AC-NEG-021 | no decrease route; terms edit rejects with `ortho.total_decrease_not_supported`; G9(b) |
| 22 | Automatic installment regeneration / redistribution / oldest-first; negative installment; installment deletion | Q-ORT-2; Q-ORT-5 | FR-ORT-006, FR-ORT-011, FR-ORT-012, FR-ORT-017, FR-ORT-019 | AC-NEG-022 | no job or route regenerates; no DELETE grant / route; CHECK amount ≥ 0; no oldest-first code path |
| 23 | Manager initial work price / consultation fee / Fixed Lab / Ortho terms | OWN Change 13 | FR-CLN-007, FR-CLN-010, FR-LAB-006, FR-ORT-002 | AC-NEG-023 | no permission mapped to MANAGER (403); revision-1 role CHECKs |
| 24 | Recalculation of old Sales commission | Q-COMM-1 | FR-PAYROLL-003, FR-STAFF-004 | AC-NEG-024 | no recalculation route or job; items unique per allocation |
| 25 | Assistant bonus / deduction / commission / incentive / proration / `final-amount` route | Q-PAY-ASST-2 | FR-PAYROLL-015, FR-PAYROLL-016, FR-STAFF-006 | AC-NEG-025 | no column, no route, no `final-amount` route, no proration code path |
| 26 | Attendance-based deductions; overtime from attendance; Sales overtime; Doctor attendance | Q-ATT-1; Q-OT-1 | FR-STAFF-012, FR-STAFF-014, FR-STAFF-015, FR-STAFF-017 | AC-NEG-026 | no FK from payroll to attendance, no Sales overtime table / column / route, no Doctor attendance column |
| 27 | Reopen-Visit route; Manager approval / Manager clinical correction | Q-CLN-1 | FR-CLN-013, FR-CLN-014, FR-RX-004 | AC-NEG-027 | no reopen route, no approval state, correction permissions not mapped to MANAGER |
| 28 | Doctor notifications / Doctor dashboard / Doctor feedback access | SEC §4 note 3 | FR-NOTIF-007, FR-REPORT-009, FR-FDB-011, FR-SEC-011 | AC-NEG-028 | no permission mapped to DOCTOR (403), no Doctor dashboard route |
| 29 | Language switch / non-Arabic UI | Q-UI-1 | FR-OPS-001 | AC-NEG-029 | no locale selector UI control, SPA root `dir=rtl lang=ar` |
| 30 | Automated WhatsApp / SMS / email delivery | BASE §5 | FR-NOTIF-001, FR-FDB-004 | AC-NEG-030 | only `InAppChannel` registered, no external channel job |
| 31 | Unconfirmed proposals (ILabAllocationStrategy, CaseDoctorAllocation, TreatmentPlan / FinancialAgreement, OnHold / Abandoned, addendum, PatientContact aggregate, approval workflows, price lists, deviation reports, clawback) | GRD §2 | FR-CLN-011, FR-OPS-002 | AC-NEG-031 | no type, table, enum value, route or job with these names (architecture scan) |
| 32 | Generic Medical History / Chronic Diseases / Previous Surgeries fields; medical detail / other-condition fields | GRD §4a | FR-MED-001 | AC-NEG-032 | no column on `patients.patients`, no DTO property (`medicalHistory`, `chronicDiseases`, `previousSurgeries`) |
| 33 | Rendered diagram files in the design package | OWN Change 11 | — (no FR; FRS §30 only) | AC-NEG-033 | no `.png` / `.svg` / `.jpg` / rendered folder in the repository (file-system check) |

## Traceability Coverage Report

Computed mechanically from docs/FRS.md and this file.

| Check | Result |
|---|---|
| Total FRs in FRS | 237 |
| Total FRs traced | 237 |
| Missing FRs | 0 |
| Duplicate FRs | 0 |
| Unknown FRs (main matrix) | 0 |
| Unknown FRs (privacy / finance / negative tables) | 0 |
| Total FLOWs traced | 16 of 16 (FLOW-01 … FLOW-16) |
| Direct FLOW references (FRS §29) missing | 0 |
| FRs without Acceptance IDs | 0 |
| Forbidden items (FRS §30) traced | 33 of 33 |
| Forbidden items without negative tests | 0 |
| Privacy boundaries traced with negative tests | 10 |
| High-risk Finance / Ortho rules traced | 22 |
| Acceptance IDs allocated | 280 (237 primary + 43 negative / privacy AC-NEG) |
| FRs not a step of any FLOW (verified by own AC) | 101 |
| Deferred items | 2 (Q-ORT-3b, OPS-1) |
| Unresolved traceability gaps | 0 |

**Notes (not traceability gaps):**

- Forbidden items #8 (digital consent) and #33 (rendered diagram files) have no dedicated FR; they are traced directly from FRS §30 to AC-NEG-008 and AC-NEG-033.
- AC-NEG-033 is a repository check. At the time of writing the repository still tracks `docs/rendered-diagrams/` (PNG / SVG) and `docs/README (1).md`, which the frozen DESIGN_FREEZE.md records as removed. AC-NEG-033 will fail until that package-hygiene item is resolved through the controlled change process; this matrix does not change any file to fix it.
- Every FLOW is also verified end-to-end in Task 5 (FRS §32 item 2).
