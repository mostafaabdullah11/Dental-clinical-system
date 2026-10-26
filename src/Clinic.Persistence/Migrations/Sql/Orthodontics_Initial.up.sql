-- Task 3 stores only the Doctor's initial terms (revision 1). The charge link, installments and Manager revisions are
-- Task 4. G9(b): a later revision may never lower the Gross Total (V1, Q-ORT-3b deferred).
CREATE FUNCTION orthodontics.tg_ortho_term_revisions_no_decrease() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM orthodontics.ortho_term_revisions p
               WHERE p.ortho_case_id = NEW.ortho_case_id AND p.revision_number = NEW.revision_number - 1
                 AND NEW.gross_total_price < p.gross_total_price) THEN
        RAISE EXCEPTION 'ortho.total_decrease_not_supported' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_ortho_term_revisions_no_decrease BEFORE INSERT ON orthodontics.ortho_term_revisions
    FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_ortho_term_revisions_no_decrease();

-- Insert-only in Task 3 (R1 for the revisions; ortho_cases.charge_id is set by Task 4 finance).
GRANT USAGE ON SCHEMA orthodontics TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON orthodontics.ortho_cases, orthodontics.ortho_term_revisions TO clinic_app;
GRANT SELECT ON orthodontics.ortho_cases, orthodontics.ortho_term_revisions TO clinic_backup;
