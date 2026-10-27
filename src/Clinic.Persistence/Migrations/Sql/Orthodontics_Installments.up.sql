-- Declared parent key for installment_payments (section 24 #18). charge_id is NULL until Visit finalization, so this is a
-- SQL UNIQUE constraint rather than an EF alternate key.
ALTER TABLE orthodontics.ortho_cases ADD CONSTRAINT ak_ortho_cases_case_charge UNIQUE (treatment_case_id, charge_id);
ALTER TABLE orthodontics.installment_payments
    ADD CONSTRAINT fk_installment_payments_cases_case_charge
    FOREIGN KEY (ortho_case_id, charge_id) REFERENCES orthodontics.ortho_cases (treatment_case_id, charge_id);

-- Applications and schedule revisions of one installment serialize on the same transaction-scoped advisory lock
-- (clinic_app has no UPDATE grant on these insert-only tables, so FOR UPDATE is not available to it).
CREATE FUNCTION orthodontics.lock_installment(p_installment_id uuid) RETURNS void LANGUAGE sql AS $$
    SELECT pg_advisory_xact_lock(hashtextextended(p_installment_id::text, 11));
$$;

CREATE FUNCTION orthodontics.current_revision(p_installment_id uuid)
    RETURNS orthodontics.ortho_installment_revisions LANGUAGE sql STABLE AS $$
    SELECT * FROM orthodontics.ortho_installment_revisions WHERE installment_id = p_installment_id ORDER BY revision_number DESC LIMIT 1;
$$;

-- G5(a)/(c): an application never exceeds its allocation (same sign), never pushes the installment's applied total above its
-- current amount or below 0, and a new (positive) application needs an active installment.
CREATE FUNCTION orthodontics.tg_installment_payments_g5() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_allocation numeric;
    v_applied_allocation numeric;
    v_applied_installment numeric;
    v_current orthodontics.ortho_installment_revisions;
BEGIN
    PERFORM orthodontics.lock_installment(NEW.installment_id);
    PERFORM pg_advisory_xact_lock(hashtextextended(NEW.payment_allocation_id::text, 12));
    SELECT amount INTO v_allocation FROM finance.payment_allocations WHERE id = NEW.payment_allocation_id;
    IF NEW.amount_applied = 0 OR sign(NEW.amount_applied) <> sign(v_allocation) THEN
        RAISE EXCEPTION 'G5: an application has the sign of its allocation' USING ERRCODE = '23514';
    END IF;
    SELECT COALESCE(sum(amount_applied), 0) + NEW.amount_applied INTO v_applied_allocation
    FROM orthodontics.installment_payments WHERE payment_allocation_id = NEW.payment_allocation_id;
    IF abs(v_applied_allocation) > abs(v_allocation) THEN
        RAISE EXCEPTION 'G5: applications exceed the allocation' USING ERRCODE = '23514';
    END IF;
    v_current := orthodontics.current_revision(NEW.installment_id);
    IF NEW.amount_applied > 0 AND NOT v_current.is_active THEN
        RAISE EXCEPTION 'G5: a retired installment cannot receive a payment' USING ERRCODE = '23514';
    END IF;
    SELECT COALESCE(sum(amount_applied), 0) + NEW.amount_applied INTO v_applied_installment
    FROM orthodontics.installment_payments WHERE installment_id = NEW.installment_id;
    IF v_applied_installment < 0 OR v_applied_installment > v_current.amount_due THEN
        RAISE EXCEPTION 'G5: installment applied total outside [0, amount due]' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_installment_payments_g5 BEFORE INSERT ON orthodontics.installment_payments
    FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_installment_payments_g5();

-- G5(b)/(c)/(d): paid history is immutable. A revision may not go below the amount already paid, retire or move the due date
-- of an installment that has any application, or re-activate a retired installment. A ManagerAdded installment is due today
-- or later (clinic-local).
CREATE FUNCTION orthodontics.tg_installment_revisions_g5() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_previous orthodontics.ortho_installment_revisions;
    v_applied numeric;
    v_has_applications boolean;
    v_today date;
BEGIN
    PERFORM orthodontics.lock_installment(NEW.installment_id);
    IF NEW.revision_number = 1 THEN
        IF NEW.source = 'ManagerAdded' THEN
            SELECT (now() AT TIME ZONE s.value)::date INTO v_today FROM configuration.settings s WHERE s.key = 'clinic_timezone';
            IF NEW.due_date < v_today THEN
                RAISE EXCEPTION 'G5: an added installment is due today or later' USING ERRCODE = '23514';
            END IF;
        END IF;
        RETURN NEW;
    END IF;
    v_previous := orthodontics.current_revision(NEW.installment_id);
    IF v_previous.revision_number <> NEW.revision_number - 1 THEN
        RAISE EXCEPTION 'G5: revisions are consecutive' USING ERRCODE = '23514';
    END IF;
    SELECT COALESCE(sum(amount_applied), 0), count(*) > 0 INTO v_applied, v_has_applications
    FROM orthodontics.installment_payments WHERE installment_id = NEW.installment_id;
    IF NEW.amount_due < v_applied THEN
        RAISE EXCEPTION 'ortho.installment_below_paid' USING ERRCODE = '23514';
    END IF;
    IF NOT NEW.is_active AND v_has_applications THEN
        RAISE EXCEPTION 'ortho.installment_has_payments' USING ERRCODE = '23514';
    END IF;
    IF NEW.is_active AND NOT v_previous.is_active THEN
        RAISE EXCEPTION 'G5: a retired installment is never re-activated' USING ERRCODE = '23514';
    END IF;
    IF NEW.due_date <> v_previous.due_date AND v_has_applications THEN
        RAISE EXCEPTION 'ortho.installment_due_date_locked' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_ortho_installment_revisions_g5 BEFORE INSERT ON orthodontics.ortho_installment_revisions
    FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_installment_revisions_g5();

-- G9(a): generic price / discount corrections never change an Orthodontic Total; OrthoTermsChange (+Δ only) is used only
-- for ortho charges.
CREATE FUNCTION finance.tg_charge_adjustments_g9() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_is_ortho boolean := EXISTS (SELECT 1 FROM orthodontics.ortho_cases WHERE charge_id = NEW.charge_id);
BEGIN
    IF v_is_ortho AND NEW.adjustment_kind <> 'OrthoTermsChange' THEN
        RAISE EXCEPTION 'ortho.use_terms_edit' USING ERRCODE = '23514';
    END IF;
    IF NOT v_is_ortho AND NEW.adjustment_kind = 'OrthoTermsChange' THEN
        RAISE EXCEPTION 'G9: OrthoTermsChange applies only to an ortho charge' USING ERRCODE = '23514';
    END IF;
    IF NEW.adjustment_kind = 'OrthoTermsChange' AND NEW.amount_delta <= 0 THEN
        RAISE EXCEPTION 'ortho.total_decrease_not_supported' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_charge_adjustments_g9 BEFORE INSERT ON finance.charge_adjustments
    FOR EACH ROW EXECUTE FUNCTION finance.tg_charge_adjustments_g9();

-- G10(a): no schedule before Visit finalization.
CREATE FUNCTION orthodontics.tg_ortho_installments_g10() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM orthodontics.ortho_cases WHERE treatment_case_id = NEW.ortho_case_id AND charge_id IS NOT NULL) THEN
        RAISE EXCEPTION 'G10: no installment before the ortho charge exists' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_ortho_installments_g10 BEFORE INSERT ON orthodontics.ortho_installments
    FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_ortho_installments_g10();

-- G9(c)/(d) and G10(b) (deferred, at commit) for one ortho case with a charge:
--   charge original_price = revision-1 Gross; Σ OrthoTermsChange = current Gross − revision-1 Gross;
--   current Deposit ≤ Billed (final_price + Σ OrthoTermsChange);
--   exactly revision-1 Months Generated installments, entered by the DOCTOR, summing to final_price − revision-1 Deposit.
CREATE FUNCTION orthodontics.check_ortho_case(p_case_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_charge_id uuid;
    v_original numeric;
    v_final numeric;
    v_terms_delta numeric;
    v_first orthodontics.ortho_term_revisions;
    v_current orthodontics.ortho_term_revisions;
    v_generated integer;
    v_generated_sum numeric;
    v_non_doctor integer;
BEGIN
    SELECT charge_id INTO v_charge_id FROM orthodontics.ortho_cases WHERE treatment_case_id = p_case_id;
    IF v_charge_id IS NULL THEN
        RETURN;
    END IF;
    SELECT original_price, final_price INTO v_original, v_final FROM finance.patient_charges WHERE id = v_charge_id;
    SELECT COALESCE(sum(amount_delta), 0) INTO v_terms_delta
    FROM finance.charge_adjustments WHERE charge_id = v_charge_id AND adjustment_kind = 'OrthoTermsChange';
    SELECT * INTO v_first FROM orthodontics.ortho_term_revisions WHERE ortho_case_id = p_case_id AND revision_number = 1;
    SELECT * INTO v_current FROM orthodontics.ortho_term_revisions WHERE ortho_case_id = p_case_id ORDER BY revision_number DESC LIMIT 1;

    IF v_original <> v_first.gross_total_price OR v_terms_delta <> v_current.gross_total_price - v_first.gross_total_price THEN
        RAISE EXCEPTION 'G9: the ortho charge and the term revisions disagree' USING ERRCODE = '23514';
    END IF;
    IF v_current.deposit_amount > v_final + v_terms_delta THEN
        RAISE EXCEPTION 'ortho.deposit_exceeds_effective_total' USING ERRCODE = '23514';
    END IF;

    SELECT count(*), COALESCE(sum(r.amount_due), 0), count(*) FILTER (WHERE r.entered_by_role <> 'DOCTOR')
    INTO v_generated, v_generated_sum, v_non_doctor
    FROM orthodontics.ortho_installments i
    JOIN orthodontics.ortho_installment_revisions r ON r.installment_id = i.id AND r.revision_number = 1
    WHERE i.ortho_case_id = p_case_id AND i.origin = 'Generated';
    IF v_generated <> v_first.number_of_months OR v_generated_sum <> v_final - v_first.deposit_amount OR v_non_doctor > 0
       OR (SELECT count(*) FROM orthodontics.ortho_installments WHERE ortho_case_id = p_case_id AND origin = 'Generated') <> v_generated THEN
        RAISE EXCEPTION 'G10: the initial schedule does not match the charge' USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION orthodontics.tg_check_case_by_case() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_TABLE_NAME = 'ortho_cases' THEN
        PERFORM orthodontics.check_ortho_case(NEW.treatment_case_id);
    ELSE
        PERFORM orthodontics.check_ortho_case(NEW.ortho_case_id);
    END IF;
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_ortho_cases_check AFTER UPDATE OF charge_id ON orthodontics.ortho_cases
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_check_case_by_case();
CREATE CONSTRAINT TRIGGER trg_ortho_term_revisions_check AFTER INSERT ON orthodontics.ortho_term_revisions
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_check_case_by_case();
CREATE CONSTRAINT TRIGGER trg_ortho_installments_check AFTER INSERT ON orthodontics.ortho_installments
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION orthodontics.tg_check_case_by_case();

CREATE FUNCTION finance.tg_charge_adjustments_ortho_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_case uuid;
BEGIN
    SELECT treatment_case_id INTO v_case FROM orthodontics.ortho_cases WHERE charge_id = NEW.charge_id;
    IF v_case IS NOT NULL THEN
        PERFORM orthodontics.check_ortho_case(v_case);
    END IF;
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_charge_adjustments_ortho_check AFTER INSERT ON finance.charge_adjustments
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_charge_adjustments_ortho_check();

-- R1: installments, revisions and applications are insert-only; ortho_cases.charge_id is set once at finalization.
GRANT SELECT, INSERT ON orthodontics.ortho_installments, orthodontics.ortho_installment_revisions, orthodontics.installment_payments TO clinic_app;
GRANT UPDATE (charge_id) ON orthodontics.ortho_cases TO clinic_app;
GRANT SELECT ON orthodontics.ortho_installments, orthodontics.ortho_installment_revisions, orthodontics.installment_payments TO clinic_backup;

-- J2 (Worker): Orthodontic Installment Due Tomorrow reads the schedule and its applications.
GRANT USAGE ON SCHEMA orthodontics TO clinic_worker;
GRANT SELECT ON orthodontics.ortho_cases, orthodontics.ortho_term_revisions, orthodontics.ortho_installments,
    orthodontics.ortho_installment_revisions, orthodontics.installment_payments TO clinic_worker;
