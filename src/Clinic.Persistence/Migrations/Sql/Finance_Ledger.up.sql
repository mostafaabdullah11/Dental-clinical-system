-- Declared parent keys with a nullable column (section 24 #16, #28): Consultation charges have no treatment case, so these
-- cannot be EF alternate keys. UNIQUE allows the NULLs; they hold trivially because id is the primary key.
ALTER TABLE finance.patient_charges ADD CONSTRAINT ak_patient_charges_id_case UNIQUE (id, treatment_case_id);
ALTER TABLE finance.patient_charges ADD CONSTRAINT ak_patient_charges_case_id UNIQUE (treatment_case_id, id);

-- Section 24 #28: a distribution row's case is its charge's own case.
ALTER TABLE finance.revenue_distributions
    ADD CONSTRAINT fk_revenue_distributions_charges_charge_case
    FOREIGN KEY (charge_id, treatment_case_id) REFERENCES finance.patient_charges (id, treatment_case_id);

-- G7(b): a Fixed-lab Treatment charge requires, at commit, the Doctor's initial Fixed Lab (revision 1) for its case. An
-- unknown Fixed Lab is never distributed and never treated as 0 (FR-LAB-007).
CREATE FUNCTION finance.tg_patient_charges_fixed_lab_initial() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.lab_model = 'Fixed' AND NOT EXISTS (
        SELECT 1 FROM finance.fixed_lab_revisions r WHERE r.treatment_case_id = NEW.treatment_case_id AND r.revision_number = 1) THEN
        RAISE EXCEPTION 'fixed_lab.initial_required' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_patient_charges_fixed_lab_initial AFTER INSERT ON finance.patient_charges
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_patient_charges_fixed_lab_initial();

-- G8 (deferred, at commit): payment conservation, non-negative Patient Credit, and every charge's net allocated amount
-- within [0, Billed]. Billed = final_price + Σ charge_adjustments.amount_delta.
CREATE FUNCTION finance.check_payment_conservation(p_payment_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_amount numeric;
    v_allocated numeric;
    v_credit numeric;
BEGIN
    SELECT amount INTO v_amount FROM finance.payments WHERE id = p_payment_id;
    SELECT COALESCE(sum(amount), 0) INTO v_allocated FROM finance.payment_allocations WHERE payment_id = p_payment_id;
    SELECT COALESCE(sum(amount), 0) INTO v_credit FROM finance.patient_credit_entries WHERE payment_id = p_payment_id;
    IF v_amount <> v_allocated + v_credit THEN
        RAISE EXCEPTION 'G8: payment % amount % <> allocations % + credit %', p_payment_id, v_amount, v_allocated, v_credit
            USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION finance.check_patient_credit(p_patient_id uuid) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    IF (SELECT COALESCE(sum(amount), 0) FROM finance.patient_credit_entries WHERE patient_id = p_patient_id) < 0 THEN
        RAISE EXCEPTION 'G8: patient credit balance below zero' USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION finance.check_charge_allocated(p_charge_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_billed numeric;
    v_allocated numeric;
BEGIN
    SELECT c.final_price + COALESCE((SELECT sum(a.amount_delta) FROM finance.charge_adjustments a WHERE a.charge_id = c.id), 0)
    INTO v_billed FROM finance.patient_charges c WHERE c.id = p_charge_id;
    SELECT COALESCE(sum(amount), 0) INTO v_allocated FROM finance.payment_allocations WHERE charge_id = p_charge_id;
    IF v_allocated < 0 OR v_allocated > v_billed THEN
        RAISE EXCEPTION 'G8: charge % allocated % outside [0, billed %]', p_charge_id, v_allocated, v_billed USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION finance.tg_g8_payments() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_payment_conservation(NEW.id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_payments_g8 AFTER INSERT ON finance.payments
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8_payments();

CREATE FUNCTION finance.tg_g8_payment_allocations() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_payment_conservation(NEW.payment_id);
    PERFORM finance.check_charge_allocated(NEW.charge_id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_payment_allocations_g8 AFTER INSERT ON finance.payment_allocations
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8_payment_allocations();

CREATE FUNCTION finance.tg_g8_patient_credit_entries() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_payment_conservation(NEW.payment_id);
    PERFORM finance.check_patient_credit(NEW.patient_id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_patient_credit_entries_g8 AFTER INSERT ON finance.patient_credit_entries
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8_patient_credit_entries();

CREATE FUNCTION finance.tg_g8_charge_adjustments() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_charge_allocated(NEW.charge_id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_charge_adjustments_g8 AFTER INSERT ON finance.charge_adjustments
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8_charge_adjustments();

-- G8b (deferred, at commit): distribution identity. Per event Σ(lab + clinic) + Σ earnings = basis_amount; per allocation
-- 0 ≤ Σ earnings ≤ Σ entitlements and the cumulative clinic share is never negative (Fixed Lab: Clinic Share ≥ 0).
CREATE FUNCTION finance.check_distribution_event(p_event_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_basis numeric;
    v_total numeric;
BEGIN
    SELECT basis_amount INTO v_basis FROM finance.distribution_events WHERE id = p_event_id;
    SELECT COALESCE((SELECT sum(lab_amount + clinic_share) FROM finance.revenue_distributions WHERE event_id = p_event_id), 0)
         + COALESCE((SELECT sum(amount) FROM finance.doctor_earnings WHERE event_id = p_event_id), 0)
    INTO v_total;
    IF v_total <> v_basis THEN
        RAISE EXCEPTION 'G8b: event % distributes % but its basis is %', p_event_id, v_total, v_basis USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION finance.check_allocation_distribution(p_allocation_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_earned numeric;
    v_entitled numeric;
    v_clinic numeric;
BEGIN
    SELECT COALESCE(sum(amount), 0) INTO v_earned FROM finance.doctor_earnings WHERE payment_allocation_id = p_allocation_id;
    SELECT COALESCE(sum(amount), 0) INTO v_entitled FROM finance.doctor_entitlements WHERE payment_allocation_id = p_allocation_id;
    SELECT COALESCE(sum(clinic_share), 0) INTO v_clinic FROM finance.revenue_distributions WHERE payment_allocation_id = p_allocation_id;
    IF v_earned < 0 OR v_earned > v_entitled THEN
        RAISE EXCEPTION 'G8b: allocation % earned % outside [0, entitled %]', p_allocation_id, v_earned, v_entitled USING ERRCODE = '23514';
    END IF;
    IF v_clinic < 0 THEN
        RAISE EXCEPTION 'finance.clinic_share_negative' USING ERRCODE = '23514';
    END IF;
END
$$;

CREATE FUNCTION finance.tg_g8b_distribution_events() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_distribution_event(NEW.id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_distribution_events_g8b AFTER INSERT ON finance.distribution_events
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8b_distribution_events();

CREATE FUNCTION finance.tg_g8b_distribution_rows() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM finance.check_distribution_event(NEW.event_id);
    PERFORM finance.check_allocation_distribution(NEW.payment_allocation_id);
    RETURN NULL;
END
$$;
CREATE CONSTRAINT TRIGGER trg_doctor_earnings_g8b AFTER INSERT ON finance.doctor_earnings
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8b_distribution_rows();
CREATE CONSTRAINT TRIGGER trg_doctor_entitlements_g8b AFTER INSERT ON finance.doctor_entitlements
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8b_distribution_rows();
CREATE CONSTRAINT TRIGGER trg_revenue_distributions_g8b AFTER INSERT ON finance.revenue_distributions
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.tg_g8b_distribution_rows();

-- R1 / R4: every ledger table is insert-only for the application (refunds and corrections are new rows).
GRANT SELECT, INSERT ON finance.patient_charges, finance.charge_adjustments, finance.payments, finance.payment_allocations,
    finance.patient_credit_entries, finance.payment_corrections, finance.distribution_events, finance.doctor_entitlements,
    finance.doctor_earnings, finance.revenue_distributions TO clinic_app;
GRANT SELECT ON finance.patient_charges, finance.charge_adjustments, finance.payments, finance.payment_allocations,
    finance.patient_credit_entries, finance.payment_corrections, finance.distribution_events, finance.doctor_entitlements,
    finance.doctor_earnings, finance.revenue_distributions TO clinic_backup;

-- J2 (Worker): the installment reminder needs the ortho charge's Billed and Collected only (no patient contact data).
GRANT USAGE ON SCHEMA finance TO clinic_worker;
GRANT SELECT (id, patient_id, charge_kind, final_price, charged_at) ON finance.patient_charges TO clinic_worker;
GRANT SELECT (charge_id, amount_delta) ON finance.charge_adjustments TO clinic_worker;
GRANT SELECT (id, charge_id, amount, allocated_at, reverses_allocation_id) ON finance.payment_allocations TO clinic_worker;
