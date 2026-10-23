-- CRM permission keys and their roles, exactly as in the frozen RBAC matrix (SECURITY_DESIGN.md §4).
-- All five keys are contact-bearing: mapped only to SALES_RECEPTION and MANAGER, never to DOCTOR.
INSERT INTO identity.permissions (key) VALUES
    ('Crm.Leads.Manage'),
    ('Crm.Leads.ViewAll'),
    ('Crm.Leads.ViewOwn'),
    ('Crm.FollowUps.Manage'),
    ('Crm.Responsibility.Assign');

INSERT INTO identity.role_permissions (role_id, permission_key)
SELECT r.id, m.permission_key
FROM (VALUES
    ('Crm.Leads.Manage', 'SALES_RECEPTION'),
    ('Crm.Leads.Manage', 'MANAGER'),
    ('Crm.FollowUps.Manage', 'SALES_RECEPTION'),
    ('Crm.FollowUps.Manage', 'MANAGER'),
    ('Crm.Leads.ViewAll', 'MANAGER'),
    ('Crm.Leads.ViewOwn', 'SALES_RECEPTION'),
    ('Crm.Responsibility.Assign', 'MANAGER')
) AS m(permission_key, role_name)
JOIN identity.roles r ON r.name = m.role_name;
