-- G7(a): a Fixed Lab obligation exists only for a case whose procedure type uses the Fixed lab model.
-- (G7(b), the deferred check on patient_charges, arrives with the charges table in Task 4.)
CREATE FUNCTION finance.tg_fixed_lab_expenses_fixed_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM clinical.treatment_cases c JOIN clinical.procedure_types t ON t.id = c.procedure_type_id
                   WHERE c.id = NEW.treatment_case_id AND t.lab_model = 'Fixed') THEN
        RAISE EXCEPTION 'G7: a fixed lab requires a Fixed lab-model case' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_fixed_lab_expenses_fixed_only BEFORE INSERT ON finance.fixed_lab_expenses
    FOR EACH ROW EXECUTE FUNCTION finance.tg_fixed_lab_expenses_fixed_only();

-- R1: fixed_lab_expenses and fixed_lab_revisions are insert-only.
GRANT USAGE ON SCHEMA finance TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON finance.fixed_lab_expenses, finance.fixed_lab_revisions TO clinic_app;
GRANT SELECT ON finance.fixed_lab_expenses, finance.fixed_lab_revisions TO clinic_backup;
