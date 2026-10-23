-- Clinic settings (DATABASE_DESIGN.md §5). The clinic timezone defaults to Africa/Cairo (FR-OPS-003 [DEFAULT]);
-- Doctor DOB visibility defaults to visible. Clinic header details are entered by the Manager in Arabic.
INSERT INTO configuration.settings (key, value, value_type) VALUES
    ('clinic_timezone', 'Africa/Cairo', 'timezone'),
    ('doctor_dob_visible', 'true', 'boolean'),
    ('clinic_name', '', 'string'),
    ('clinic_contact', '', 'string');

GRANT USAGE ON SCHEMA configuration TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT ON configuration.settings TO clinic_app, clinic_worker, clinic_backup;
GRANT UPDATE (value, updated_by_user_id, updated_at) ON configuration.settings TO clinic_app;
