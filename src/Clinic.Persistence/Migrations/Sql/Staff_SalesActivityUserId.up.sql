-- Adds the Sales employee's user id so a planner can address the owner's notification (API B5 / B7). Still no salary,
-- commission, profile or contact data. CREATE OR REPLACE VIEW may only append columns, which this does.
CREATE OR REPLACE VIEW staff.v_sales_employee_activity AS
SELECT s.id AS sales_employee_id, s.is_active, s.user_id
FROM staff.sales_employees s;

-- The API resolves active Sales owners (CRM assignment / redistribution) through the same view.
GRANT SELECT ON staff.v_sales_employee_activity TO clinic_app;
