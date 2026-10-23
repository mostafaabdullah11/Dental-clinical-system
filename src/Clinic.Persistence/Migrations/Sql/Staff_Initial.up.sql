-- The only staff object the Worker may read: no salary, commission, percentage, profile or contact data.
CREATE VIEW staff.v_sales_employee_activity AS
SELECT s.id AS sales_employee_id, s.is_active
FROM staff.sales_employees s;

GRANT USAGE ON SCHEMA staff TO clinic_app, clinic_worker, clinic_backup;

-- Doctor / Sales / Assistant records: SELECT, INSERT and column-restricted UPDATE; never DELETE (FR-STAFF-011).
GRANT SELECT, INSERT ON staff.doctors, staff.sales_employees, staff.assistants TO clinic_app;
GRANT UPDATE (current_percentage, is_active, deactivated_at) ON staff.doctors TO clinic_app;
GRANT UPDATE (base_monthly_salary, is_active, deactivated_at) ON staff.sales_employees TO clinic_app;
GRANT UPDATE (full_name, current_salary, overtime_hourly_rate, is_active, deactivated_at) ON staff.assistants TO clinic_app;

-- Commission rate history is insert-only (H): SELECT, INSERT only.
GRANT SELECT, INSERT ON staff.sales_commission_rate_history TO clinic_app;

GRANT SELECT ON staff.v_sales_employee_activity TO clinic_worker;
GRANT SELECT ON ALL TABLES IN SCHEMA staff TO clinic_backup;
