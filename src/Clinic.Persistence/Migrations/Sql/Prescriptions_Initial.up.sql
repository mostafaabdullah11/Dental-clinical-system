-- G1 (Q-CLN-1) on prescriptions and their items: a prescription of a Finalized Visit changes only through the
-- audited correction of the Visit's own Doctor (FR-RX-004).
CREATE FUNCTION prescriptions.tg_prescriptions_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM infra.assert_visit_editable_or_corrected(NEW.visit_id);
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_prescriptions_g1 BEFORE INSERT OR UPDATE ON prescriptions.prescriptions
    FOR EACH ROW EXECUTE FUNCTION prescriptions.tg_prescriptions_g1();

CREATE FUNCTION prescriptions.tg_prescription_items_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM infra.assert_visit_editable_or_corrected((SELECT p.visit_id FROM prescriptions.prescriptions p WHERE p.id = NEW.prescription_id));
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_prescription_items_g1 BEFORE INSERT OR UPDATE ON prescriptions.prescription_items
    FOR EACH ROW EXECUTE FUNCTION prescriptions.tg_prescription_items_g1();

-- Medication details are C-Clinical: no worker access. No DELETE (items are soft-removed).
GRANT USAGE ON SCHEMA prescriptions TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON prescriptions.prescriptions, prescriptions.prescription_items TO clinic_app;
GRANT UPDATE (general_notes, voided_at) ON prescriptions.prescriptions TO clinic_app;
GRANT UPDATE (sort_order, medication_name, dosage, frequency, duration, instructions, removed_at) ON prescriptions.prescription_items TO clinic_app;
GRANT SELECT ON prescriptions.prescriptions, prescriptions.prescription_items TO clinic_backup;
