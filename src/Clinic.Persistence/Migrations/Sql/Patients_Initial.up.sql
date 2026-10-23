-- Patient search indexes (DATABASE_DESIGN.md §21): trigram on lower(full_name); phone prefix (Sales / Manager only).
CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
CREATE INDEX ix_patients_full_name_trgm ON patients.patients USING gin (lower(full_name) public.gin_trgm_ops);
CREATE INDEX ix_patient_contacts_phone_prefix ON patients.patient_contacts (phone text_pattern_ops);

-- Least privilege (SECURITY_DESIGN.md §15, DATABASE_DESIGN.md §7): no DELETE anywhere; column-restricted UPDATE.
GRANT USAGE ON SCHEMA patients TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON patients.patients, patients.patient_contacts, patients.patient_medical_histories,
    patients.patient_doctor_assignments TO clinic_app;

-- Identity corrections and the separate chief complaint. patient_code, registered_at and created_* are frozen.
GRANT UPDATE (full_name, date_of_birth, age_years_recorded, age_recorded_on, chief_complaint) ON patients.patients TO clinic_app;

-- Contact corrections only: patient_source and original_sales_employee_id are frozen after creation.
GRANT UPDATE (phone, whatsapp, email, address, crm_notes) ON patients.patient_contacts TO clinic_app;

-- Intake answers only: patient_id and created_at are never updated.
GRANT UPDATE (has_allergy, has_hypertension, has_viral_hepatitis, has_diabetes, has_kidney_problems,
    has_bleeding_or_clotting_problem, has_heart_problems, has_rheumatic_disease, is_smoker,
    has_previous_surgical_operations, is_taking_medications, last_dental_visit, is_pregnant, is_breastfeeding,
    updated_at) ON patients.patient_medical_histories TO clinic_app;

-- Assignment lifecycle: only ended_at is set, once, when the successor is inserted.
GRANT UPDATE (ended_at) ON patients.patient_doctor_assignments TO clinic_app;

GRANT USAGE ON SEQUENCE patients.patient_code_seq TO clinic_app;

-- Backup: explicit SELECT. Worker: no access to the patients schema (in particular never the intake table).
GRANT SELECT ON ALL TABLES IN SCHEMA patients TO clinic_backup;
