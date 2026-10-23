-- Lead status catalogue: exactly the 8 frozen statuses. Open / closed classification by Owner decision 2026-10-23:
-- closed = Converted and NotInterested; every other status is open. A converted lead is always closed (predicate).
INSERT INTO crm.lead_statuses (code, is_open, sort_order) VALUES
    ('New', true, 1),
    ('Contacted', true, 2),
    ('Interested', true, 3),
    ('CallLater', true, 4),
    ('NoAnswer', true, 5),
    ('NotInterested', false, 6),
    ('Booked', true, 7),
    ('Converted', false, 8);

-- The patient link is written once by conversion and never changed or cleared afterwards.
CREATE FUNCTION crm.tg_leads_conversion_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.converted_patient_id IS NOT NULL AND NEW.converted_patient_id IS DISTINCT FROM OLD.converted_patient_id THEN
        RAISE EXCEPTION 'crm.leads.converted_patient_id is immutable once set' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;

CREATE TRIGGER trg_leads_conversion_immutable
    BEFORE UPDATE OF converted_patient_id ON crm.leads
    FOR EACH ROW EXECUTE FUNCTION crm.tg_leads_conversion_immutable();

-- Least privilege (SECURITY_DESIGN.md §15): no DELETE anywhere; column-restricted UPDATE.
GRANT USAGE ON SCHEMA crm TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT ON crm.lead_statuses TO clinic_app, clinic_worker;
GRANT SELECT, INSERT ON crm.leads, crm.follow_ups, crm.follow_up_contact_attempts, crm.responsibility_history TO clinic_app;

-- Leads: original_sales_employee_id (frozen attribution), created_* and id are never updated.
GRANT UPDATE (name, phone, source, interested_service, notes, next_follow_up_date, status,
    responsible_sales_employee_id, converted_patient_id) ON crm.leads TO clinic_app;

-- Follow-ups: type, source references and created_* are fixed at creation.
GRANT UPDATE (assigned_sales_employee_id, due_date, status, notes, result, next_follow_up_date) ON crm.follow_ups TO clinic_app;

-- follow_up_contact_attempts and responsibility_history are insert-only (no UPDATE grant).

-- Worker (J3 planner / J7 counts): ids, type, dates, status and owner only; never notes, result, name or phone.
GRANT SELECT (id, follow_up_type, patient_id, lead_id, assigned_sales_employee_id, due_date, status) ON crm.follow_ups TO clinic_worker;
GRANT SELECT (id, status, responsible_sales_employee_id, converted_patient_id) ON crm.leads TO clinic_worker;

GRANT SELECT ON ALL TABLES IN SCHEMA crm TO clinic_backup;
