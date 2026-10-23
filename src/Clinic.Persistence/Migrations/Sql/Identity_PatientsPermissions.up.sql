-- Patients permission keys and their roles, exactly as in the frozen RBAC matrix (SECURITY_DESIGN.md §4).
-- Contact-bearing keys are mapped only to SALES_RECEPTION and MANAGER, never to DOCTOR.
-- DOCTOR never receives Patients.MedicalHistory.EditIntake. Timeline keys arrive with the timeline routes.
INSERT INTO identity.permissions (key) VALUES
    ('Patients.Search.Clinical'),
    ('Patients.Search.Contact'),
    ('Patients.Clinical.Read'),
    ('Patients.Clinical.Write'),
    ('Patients.Identity.Write'),
    ('Patients.Contact.Read'),
    ('Patients.Contact.Write'),
    ('Patients.MedicalHistory.Read'),
    ('Patients.MedicalHistory.EditIntake'),
    ('Patients.Reassign');

INSERT INTO identity.role_permissions (role_id, permission_key)
SELECT r.id, m.permission_key
FROM (VALUES
    ('Patients.Search.Clinical', 'DOCTOR'),
    ('Patients.Search.Clinical', 'MANAGER'),
    ('Patients.Search.Contact', 'SALES_RECEPTION'),
    ('Patients.Search.Contact', 'MANAGER'),
    ('Patients.Clinical.Read', 'DOCTOR'),
    ('Patients.Clinical.Read', 'MANAGER'),
    ('Patients.Clinical.Write', 'DOCTOR'),
    ('Patients.Clinical.Write', 'MANAGER'),
    ('Patients.Identity.Write', 'SALES_RECEPTION'),
    ('Patients.Identity.Write', 'MANAGER'),
    ('Patients.Contact.Read', 'SALES_RECEPTION'),
    ('Patients.Contact.Read', 'MANAGER'),
    ('Patients.Contact.Write', 'SALES_RECEPTION'),
    ('Patients.Contact.Write', 'MANAGER'),
    ('Patients.MedicalHistory.Read', 'DOCTOR'),
    ('Patients.MedicalHistory.Read', 'SALES_RECEPTION'),
    ('Patients.MedicalHistory.Read', 'MANAGER'),
    ('Patients.MedicalHistory.EditIntake', 'SALES_RECEPTION'),
    ('Patients.MedicalHistory.EditIntake', 'MANAGER'),
    ('Patients.Reassign', 'MANAGER')
) AS m(permission_key, role_name)
JOIN identity.roles r ON r.name = m.role_name;
