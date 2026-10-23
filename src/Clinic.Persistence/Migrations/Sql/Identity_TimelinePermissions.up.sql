-- Patient timeline keys (FR-PAT-010), exactly as in the frozen RBAC matrix (SECURITY_DESIGN.md §4):
-- Patients.Timeline.Clinical → DOCTOR, MANAGER; Patients.Timeline.Operational → SALES_RECEPTION, MANAGER.
INSERT INTO identity.permissions (key) VALUES
    ('Patients.Timeline.Clinical'),
    ('Patients.Timeline.Operational');

INSERT INTO identity.role_permissions (role_id, permission_key)
SELECT r.id, m.permission_key
FROM (VALUES
    ('Patients.Timeline.Clinical', 'DOCTOR'),
    ('Patients.Timeline.Clinical', 'MANAGER'),
    ('Patients.Timeline.Operational', 'SALES_RECEPTION'),
    ('Patients.Timeline.Operational', 'MANAGER')
) AS m(permission_key, role_name)
JOIN identity.roles r ON r.name = m.role_name;
