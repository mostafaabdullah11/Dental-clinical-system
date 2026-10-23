-- Identity: exactly three application roles (FR-AUTH-001). Assistant is a staff type, never a role.
INSERT INTO identity.roles (id, name) VALUES
    (gen_random_uuid(), 'MANAGER'),
    (gen_random_uuid(), 'DOCTOR'),
    (gen_random_uuid(), 'SALES_RECEPTION');

-- Permission keys used by the Task 2 foundation routes (SECURITY_DESIGN.md §4). Later slices add their keys.
INSERT INTO identity.permissions (key) VALUES
    ('Identity.Users.Manage'),
    ('Identity.Users.SetPassword'),
    ('Staff.Manage'),
    ('Staff.DoctorPercentage.Configure'),
    ('Staff.SalesCommission.Configure'),
    ('Payroll.Sales.ManageAll'),
    ('Configuration.Manage'),
    ('Audit.Read');

-- All of the keys above are Manager-only in the frozen RBAC matrix.
INSERT INTO identity.role_permissions (role_id, permission_key)
SELECT r.id, p.key
FROM identity.roles r
CROSS JOIN identity.permissions p
WHERE r.name = 'MANAGER';

-- Worker role resolution without any privilege on the base tables (owned by clinic_migrator, not security_invoker).
CREATE VIEW identity.v_active_user_roles AS
SELECT u.id AS user_id, r.name AS role_name
FROM identity.users u
JOIN identity.user_roles ur ON ur.user_id = u.id
JOIN identity.roles r ON r.id = ur.role_id
WHERE u.is_active;

-- Least privilege (SECURITY_DESIGN.md §15): no DELETE, no TRUNCATE, column-restricted UPDATE.
GRANT USAGE ON SCHEMA identity TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT, INSERT ON identity.users, identity.user_roles TO clinic_app;
GRANT UPDATE (password_hash, security_stamp, is_active, access_failed_count, lockout_end) ON identity.users TO clinic_app;
GRANT UPDATE (role_id) ON identity.user_roles TO clinic_app;
GRANT SELECT ON identity.roles, identity.permissions, identity.role_permissions TO clinic_app;
GRANT SELECT ON identity.v_active_user_roles TO clinic_worker;
GRANT SELECT ON ALL TABLES IN SCHEMA identity TO clinic_backup;
