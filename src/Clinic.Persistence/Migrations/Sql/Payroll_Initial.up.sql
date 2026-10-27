-- Clinic-local calendar date of a moment (configuration.settings clinic_timezone, FR-OPS-003). Payroll months are clinic
-- calendar months.
CREATE FUNCTION payroll.clinic_date(p_at timestamptz) RETURNS date LANGUAGE sql STABLE AS $$
    SELECT (p_at AT TIME ZONE (SELECT value FROM configuration.settings WHERE key = 'clinic_timezone'))::date;
$$;

-- G2: payroll sealing. Draft -> Finalized -> Paid only; finalized_at only after the month has ended (clinic-local); once
-- finalized_at is set every column except status and paid_at is sealed. At finalization the header totals must equal
-- their items / adjustments.
CREATE FUNCTION payroll.tg_payrolls_g2() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_old jsonb := to_jsonb(OLD) - 'status' - 'paid_at' - 'net_salary';
    v_new jsonb := to_jsonb(NEW) - 'status' - 'paid_at' - 'net_salary';
BEGIN
    IF OLD.finalized_at IS NOT NULL AND v_old <> v_new THEN
        RAISE EXCEPTION 'G2: a finalized payroll is immutable' USING ERRCODE = '23514';
    END IF;
    IF NOT ((OLD.status = NEW.status)
            OR (OLD.status = 'Draft' AND NEW.status = 'Finalized')
            OR (OLD.status = 'Finalized' AND NEW.status = 'Paid')) THEN
        RAISE EXCEPTION 'G2: invalid payroll status transition % -> %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
    IF OLD.finalized_at IS NULL AND NEW.finalized_at IS NOT NULL THEN
        IF payroll.clinic_date(NEW.finalized_at) <= NEW.period_end THEN
            RAISE EXCEPTION 'payroll.period_not_ended' USING ERRCODE = '23514';
        END IF;
        IF TG_TABLE_NAME = 'doctor_payrolls' THEN
            IF NEW.doctor_share_total <> (SELECT COALESCE(sum(amount), 0) FROM payroll.doctor_payroll_items WHERE doctor_payroll_id = NEW.id)
               OR NEW.adjustments_total <> (SELECT COALESCE(sum(amount), 0) FROM payroll.doctor_payroll_adjustments WHERE doctor_payroll_id = NEW.id) THEN
                RAISE EXCEPTION 'G2: doctor payroll totals differ from their items' USING ERRCODE = '23514';
            END IF;
        ELSIF TG_TABLE_NAME = 'sales_payrolls' THEN
            IF NEW.commission_total <> (SELECT COALESCE(sum(commission_amount), 0) FROM payroll.sales_payroll_commission_items WHERE sales_payroll_id = NEW.id)
               OR NEW.bonus_total <> (SELECT COALESCE(sum(amount), 0) FROM payroll.sales_payroll_adjustments WHERE sales_payroll_id = NEW.id AND kind = 'Bonus')
               OR NEW.deductions_total <> (SELECT COALESCE(sum(amount), 0) FROM payroll.sales_payroll_adjustments WHERE sales_payroll_id = NEW.id AND kind = 'Deduction') THEN
                RAISE EXCEPTION 'G2: sales payroll totals differ from their items' USING ERRCODE = '23514';
            END IF;
        ELSE
            IF NEW.overtime_amount <> (SELECT COALESCE(sum(amount), 0) FROM payroll.assistant_payroll_overtime_items WHERE assistant_payroll_id = NEW.id) THEN
                RAISE EXCEPTION 'G2: assistant overtime differs from its items' USING ERRCODE = '23514';
            END IF;
        END IF;
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_doctor_payrolls_g2 BEFORE UPDATE ON payroll.doctor_payrolls FOR EACH ROW EXECUTE FUNCTION payroll.tg_payrolls_g2();
CREATE TRIGGER trg_sales_payrolls_g2 BEFORE UPDATE ON payroll.sales_payrolls FOR EACH ROW EXECUTE FUNCTION payroll.tg_payrolls_g2();
CREATE TRIGGER trg_assistant_payrolls_g2 BEFORE UPDATE ON payroll.assistant_payrolls FOR EACH ROW EXECUTE FUNCTION payroll.tg_payrolls_g2();

-- G2: no item or adjustment once the payroll is finalized; each item belongs to the payroll's person and month.
CREATE FUNCTION payroll.tg_doctor_payroll_items_g2() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_payroll payroll.doctor_payrolls;
BEGIN
    SELECT * INTO v_payroll FROM payroll.doctor_payrolls WHERE id = NEW.doctor_payroll_id;
    IF v_payroll.finalized_at IS NOT NULL THEN
        RAISE EXCEPTION 'payroll.not_draft' USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM finance.doctor_earnings e
                   WHERE e.id = NEW.doctor_earning_id AND e.doctor_id = v_payroll.doctor_id AND e.amount = NEW.amount
                     AND payroll.clinic_date(e.recognized_at) BETWEEN v_payroll.period_start AND v_payroll.period_end) THEN
        RAISE EXCEPTION 'G2: the earning belongs to another doctor or month' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_doctor_payroll_items_g2 BEFORE INSERT ON payroll.doctor_payroll_items
    FOR EACH ROW EXECUTE FUNCTION payroll.tg_doctor_payroll_items_g2();

CREATE FUNCTION payroll.tg_doctor_payroll_adjustments_g2() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF (SELECT finalized_at FROM payroll.doctor_payrolls WHERE id = NEW.doctor_payroll_id) IS NOT NULL THEN
        RAISE EXCEPTION 'payroll.not_draft' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_doctor_payroll_adjustments_g2 BEFORE INSERT ON payroll.doctor_payroll_adjustments
    FOR EACH ROW EXECUTE FUNCTION payroll.tg_doctor_payroll_adjustments_g2();

-- Commission items: the collection is in the payroll month and before the employee's deactivation, the rate revision is the
-- employee's own and was in force at the collection (a reversal uses the original collection's rate, which is older).
CREATE FUNCTION payroll.tg_sales_payroll_commission_items_g2() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_payroll payroll.sales_payrolls;
    v_deactivated timestamptz;
BEGIN
    SELECT * INTO v_payroll FROM payroll.sales_payrolls WHERE id = NEW.sales_payroll_id;
    IF v_payroll.finalized_at IS NOT NULL THEN
        RAISE EXCEPTION 'payroll.not_draft' USING ERRCODE = '23514';
    END IF;
    IF payroll.clinic_date(NEW.allocated_at) NOT BETWEEN v_payroll.period_start AND v_payroll.period_end
       OR NOT EXISTS (SELECT 1 FROM finance.payment_allocations a WHERE a.id = NEW.payment_allocation_id AND a.allocated_at = NEW.allocated_at
                      AND a.amount = NEW.base_amount) THEN
        RAISE EXCEPTION 'G2: the collection is outside the payroll month' USING ERRCODE = '23514';
    END IF;
    SELECT deactivated_at INTO v_deactivated FROM staff.sales_employees WHERE id = v_payroll.sales_employee_id;
    IF v_deactivated IS NOT NULL AND NEW.allocated_at >= v_deactivated THEN
        RAISE EXCEPTION 'G2: no commission after deactivation' USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM staff.sales_commission_rate_history r
                   WHERE r.id = NEW.rate_revision_id AND r.sales_employee_id = v_payroll.sales_employee_id
                     AND r.effective_from <= NEW.allocated_at AND r.commission_percentage = NEW.commission_percentage_applied) THEN
        RAISE EXCEPTION 'G2: the rate revision does not apply to this collection' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_sales_payroll_commission_items_g2 BEFORE INSERT ON payroll.sales_payroll_commission_items
    FOR EACH ROW EXECUTE FUNCTION payroll.tg_sales_payroll_commission_items_g2();

CREATE FUNCTION payroll.tg_sales_payroll_adjustments_g2() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF (SELECT finalized_at FROM payroll.sales_payrolls WHERE id = NEW.sales_payroll_id) IS NOT NULL THEN
        RAISE EXCEPTION 'payroll.not_draft' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_sales_payroll_adjustments_g2 BEFORE INSERT ON payroll.sales_payroll_adjustments
    FOR EACH ROW EXECUTE FUNCTION payroll.tg_sales_payroll_adjustments_g2();

-- Overtime items: the entry is the payroll Assistant's, inside the month, not voided, copied exactly.
CREATE FUNCTION payroll.tg_assistant_payroll_overtime_items_g2() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_payroll payroll.assistant_payrolls;
BEGIN
    SELECT * INTO v_payroll FROM payroll.assistant_payrolls WHERE id = NEW.assistant_payroll_id;
    IF v_payroll.finalized_at IS NOT NULL THEN
        RAISE EXCEPTION 'payroll.not_draft' USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM staff.assistant_overtime_entries o
                   WHERE o.id = NEW.overtime_entry_id AND o.assistant_id = v_payroll.assistant_id AND o.voided_at IS NULL
                     AND o.work_date BETWEEN v_payroll.period_start AND v_payroll.period_end
                     AND o.approved_hours = NEW.approved_hours AND o.hourly_rate_snapshot = NEW.hourly_rate_snapshot AND o.amount = NEW.amount) THEN
        RAISE EXCEPTION 'G2: the overtime entry does not belong to this payroll' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_assistant_payroll_overtime_items_g2 BEFORE INSERT ON payroll.assistant_payroll_overtime_items
    FOR EACH ROW EXECUTE FUNCTION payroll.tg_assistant_payroll_overtime_items_g2();

-- G2: an overtime entry used by a payroll item cannot be voided.
CREATE FUNCTION staff.tg_assistant_overtime_entries_void_g2() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.voided_at IS NULL AND NEW.voided_at IS NOT NULL
       AND EXISTS (SELECT 1 FROM payroll.assistant_payroll_overtime_items WHERE overtime_entry_id = NEW.id) THEN
        RAISE EXCEPTION 'overtime.in_payroll' USING ERRCODE = '23514';
    END IF;
    IF OLD.voided_at IS NOT NULL THEN
        RAISE EXCEPTION 'G2: a voided overtime entry is final' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_assistant_overtime_entries_void_g2 BEFORE UPDATE ON staff.assistant_overtime_entries
    FOR EACH ROW EXECUTE FUNCTION staff.tg_assistant_overtime_entries_void_g2();

-- R1 / R3: items and adjustments are insert-only; only the header working columns and the status columns are updatable.
GRANT USAGE ON SCHEMA payroll TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA payroll TO clinic_app;
GRANT UPDATE (revenue_total, lab_total, doctor_share_total, clinic_share_total, adjustments_total, final_amount, status, finalized_at, paid_at)
    ON payroll.doctor_payrolls TO clinic_app;
GRANT UPDATE (base_salary_snapshot, commission_total, bonus_total, deductions_total, status, finalized_at, paid_at)
    ON payroll.sales_payrolls TO clinic_app;
GRANT UPDATE (salary_snapshot, base_pay_amount, base_pay_source, partial_period_reason, base_pay_entered_by_user_id, overtime_amount,
    final_amount, status, finalized_at, paid_at) ON payroll.assistant_payrolls TO clinic_app;
GRANT SELECT ON ALL TABLES IN SCHEMA payroll TO clinic_backup;
