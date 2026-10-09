# DATABASE_DESIGN.md

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL
**Source:** Step 7 as approved, plus every later schema correction (work items, ortho allocation chain, age handling, fixed lab per case, `doctor_earnings`, attribution holds, one role per account, password-reset flag, Worker views, backup and Data Protection grants). Obsolete Step 7 structures are not reproduced.

Labels: **CONFIRMED** = a business rule the user stated. **TECHNICAL** = engineering choice with no business meaning. **DEFAULT** = a technical default that can be vetoed. **OPEN** = undecided, nothing is implemented.

There is no Inventory table, no X-ray table and no imaging column anywhere in this schema. "Medical Supplies" exists only as an expense-category row.

---

## 1. Conventions

| Item | Convention |
|---|---|
| Keys | `uuid` primary keys (time-ordered). Human-readable codes only where confirmed (`patient_code`). |
| Money | `numeric(12,2)`, EGP. Percentages are `numeric(5,2)` with `CHECK 0..100`. Application arithmetic uses `decimal`, never floating point. |
| Time | `timestamptz` (UTC) for moments. `date` for calendar dates (due dates, payroll periods), interpreted in the clinic-local timezone. |
| Every table | `created_at`, plus `created_by_user_id` where a person acts. |
| Concurrency | **RV** = PostgreSQL `xmin` row-version token (API: ETag / `If-Match`). |
| Deletes | No physical deletes in business schemas. All FKs are `ON DELETE NO ACTION`; no cross-schema cascades. Soft markers (`voided_at`, `removed_at`, `withdrawn_at`, `is_active`) where needed. |
| **H** | Historical, insert-only. `clinic_app` has no UPDATE or DELETE grant. |
| **L** | Lifecycle. Only the named columns may change. Everything else is frozen. |
| **M** | Mutable current state. Updates allowed; every change is audited with old and new values. |
| **A** / **A+** | **A** = automatic audit through the SaveChanges interceptor. **A+** = additionally an explicit semantic audit event. |

## 2. Cross-module references, schemas and migrations

- **Cross-module EF navigation: avoid.** Module entities are `internal`; cross-module references are scalar id properties.
- **Database foreign key constraint: keep.** Every reference to a business entity is a real PostgreSQL FK. All cross-schema FKs are declared in one reviewable file, `CrossModuleReferences.cs`, in the persistence host project, using one-sided `HasOne<Other>().WithMany().HasForeignKey(...)` with no navigation properties. An architecture test compares that file to the database.
- **FK direction is free.** Only code dependencies follow the module levels. `finance.patient_charges → clinical.treatment_cases` is a valid FK even though Finance never calls Clinical code.
- **No duplicate entities.** A business entity lives in exactly one table. The only deliberate copies are listed in section 20.
- **Polymorphic references (no FK, intentional):** `audit.audit_log.entity_id`, `notifications.related_entity_id`.
- **Schema = module.** Business schemas: `identity`, `audit`, `configuration`, `staff`, `patients`, `scheduling`, `clinical`, `prescriptions`, `orthodontics`, `crm`, `finance`, `expenses`, `payroll`, `notifications`, `feedback`. Infrastructure schemas: `infra` (migration history, Data Protection keys, guard functions) and `hangfire`.
- **Migrations.** One DbContext, one history table in `infra`. Each migration is named `<date>_<Module>_<Change>`; a migration touches one module's schema, except cross-module FK additions named `<date>_Xref_<Change>`. A test fails if any table's schema differs from its owning module.
- **Database roles.** `clinic_migrator` (DDL, run by CI/migrator only), `clinic_app` (DML only: no DDL, no DELETE on business schemas, no UPDATE on **H** tables), `clinic_worker` (least privilege, section 10), `clinic_backup` (explicit SELECTs, section 10).

---

## 3. identity

| Table (PK) | Key columns | Constraints / indexes | Mutability and audit |
|---|---|---|---|
| **identity.users** (id) | user_name, password_hash (**nullable**), security_stamp, is_active, **password_reset_required** boolean, lockout fields | Unique normalized user_name | **M**. Login, failed login, deactivation, password reset are **A+**. Deactivation sets `is_active=false` and rotates the stamp (ends sessions immediately). Audit never stores passwords, hashes or reset tokens. |
| **identity.roles** (id) | name (`MANAGER`, `DOCTOR`, `SALES_RECEPTION`) | Unique name | **M**, seeded, **A**. The role name constants are what code uses; no role UUID is hardcoded. |
| **identity.user_roles** (user_id, role_id) | | **Unique (user_id)** → exactly one operational role per account. Both FKs. | **M**, **A**. Role change is a Manager action, **A+**, rotates the stamp. |
| **identity.permissions** (key) | e.g. `Finance.Discount.Set` | | Seeded by deployment |
| **identity.role_permissions** (role_id, permission_key) | | Both FKs. Migration test and startup test: no contact-bearing key may map to `DOCTOR`. | **M**, **A** |
| **identity.v_active_user_roles** (view) | user_id, role_name (active users only) | Owned by `clinic_migrator`; **not** `security_invoker` | Read-only. Gives the Worker role resolution without any privilege on the base tables. |

`password_hash = NULL` with `password_reset_required = true` is the "recovery state": login is refused until a new reset completes (used by post-restore lockdown, DEPLOYMENT_ARCHITECTURE.md).

## 4. audit

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **audit.audit_log** (id bigint) | occurred_at, user_id, action, entity_type, entity_id (text), previous_value jsonb, new_value jsonb, is_restricted, ip, user_agent | FK user_id → identity.users (nullable for unknown-user failed logins). `entity_id` has no FK. Indexes: occurred_at; (entity_type, entity_id); (user_id, occurred_at). | **H**. `clinic_app` has INSERT and SELECT only. Rows holding contact values are flagged `is_restricted`. Only the Manager can read the log. Never contains passwords, hashes, tokens. |

## 5. configuration

| Table (PK) | Key columns | Constraints | Mutability |
|---|---|---|---|
| **configuration.settings** (key) | value, value_type, updated_by, RV | | **M**, **A**. Keys: `clinic_timezone`, `referral_reason_visible_to_sales` (unset = hidden), `doctor_dob_visible` (default visible), `feedback_link_expiry_days` (unset), `default_appointment_minutes` (unset), clinic name/logo/contact for print. |
| **configuration.lab_percentage_history** (id) | percentage, effective_from, set_by_user_id | | **H**. Current rate = latest row. Each completion records the rate it used. |
| **configuration.payment_methods** (code) | display_name, is_active | Seeded: Cash, Visa, InstaPay | **M**, **A** |

## 6. staff

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **staff.doctors** (id) | full_name, specialty (nullable), **current_percentage**, is_active, deactivated_at | FK user_id → identity.users, unique. Index (is_active). | **M**, **A+** on percentage changes. Work snapshots copy this value; nothing refers back to it. |
| **staff.sales_employees** (id) | full_name, base_monthly_salary, commission_percentage, is_active, deactivated_at | FK user_id → identity.users, unique | **M**, **A+** on salary/percentage changes |
| **staff.assistants** (id) *(v1.1)* | full_name, **current_salary** (numeric(12,2), CHECK > 0), is_active, deactivated_at, created_at, row version (`xmin` ETag) | **No `user_id`, no FK to `identity`.** Index (is_active). CHECK (is_active OR deactivated_at IS NOT NULL). | **M**, **A+** on creation, salary change and activation/deactivation. Never deleted. Payroll copies `current_salary` into its own snapshot; nothing refers back to this column. No salary-history table (V1): history = payroll snapshots + audit trail. |
| **staff.v_sales_employee_activity** (view) | sales_employee_id, is_active | Owned by `clinic_migrator`; **not** `security_invoker` | Read-only. Only object the Worker may read in `staff`. Exposes no salary, commission, percentage, profile or contact data. |

The Manager has a user account and no staff row. **Assistants have a staff row and no user account (v1.1)**: they are outside the user-to-staff consistency rule below, and a DOCTOR / SALES_RECEPTION / MANAGER user can never be linked to an assistant. Employees are never deleted. Staff `is_active` and the user's `is_active` change together. A DOCTOR user must have a `staff.doctors` row and no sales row; SALES_RECEPTION the reverse; MANAGER neither (command-handler check + startup consistency test).

## 7. patients

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **patients.patients** (id) | patient_code, full_name, date_of_birth (nullable), **age_years_recorded** smallint (nullable), **age_recorded_on** date (nullable), medical_history, chronic_diseases, previous_surgeries, chief_complaint, registered_at, is_active, RV | Unique patient_code. CHECK: age columns both set or both null; 0..120; **at least one of date_of_birth / age_years_recorded present**. Trigram index on lower(full_name). | **M**, **A**. Record opens are **A+**. Contains no contact data. A date of birth is never inferred from an age. |
| **patients.patient_contacts** (patient_id, 1:1) | phone, whatsapp, email, address, crm_notes, original_sales_employee_id (**nullable**), RV | FK patient_id → patients; FK original_sales_employee_id → staff.sales_employees. Index on phone (prefix search). | **M**, **A** with `is_restricted` audit rows. Doctor-facing queries never touch this table. Direct registration leaves `original_sales_employee_id` NULL; lead conversion copies the lead's value once. |
| **patients.patient_doctor_assignments** (id) | patient_id, doctor_id, previous_doctor_id, assigned_at, ended_at, assigned_by_user_id, reason | FKs to patients, staff.doctors (both doctor columns), identity.users. **Partial unique (patient_id) WHERE ended_at IS NULL** = one current doctor. Index (patient_id, assigned_at desc). | **L**. Only `ended_at` is set, once, in the same transaction that inserts the successor. **A+**. Historical work never references this table. |

**Age display (DTO rule):** DOB present → current age computed from DOB. Only a recorded age → "Age recorded at registration: N (recorded on D)"; never aged forward. Same for Doctor DTO, Sales DTO, Manager view and prescription print model.

## 8. scheduling

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **scheduling.availability_weeks** (id) | doctor_id, week_start_date, status (Draft/Published), published_at, published_by_user_id, RV | Unique (doctor_id, week_start_date) | **L**. **A+**. |
| **scheduling.availability_ranges** (id) | week_id, day_date, start_time, end_time, withdrawn_at | **Exclusion constraint** (btree_gist): no overlapping ranges for the same doctor/day where `withdrawn_at IS NULL` | **L** (`withdrawn_at` only). **A**. |
| **scheduling.appointments** (id) | patient_id, doctor_id, start_at, duration_minutes (nullable), status (**7 confirmed values**: Scheduled, Confirmed, InTreatment, Completed, Cancelled, NoShow, Rescheduled; CHECK rejects any other value, including Arrived and Waiting), reason, sales_notes, kind (Standard / ImplantFollowUp), previous_appointment_id, status_changed_at, status_changed_by_user_id, status_change_reason, created_by_user_id, RV | FKs to patients, staff.doctors, identity.users; self-FK previous_appointment_id (unique). **Partial unique (doctor_id, start_at)** WHERE status NOT IN ('Cancelled','Rescheduled'). Indexes: (doctor_id, start_at); (patient_id, start_at desc); (start_at); (status, start_at). | **L**. `start_at`, `doctor_id`, `patient_id`, `reason` frozen; only status and status-change fields move. **No arrival / check-in columns exist** (`arrived_at` and `checked_in_by_user_id` were removed in v1.1; any future arrival tracking waits for Q-APP-4). Nothing is implemented yet, so no data migration is involved. A reschedule marks the old row Rescheduled and inserts a new row pointing back. **A+**. |

`reason` is Doctor-visible; `sales_notes` is Sales/Manager only. `kind` is a discriminator so the reminder job can emit "Implant Follow-Up Tomorrow". Duration (Q-APP-1) and Manager override (Q-APP-2) are OPEN, so the constraint is deliberately same-slot uniqueness; an overlap exclusion can replace it additively later.

## 9. clinical

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **clinical.procedure_types** (id) | code, name, category, lab_model (General/Fixed), option_definition jsonb, is_active | Unique code | **M**, **A** |
| **clinical.visits** (id) | patient_id, doctor_id, appointment_id (nullable), status (Draft/InProgress/Finalized), started_at, finalized_at, finalized_by_user_id, clinical_notes, RV | FKs to patients, staff.doctors, scheduling.appointments (unique when not null). Indexes: (patient_id, started_at desc); (doctor_id, started_at). | **L** (status, finalized_*, and `clinical_notes` until finalized). **A+** on finalize. |
| **clinical.treatment_cases** (id) | patient_id, procedure_type_id, **started_by_doctor_id**, status (Planned/Active/Completed/Cancelled), teeth text[], area, details jsonb, lab_model_snapshot, started_at, completed_at, RV | FKs to patients, procedure_types, staff.doctors. **Unique (id, patient_id).** Indexes: (patient_id); (started_by_doctor_id, status); (procedure_type_id). | **L** (status, completed_at). **A+** on status change. `started_by_doctor_id` is clinical context only (no revenue meaning). No price, percentage or fixed-lab column on the case. |
| **clinical.case_work_items** (id) | treatment_case_id, patient_id, **performing_doctor_id**, **doctor_percentage_snapshot** numeric(5,2), entered_price numeric(12,2), treatment_record_id (nullable), created_at, RV | Composite (treatment_case_id, patient_id) → treatment_cases. performing_doctor_id → staff.doctors. Composite **(treatment_record_id, treatment_case_id, patient_id) → treatment_records (id, treatment_case_id, patient_id)**. Composite **(treatment_record_id, performing_doctor_id) → treatment_records (id, doctor_id)**. **Unique (id, treatment_case_id, performing_doctor_id).** Indexes: (treatment_case_id); (performing_doctor_id, created_at). | **L**, guard **G6**. Frozen from creation: snapshot, performing doctor, case. `entered_price` editable and audited only until a charge references the item. **A+**. No `entered_fixed_lab` column. |
| **clinical.treatment_records** (id) | visit_id, treatment_case_id, patient_id, doctor_id | Composite FKs (visit_id, patient_id) and (treatment_case_id, patient_id). **Unique (visit_id, treatment_case_id)**, **(id, treatment_case_id, patient_id)**, **(id, doctor_id)**. Index (treatment_case_id). | Guarded by **G1** once the visit is finalized. **A**. |
| **clinical.treatment_steps** (id) | treatment_case_id, step_code (AccessCavity / MechanicalPreparation / Obturation / PostAndCore), status (Pending/Completed), completed_at, completed_in_visit_id, completed_by_doctor_id | Unique (case, step_code). FKs to cases, visits, doctors. | **L**. Pending → Completed once. **G1**. Post & Core exists only if the Doctor adds it. **A**. |
| **clinical.root_canal_measurements** (id) | treatment_case_id, canal_number (1–5), length_mm numeric(5,2), recorded_in_visit_id, recorded_by_doctor_id | Unique (case, canal_number, visit). Latest row per canal = current. | **H** after the visit is finalized (**G1**). **A**. |
| **clinical.implant_details** (treatment_case_id, 1:1) | has_bone_graft, has_membrane, procedure_date, **healing_months** `CHECK 1..6` | FK to cases | **M**, **A**. Assigned doctor = `treatment_cases.started_by_doctor_id`. No imaging field. |

Every procedure is a `treatment_case` (filling, root canal, implant, ortho, …); a case spans visits through `treatment_records`.

**Catalog mapping** (validated against `option_definition`):

| Procedure type | `details` content |
|---|---|
| Root Canal | tooth in `teeth`; canals and steps in their own tables |
| Filling | `filling_type` GIC or Composite; `composite_zone` Anterior/Posterior **required only if Composite** |
| Extraction | `extraction_type` Simple / Surgical / AdvancedSurgical (no sutures or bone-removal fields) |
| Pediatric: Pulpotomy, Pulpectomy, Space Maintainer, Stainless Steel Crown, Extraction, Zircon Crown, Other | Pediatric Extraction has **no** type. Pediatric Other has free-text name and details |
| Implant | `implant_details`. Lab model Fixed |
| Fixed Prosthetics | `material`: Porcelain/PFM, Zirconia, E-max. Lab model Fixed |
| Removable Prosthetics | `type`: Complete/Partial; `material`: Flexible Acrylic / Standard Acrylic. Lab model Fixed |
| Scaling/Cleaning, Deep Cleaning, Polishing | No options |
| Laser Bleaching/Whitening | `quality`: Basic / HighQuality (only this type) |
| Orthodontics | `orthodontics.ortho_cases` |
| Other | Free-text `name` and `details` |

Everything not listed as Fixed uses the General lab model (literal reading: Fixed = Implant, Fixed Prosthetics, Removable Prosthetics). Orthodontics is General (TECHNICAL placeholder).

## 10. prescriptions

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **prescriptions.prescriptions** (id) | patient_id, doctor_id, visit_id, general_notes, voided_at, RV | FKs to patients, doctors, visits; composite (visit_id, patient_id). Indexes (patient_id, created_at desc); (visit_id). | **M while the visit is not finalized.** After finalization guarded by **G1**; every change **A** with previous version in the audit entry, flagged `after_finalization`. The post-finalization correction workflow is **OPEN (Q-CLN-1)**. |
| **prescriptions.prescription_items** (id) | prescription_id, sort_order, medication_name, dosage, frequency, duration, instructions, removed_at | FK prescription_id. Index (prescription_id). | Same as above. Soft-removed (`removed_at`), never deleted. |

The printable prescription is generated on demand from a print model with no contact fields and is not stored.

## 11. orthodontics

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **orthodontics.ortho_cases** (treatment_case_id, 1:1) | area (UpperArch/LowerArch/Appliance), appliance_type (free text), **deposit_amount (nullable)**, **number_of_months (nullable)**, **charge_id (nullable, unique)** | FK to clinical.treatment_cases. Composite **(treatment_case_id, charge_id) → finance.patient_charges (treatment_case_id, id)**. Unique (treatment_case_id, charge_id). CHECK appliance_type present when area = Appliance. | **M**, **A+**. The three terms columns are NULL until the actor who sets terms is decided (OPEN: `Orthodontics.Terms.Set` assigned to no role). Total price is **not** stored here (`patient_charges.final_price`). Remaining = charge final price − deposit (derived). |
| **orthodontics.ortho_installments** (id) | ortho_case_id, installment_number, due_date, amount_due | FK ortho_cases. **Unique (case, number)** and **(id, ortho_case_id)**. Index (due_date). CHECK amount_due > 0. | **H**. One row per installment so custom schedules fit. Changes are controlled, audited actions (Q-ORT-1, Q-ORT-3 OPEN). |
| **orthodontics.installment_payments** (id) | installment_id, ortho_case_id, **payment_allocation_id**, charge_id, amount_applied numeric(12,2) | Composite **(installment_id, ortho_case_id)** → ortho_installments. Composite **(ortho_case_id, charge_id)** → ortho_cases. Composite **(payment_allocation_id, charge_id)** → finance.payment_allocations. **Unique (installment_id, payment_allocation_id).** Indexes (payment_allocation_id); (installment_id). | **H**, **A**. Guard **G5**. **Unused until Q-ORT-2 is answered** (no endpoint maps to it). |

The FK chain guarantees money allocated to another charge cannot be applied to this case's installments. `ortho_case_id` and `charge_id` repeated here are FK-checked copies, not independent truths.

**Installment status** (Upcoming/Due/Partially Paid/Paid/Overdue) is derived at read time from clinic-local date, `due_date` and applied amounts; never stored. Schedule generation takes the first due date as input; initial generated installments are equal, rounded to 2 decimals, with the final installment absorbing any rounding difference (TECHNICAL); whether individual amounts may later be customized remains OPEN under Q-ORT-1 and no customization route exists.

## 12. crm

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **crm.leads** (id) | name, phone, source, interested_service, **original_sales_employee_id**, notes, next_follow_up_date, status (8 confirmed values), converted_patient_id, RV | FKs to staff.sales_employees and patients (unique when not null). Indexes (status); (original_sales_employee_id, next_follow_up_date); phone. | **M**; `original_sales_employee_id` frozen. **A**. |
| **crm.referrals** (id) | patient_id, referring_doctor_id, referral_type (Surgery/Implant/Orthodontics/Prosthetics/Other), reason, notes, follow_up_instructions, follow_up_status (New/Contacted/Booked/Completed/Lost), created_at, RV | FKs to patients, staff.doctors. Indexes (follow_up_status, created_at); (patient_id). | **L** (follow_up_status and Sales follow-up work). `notes` Doctor-side. `reason` visible to Sales only per setting. `follow_up_instructions` carries the confirmed Sales-visible instructions. |
| **crm.follow_ups** (id) | patient_id (nullable), follow_up_type (6 confirmed sources), assigned_sales_employee_id, due_date, status, notes, result, next_follow_up_date, optional source FKs lead_id, referral_id, appointment_id, installment_id, charge_id | FKs to patients, leads, referrals, scheduling.appointments, orthodontics.ortho_installments, finance.patient_charges, staff.sales_employees. CHECK at most one source FK; patient_id or lead_id present. Indexes (assigned_sales_employee_id, status, due_date); (due_date). | **M**, **A** |
| **crm.follow_up_contact_attempts** (id) | follow_up_id, attempted_at, attempted_by_user_id, outcome, notes | FK follow_ups. Index (follow_up_id, attempted_at). | **H** (never overwritten) |

The Original Sales Representative on a referral is **derived** from `patients.patient_contacts`. Doctors never receive it.

## 13. finance

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **finance.patient_charges** (id) | patient_id, charge_kind (Treatment/Consultation), treatment_case_id (nullable), case_work_item_id (nullable), visit_id (nullable), performing_doctor_id (nullable), doctor_percentage_snapshot (nullable), lab_model (nullable), original_price, discount_amount, **final_price** (generated = original − discount), discount_set_by_user_id (nullable), charged_at | FKs to patients, visits. Composite (treatment_case_id, patient_id) → clinical.treatment_cases. Composite **(case_work_item_id, treatment_case_id, performing_doctor_id) → clinical.case_work_items**. **Unique (id, patient_id)**, **(id, treatment_case_id)**, **(treatment_case_id, id)**, **(id, charge_kind)**. **Partial unique (case_work_item_id) WHERE charge_kind='Treatment'** (one charge per work item). CHECK 0 ≤ discount ≤ price. CHECK: Treatment charges require case_work_item_id, performing_doctor_id, doctor_percentage_snapshot; Consultation charges require all three NULL. Indexes (patient_id, charged_at desc); (performing_doctor_id, charged_at); (treatment_case_id); (charge_kind, charged_at). | **H**. The established agreement; never updated. Corrections go through `charge_adjustments`. **A+**. A case may have **many** treatment charges (one per work item). |
| **finance.payments** (id) | patient_id, amount, payment_method_code, paid_at, received_by_user_id, idempotency_key, reverses_payment_id | FKs to patients, configuration.payment_methods, identity.users, self-FK. **Unique idempotency_key.** Unique (id, patient_id). CHECK positive amount when `reverses_payment_id` is null, negative when set. Indexes (patient_id, paid_at desc); (paid_at, payment_method_code). | **H**. A correction is a new negative payment. Primitive only; the refund workflow is **OPEN (Q-PAY-1)**. **A+**. |
| **finance.payment_allocations** (id) | payment_id, charge_id, patient_id, amount | Composite (payment_id, patient_id) → payments; (charge_id, patient_id) → charges. **Unique (payment_id, charge_id)**, **(id, charge_id)**, **(id, charge_id, amount)**. Index (charge_id). | **H**. Σ allocations of a payment ≤ the payment (V1 default: equal). |
| **finance.doctor_earnings** (id) | payment_allocation_id, charge_id, charge_kind, attributed_doctor_id, allocated_amount, doctor_percentage_applied, doctor_share, rule_version, created_at | Composite **(payment_allocation_id, charge_id, allocated_amount) → payment_allocations (id, charge_id, amount)**. Composite (charge_id, charge_kind) → patient_charges. attributed_doctor_id → staff.doctors. **Unique payment_allocation_id**, **(id, allocated_amount, doctor_share)**. CHECK charge_kind='Treatment'; doctor_share same sign as allocated_amount. Indexes (attributed_doctor_id, created_at); (charge_id). | **H**, **A+**. One row per allocation to a treatment charge (not held, see below). `doctor_share = round_half_up(allocated × snapshot / 100, 2)`. **No lab value enters it.** Reversal = exact negation. |
| **finance.revenue_distributions** (id) | doctor_earning_id, allocated_amount, doctor_share, lab_amount, clinic_share, lab_percentage_applied (nullable), lab_model, rule_version, created_at | Composite **(doctor_earning_id, allocated_amount, doctor_share) → doctor_earnings (id, allocated_amount, doctor_share)**. **Unique doctor_earning_id.** **CHECK doctor_share + lab_amount + clinic_share = allocated_amount.** Index (created_at). | **H**. The Lab/Clinic **completion** of an earning. "Pending completion" = the absence of a row. Clinic Share is the remainder. |
| **finance.fixed_lab_expenses** (id) | treatment_case_id, patient_id, entered_by_doctor_id, created_at | Composite (treatment_case_id, patient_id) → clinical.treatment_cases. entered_by_doctor_id → staff.doctors. **Unique (treatment_case_id)**, **(id, treatment_case_id)**. | **H**, **A+**. Holds no amount. Created when the Doctor enters the initial amount on a Fixed-lab case. Guard **G7**. |
| **finance.fixed_lab_revisions** (id) | fixed_lab_expense_id, treatment_case_id, revision_number, amount, entered_by_user_id, entered_at, reason | Composite (fixed_lab_expense_id, treatment_case_id) → fixed_lab_expenses (id, treatment_case_id). entered_by_user_id → identity.users. **Unique (fixed_lab_expense_id, revision_number)**. CHECK amount ≥ 0. Index (fixed_lab_expense_id, revision_number desc). | **H**. Revision 1 = Doctor's initial entry; a Manager edit inserts the next revision with reason; **A+**. Current = latest. No fixed-lab amount is copied to charges or distributions. |
| **finance.charge_adjustments** (id) | charge_id, amount_delta, reason, created_by_user_id | FK charge_id. Index (charge_id). | **H**, **A+**. Not wired to any endpoint (Q-PAY-1). |
| **finance.attribution_holds** (id) | charge_id, reason (PatientReassigned / DoctorDeactivated), reassignment_id (nullable), deactivated_doctor_id (nullable), created_at | FKs charge_id → patient_charges; reassignment_id → patients.patient_doctor_assignments; deactivated_doctor_id → staff.doctors. CHECK exactly one source column set, matching the reason. **Unique (charge_id, reassignment_id)**, **(charge_id, deactivated_doctor_id)**, **(id, charge_id)**. | **H**, **A+** |
| **finance.pending_doctor_attributions** (id) | payment_allocation_id, charge_id, attribution_hold_id, created_at | Composite (payment_allocation_id, charge_id) → payment_allocations (id, charge_id). Composite (attribution_hold_id, charge_id) → attribution_holds (id, charge_id). **Unique payment_allocation_id.** | **H**. "Pending" is derived: a pending row with no `doctor_earnings` row. |

Consultation charges produce **no** earning and **no** distribution rows ("consultation fees do not participate in Doctor commission"). Fixed-lab charges produce an earning immediately (when attribution is clear) and **no** distribution row until Q-FIN-1 is answered. Held allocations produce neither.

## 14. expenses

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **expenses.expense_categories** (id) | name, is_active | Unique name. Seeded with the 11 confirmed categories. | **M**, **A** |
| **expenses.clinic_expenses** (id) | expense_date, category_id, description, amount, payment_method_code, paid_to_vendor, notes, finalized_at, adjusts_expense_id | FKs to categories, payment methods, identity.users, self-FK for adjustments. Indexes (expense_date); (category_id, expense_date). | **L**. Editable and audited until `finalized_at`; afterwards amount/date/category are frozen (**G3**) and changes are adjustment rows (signed amount). What triggers finalization is not defined. |

## 15. payroll

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **payroll.doctor_payrolls** (id) | doctor_id, period_start, period_end, revenue_total, lab_total, doctor_share_total, clinic_share_total, adjustments_total, final_amount (all sealed at finalization), status, finalized_at, paid_at | FK doctors. Unique (doctor_id, period_start, period_end). Index (status). | **L**. Status, finalized_at, paid_at may change. The seven sealed numeric columns freeze once `finalized_at` is set (**G2**). Payout derives from `doctor_share_total` and adjustments only; `lab_total`/`clinic_share_total` are informational. **A+**. |
| **payroll.doctor_payroll_items** (id) | doctor_payroll_id, **doctor_earning_id** | FKs to payrolls and finance.doctor_earnings. **Unique doctor_earning_id** (an earning is paid at most once). | **H** |
| **payroll.doctor_payroll_adjustments** (id) | doctor_payroll_id, amount, reason | FK payroll | **H**, blocked after finalization by **G2** |
| **payroll.sales_payrolls** (id) | sales_employee_id, period_month, base_salary_snapshot, commission_percentage_snapshot, commission_total, bonus_total, deductions_total, net_salary (generated), status, finalized_at, paid_at | FK sales_employees. Unique (sales_employee_id, period_month). | **L**, same sealing as doctor payroll. Sales sees only own rows. |
| **payroll.sales_payroll_commission_items** (id) | sales_payroll_id, patient_id, base_amount, commission_amount, basis_description | FKs to payroll and patients. Index (sales_payroll_id). | **H**. The base is whatever Q-FIN-2 decides (OPEN). |
| **payroll.sales_payroll_adjustments** (id) | sales_payroll_id, kind (Bonus/Deduction), amount, reason | FK payroll | **H**, guarded like **G2** |
| **payroll.assistant_payrolls** (id) *(v1.1)* | assistant_id, period_start, period_end, **salary_snapshot**, **final_amount**, status, finalized_at, paid_at, created_at | FK `staff.assistants`. CHECK period_end ≥ period_start. **CHECK salary_snapshot > 0.** **CHECK final_amount = salary_snapshot** (V1: salary only; no free amount; no bonus, deduction, overtime or other component). Unique (assistant_id, period_start, period_end). **Exclusion constraint** (`btree_gist`): no two payrolls for the same assistant with overlapping `daterange(period_start, period_end, '[]')` (technical integrity rule against duplicate salary expense). Index (status). | **L**. Status, finalized_at, paid_at may change. `salary_snapshot` and `final_amount` freeze once `finalized_at` is set (**G2**). **A+** on create, finalize, mark-paid. |

**Assistant tables: grant, backup and module-ownership decisions (v1.1).** `staff.assistants` belongs to the **Staff** module and `payroll.assistant_payrolls` to the **Payroll** module; no new module and no new schema. Neither is a Finance-ledger table and neither alters one.

| Object | `clinic_app` | `clinic_worker` | `clinic_backup` | Other |
|---|---|---|---|---|
| `staff.assistants` | SELECT, INSERT; UPDATE only on `full_name`, `current_salary`, `is_active`, `deactivated_at` (row version is `xmin`). No DELETE. | **No access.** The Worker's only `staff` object remains `staff.v_sales_employee_activity`; it does not expose Assistants. | SELECT (explicit; covered by "every table in the business schemas" and stated here as the backup-grant decision) | Owned by `clinic_migrator`. Salary-column writes are exposed only through the route guarded by `Staff.AssistantSalary.Configure`. |
| `payroll.assistant_payrolls` | SELECT, INSERT; UPDATE only on `salary_snapshot`, `final_amount`, `status`, `finalized_at`, `paid_at`, with **G2** sealing the two money columns once `finalized_at` is set. No DELETE. | **No access.** | SELECT (explicit backup-grant decision) | Owned by `clinic_migrator`. |

Audit entries (written in the same transaction, **A+**): Assistant created, Assistant updated, **salary changed** (old and new value), Assistant deactivated, Assistant reactivated, payroll draft created, payroll finalized, payroll marked paid. An audit entry never alters a payroll snapshot.

**Appointment status database tests (v1.1):** (1) inserting or updating an appointment with status `Arrived` is rejected by the CHECK; (2) likewise `Waiting`; (3) the seven allowed values are accepted; (4) `scheduling.appointments` has no `arrived_at` or `checked_in_by_user_id` column; (5) the partial unique index (doctor_id, start_at) still excludes only Cancelled and Rescheduled.

**Assistant database tests (v1.1):** (1) `staff.assistants` has no `user_id` or any FK to `identity`, and creating an Assistant inserts no `identity.users` row; (2) two payrolls for one Assistant with overlapping periods are rejected by the exclusion constraint (identical, partially overlapping, nested and adjacent-sharing-a-day periods), non-overlapping periods succeed; (3) `period_end < period_start`, `salary_snapshot ≤ 0`, `current_salary ≤ 0` and `final_amount ≠ salary_snapshot` are rejected; (4) after `finalized_at` is set, an UPDATE of `salary_snapshot` or `final_amount` fails while `status`/`paid_at` succeed (G2); (5) changing `current_salary` leaves every existing payroll row byte-identical; (6) `clinic_app` cannot DELETE from either table; (7) `clinic_worker` cannot SELECT either table; (8) `clinic_backup` can SELECT both and write neither, and a dump restores both with constraints.

Payroll status placeholders: Draft, Finalized, Paid (TECHNICAL, from the confirmed fields status / finalized-at / paid-date).

## 16. notifications and feedback

| Table (PK) | Key columns | Constraints / indexes | Mutability |
|---|---|---|---|
| **notifications.notifications** (id) | notification_type (7 confirmed), recipient_user_id, recipient_role, patient_id, related_entity_type, related_entity_id, **dedupe_key**, scheduled_for, sent_at, read_at, status (Pending/Sent/Failed/Cancelled), retry_count, last_error | FKs to identity.users and patients. `related_entity_id` no FK. **Unique dedupe_key.** Partial index (scheduled_for) WHERE status='Pending'. Index (recipient_user_id, read_at). | **L** (status, sent_at, read_at, retry_count, last_error). One row per recipient user. No patient name/phone stored. `last_error` scrubbed, truncated to 500 chars. Doctors are never recipients (no Doctor inbox in V1). |
| **feedback.feedback_requests** (id) | patient_id, visit_id, doctor_id, link_token_hash, link_sent_at, sent_by_user_id, delivery_channel, status (Pending/Sent/Submitted/Expired), submitted_at, rating, comment, expires_at (nullable) | FKs to patients, clinical.visits (unique), doctors, users. Unique link_token_hash. Index (status). | **L**. Only the token hash is stored. `expires_at` stays NULL until Q-FDB-1 is answered. |

## 17. infra

| Object | Notes |
|---|---|
| `infra.__EFMigrationsHistory` | Single migration history. `clinic_backup`: SELECT. |
| `infra.data_protection_keys` | Data Protection key ring. `clinic_app`: **SELECT, INSERT only**. `clinic_backup`: **no grant**. Excluded from the dump (`--exclude-table-data`). |
| Guard functions G1–G7 | Owned by `clinic_migrator` |
| `hangfire` schema | Hangfire PostgreSQL storage; Worker role writes only here and to `notifications` as listed |

---

## 18. Data-integrity rules and guards

| Rule | Mechanism |
|---|---|
| **R1** Ledger and history tables are insert-only | `patient_charges`, `payments`, `payment_allocations`, `doctor_earnings`, `revenue_distributions`, `fixed_lab_expenses`, `fixed_lab_revisions`, `charge_adjustments`, `attribution_holds`, `pending_doctor_attributions`, `lab_percentage_history`, `installment_payments`, `ortho_installments`, `follow_up_contact_attempts`, payroll item/adjustment tables, `root_canal_measurements` (after finalization), `audit_log`: `clinic_app` has no UPDATE/DELETE grant. |
| **R2** Visit-owned clinical records are editable until the visit is finalized | G1 |
| **R3** Payroll snapshot columns frozen; status transitions allowed | G2 |
| **R4** No business-schema DELETE | `clinic_app` has no DELETE grant |

- **G1 – finalized-visit guard.** One shared trigger function in `infra`, attached to `treatment_records`, `treatment_steps`, `root_canal_measurements`, `prescriptions`, `prescription_items` and visit notes. On UPDATE it checks whether the owning visit is Finalized (prescriptions reach it via `visit_id`). If so the change is rejected **unless** the transaction carries `SET LOCAL clinic.audited_correction = 'on'`; only the audited-correction code path sets it and it also writes the audit entry. Who may correct and how remains **OPEN (Q-CLN-1)**.
- **G2 – payroll sealing.** Applies to `doctor_payrolls`, `sales_payrolls` and (v1.1) `assistant_payrolls` (sealed: `salary_snapshot`, `final_amount`). Rejects any change to the sealed numeric columns, or insertion of items/adjustments, once `finalized_at IS NOT NULL`. `status`, `finalized_at`, `paid_at` stay updatable.
- **G3 – expense sealing.** Same pattern for `clinic_expenses` after `finalized_at`.
- **G4 – charge/payment consistency.** Declarative only: generated `final_price`, composite FKs keeping patient consistent, `reverses_payment_id` sign check, distribution sum CHECK. No triggers.
- **G5 – allocation cap** on `installment_payments` (insert trigger): locks the allocation row `FOR UPDATE`; Σ `amount_applied` for the allocation including the new row must not exceed the allocation amount; sign must match. The table is unused until Q-ORT-2.
- **G6 – work-item freeze.** Rejects changes to frozen columns, and to `entered_price` once a charge references the item.
- **G7 – fixed-lab eligibility.** Insert trigger on `fixed_lab_expenses` requires `procedure_types.lab_model = 'Fixed'`.

Not enforced by the database (command-handler checks plus integration tests): ortho cases accept exactly one work item and one charge (`separatelyPricedWork` is refused on ortho cases); the record's visit is not cancelled; the snapshot copies match (work item → charge).

## 19. Cardinalities

| Relationship | Cardinality |
|---|---|
| Patient → Contact profile | 1:1 |
| Patient → Appointments | 1:N |
| Appointment → successor | 1:0..1 (reschedule chain) |
| Patient → Visits | 1:N |
| Visit → Treatment Records | 1:N (frozen after finalization) |
| Treatment Case ↔ Visits | N:M through `treatment_records` |
| Patient → Treatment Cases | 1:N |
| Patient → Doctor Assignments | 1:N (1 current) |
| Assistant → Assistant Payrolls (v1.1) | 1:N, periods never overlapping |
| Treatment Case → Work Items | 1:N |
| Doctor → Work Items (performed work) | 1:N (financial ownership generally follows performed work; subject to Q-FIN-9 after reassignment or deactivation) |
| Doctor → Treatment Cases started | 1:N (clinical context only) |
| Work Item → Charge | 1:0..1 |
| Treatment Case → Charges | 1:N |
| Work Item → Treatment Record | N:0..1 |
| Treatment Case → Root Canal Steps | 1:3..4 |
| Treatment Case → Canal Measurements | 1:N |
| Treatment Case → Implant Details | 1:0..1 |
| Treatment Case → Ortho Case | 1:0..1 |
| Ortho Case → Charge | 1:0..1 (1:1 once terms exist) |
| Ortho Case → Installments | 1:N |
| Installment ↔ Payment Allocation | N:M through `installment_payments` (unused until Q-ORT-2) |
| Allocation → Installment Payments | 1:N (capped by G5) |
| Treatment Case → Fixed Lab Expense | 1:0..1 (Fixed-lab types only) |
| Fixed Lab Expense → Revisions | 1:N |
| Fixed Lab Expense → Charges / Payments / Doctors | **no relationship** (deliberate; Q-FIN-1) |
| Patient → Charges | 1:N |
| Charge → Payment Allocations | 1:N |
| Payment → Payment Allocations | 1:N |
| Charge ↔ Payment | N:M through allocations |
| Allocation → Doctor Earning | 1:0..1 (none for consultation or held allocations) |
| Doctor Earning → Revenue Distribution | 1:0..1 |
| Charge → Attribution Holds | 1:N |
| Allocation → Pending Attribution | 1:0..1 |
| Charge → Adjustments | 1:N |
| Visit → Prescriptions → Items | 1:N → 1:N |
| Visit → Feedback Request | 1:0..1 |
| Lead → Patient | 1:0..1 |
| Sales Employee → Original-patients | 1:N (through the contact profile) |
| Follow-up → Contact Attempts | 1:N |
| Doctor → Doctor Payrolls | 1:N (one per period) |
| Doctor Payroll → Earnings | 1:N (an earning appears in at most one payroll) |
| Sales Employee → Sales Payrolls | 1:N (one per month) |

## 20. Source of truth, derived values, snapshots

**Source of truth (stored once)**

| Fact | Where |
|---|---|
| Patient clinical identity and history | `patients.patients` |
| Contact data, Original Sales Rep | `patients.patient_contacts` |
| Current doctor and assignment history | `patient_doctor_assignments` |
| Appointment time, doctor, history | `scheduling.appointments` chain |
| Doctor's entered price before agreement | `clinical.case_work_items.entered_price` |
| Agreed price, discount, final price, performing Doctor and snapshot | `finance.patient_charges` (one per work item) |
| Money received | `payments`, `payment_allocations` |
| Fixed lab amount (current and history) | `fixed_lab_revisions` via the case's one `fixed_lab_expenses` row |
| General lab rate | `lab_percentage_history` |
| Doctor share per collection | `doctor_earnings` |
| Which allocation paid which installment | `orthodontics.installment_payments` |
| Doctor and Sales current percentages | `staff.*` |

**Derived, never stored as a balance**

| Value | Derivation |
|---|---|
| Billed | Σ charge `final_price` + Σ adjustments |
| Collected | Σ allocation amounts (including reversals) |
| Outstanding | Billed − Collected |
| Pending completion | Σ (allocated − doctor_share) over earnings with no distribution row |
| Pending Doctor Attribution | allocations with a pending row and no earning |
| Current fixed lab for a case | latest revision |
| Installment paid / remaining / status | applied amounts, due date, clinic-local today |
| Visible age | DOB, else recorded age shown as recorded |
| Doctor own-work figures | Charges where `performing_doctor_id` = signed-in Doctor, with their allocations, earnings, distributions |

**Snapshots (frozen when created)**

| Snapshot | Where |
|---|---|
| Doctor percentage for the work | `case_work_items.doctor_percentage_snapshot` (source), copied once to `patient_charges.doctor_percentage_snapshot` |
| Percentage applied per collection | `doctor_earnings.doctor_percentage_applied`; `revenue_distributions.lab_percentage_applied` |
| Lab model | `treatment_cases.lab_model_snapshot`, `patient_charges.lab_model` |
| Sales base salary and commission % | `sales_payrolls` |
| Assistant salary for a period (v1.1) | `assistant_payrolls.salary_snapshot` (copied from `staff.assistants.current_salary` at draft creation; never recalculated after a salary change) |
| Payroll totals | sealed on payroll headers |

**Deliberate copies:** work item → charge (Doctor, case, percentage; write-once, FK-checked, test asserts equality); lead → patient contact (Original Sales Rep, once at conversion); FK-checked copies in earnings/distributions/installment payments (ids and amounts only to let the database verify chains).

**Never silently overwritten:** charges, payments, allocations, earnings, distributions, fixed lab expenses and revisions, adjustments, attribution holds, lab percentage history, installment rows and payment links, root canal measurements, finalized visit content (including prescriptions), doctor assignment history, appointment history, sealed payroll columns, finalized expense columns, contact attempts, work-item frozen columns, audit log.

## 21. Indexes (summary)

| Area | Indexes |
|---|---|
| Patient search | unique `patient_code`, trigram on name, phone prefix (Sales only) |
| Appointments | (doctor,start), (patient,start), (start), (status,start) |
| Visits | (patient,started), (doctor,started) |
| Charges / payments | (patient,date) on both; (method,date) for the payment-method report |
| Installments | `due_date`; unique (case, number) |
| Notifications | partial pending index on scheduled time; (recipient, read) |
| Referrals / follow-ups | (status, created_at); (assigned employee, status, due_date) |
| Assistant payroll (v1.1) | (assistant_id, period_start) via the exclusion constraint; (status) |
| Reporting | (created_at) on `revenue_distributions`; (attributed_doctor_id, created_at) on `doctor_earnings`; (expense_date) on expenses |

Nothing beyond these is added until measured.

## 22. How open finance questions stay schema-neutral

| Open item | Why no redesign is needed later |
|---|---|
| **Q-FIN-1** fixed lab across partial payments | Payments, allocations, earnings and revisions are immutable inputs. No fixed-lab completion rows exist yet; a versioned calculator (`rule_version`) fills them for existing and future allocations. Doctor Share is never blocked. |
| **W2** which general-lab rate applies | Rate used is recorded on every completion row; completion generated only when all candidate rules agree. |
| **Q-FIN-2** Sales commission base | Commission items store whatever base the rule defines. |
| **Q-FIN-3** discount role | `discount_amount`/`discount_set_by_user_id` recorded at charge creation; `Finance.Discount.Set` assigned to no role. |
| **Q-FIN-6 (redefined)** effect of a fixed-lab revision on already-completed Lab/Clinic allocations | Revisions and completions are insert-only. **Never affects Doctor Share.** |
| **Q-FIN-9** attribution after reassignment or departure | Holds and pending attributions record the situation; the earning is inserted later by a calculator. |
| **Q-ORT-2** installment application | `installment_payments` exists, allocation-based, unused. |
| **Q-PAY-ASST-1 / Q-PAY-ASST-2** Assistant salary in period reports; partial-period salary (v1.1) | Payrolls store `period_start/period_end` and an unprorated snapshot. Period-report allocation is a read-side rule (no stored allocation). A partial-period rule would add components or effective-dated salaries by additive migration; nothing stored changes. |
| **Ortho terms actor** | `ortho_cases.charge_id`, `deposit_amount`, `number_of_months` nullable. |

## 23. Technical placeholders (not business rules)

| Item | What was done |
|---|---|
| Follow-up status values | Stored as text validated in code until a list is supplied |
| Payroll status values | Draft, Finalized, Paid |
| `referrals.follow_up_instructions` | Added so the confirmed Sales-visible instructions have a home |
| Orthodontics lab model | General |
| Doctor's entered price | On `case_work_items.entered_price` until a charge exists |
| Doctor's initial fixed lab | Written straight to `fixed_lab_expenses` + revision 1; never on a work item or charge |
