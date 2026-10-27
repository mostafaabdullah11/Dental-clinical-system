-- FR-EXP-001: exactly the 11 confirmed categories (Arabic UI names). Only Materials / Medical Supplies and Petty /
-- Miscellaneous are Sales-recordable. Materials are an expense category, never Inventory.
INSERT INTO expenses.expense_categories (id, name, is_active, sales_can_record, sort_order) VALUES
    ('0192f000-0000-7000-8000-000000000001', 'مياه', true, false, 1),
    ('0192f000-0000-7000-8000-000000000002', 'كهرباء', true, false, 2),
    ('0192f000-0000-7000-8000-000000000003', 'إيجار', true, false, 3),
    ('0192f000-0000-7000-8000-000000000004', 'إنترنت', true, false, 4),
    ('0192f000-0000-7000-8000-000000000005', 'صيانة', true, false, 5),
    ('0192f000-0000-7000-8000-000000000006', 'نظافة', true, false, 6),
    ('0192f000-0000-7000-8000-000000000007', 'أدوات مكتبية', true, false, 7),
    ('0192f000-0000-7000-8000-000000000008', 'خامات / مستلزمات طبية', true, true, 8),
    ('0192f000-0000-7000-8000-000000000009', 'تسويق', true, false, 9),
    ('0192f000-0000-7000-8000-00000000000a', 'مواصلات', true, false, 10),
    ('0192f000-0000-7000-8000-00000000000b', 'نثريات / متفرقات', true, true, 11);

-- FR-EXP-002: a Sales / Reception expense is accepted only in an active Sales-recordable category (the command checks first).
CREATE FUNCTION expenses.tg_clinic_expenses_sales_category() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.created_by_role = 'SALES_RECEPTION' AND NOT EXISTS (
        SELECT 1 FROM expenses.expense_categories c WHERE c.id = NEW.category_id AND c.is_active AND c.sales_can_record) THEN
        RAISE EXCEPTION 'expenses.category_not_allowed' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_clinic_expenses_sales_category BEFORE INSERT ON expenses.clinic_expenses
    FOR EACH ROW EXECUTE FUNCTION expenses.tg_clinic_expenses_sales_category();

-- FR-EXP-005 / -006: the Manager edits in place (audited); creator, creator role and created_at have no UPDATE grant. No
-- DELETE, no finalization, no attachment.
GRANT USAGE ON SCHEMA expenses TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON expenses.expense_categories, expenses.clinic_expenses TO clinic_app;
GRANT UPDATE (name, is_active, sales_can_record) ON expenses.expense_categories TO clinic_app;
GRANT UPDATE (expense_date, category_id, description, amount, payment_method_code, paid_to_vendor, notes, updated_by_user_id, updated_at)
    ON expenses.clinic_expenses TO clinic_app;
GRANT SELECT ON expenses.expense_categories, expenses.clinic_expenses TO clinic_backup;
