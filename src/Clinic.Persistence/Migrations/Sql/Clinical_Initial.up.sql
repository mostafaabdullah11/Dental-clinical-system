-- Procedure catalog (FR-CLN-003, DATABASE_DESIGN.md catalog mapping). option_definition is interpreted generically
-- by ProcedureCatalog: teeth none / optional / single / required; fields enum / text with required, requiredWhen and
-- allowedWhen. Any member that is not declared (sutures, bone removal, quality outside Laser Bleaching, a type on
-- Pediatric Extraction) is refused with catalog.option_invalid. Lab model Fixed = Implant, Fixed Prosthetics,
-- Removable Prosthetics; everything else General (Orthodontics General, TECHNICAL placeholder).
INSERT INTO clinical.procedure_types (id, code, name, category, lab_model, option_definition, is_active, sort_order) VALUES
    (gen_random_uuid(), 'RootCanal', 'علاج عصب', 'Endodontics', 'General', '{"teeth":"single","fields":[]}', true, 10),
    (gen_random_uuid(), 'Filling', 'حشو', 'Restorative', 'General',
     '{"teeth":"required","fields":[{"name":"filling_type","kind":"enum","values":["GIC","Composite"],"required":true},{"name":"composite_zone","kind":"enum","values":["Anterior","Posterior"],"requiredWhen":{"field":"filling_type","equals":"Composite"},"allowedWhen":{"field":"filling_type","equals":"Composite"}}]}',
     true, 20),
    (gen_random_uuid(), 'Extraction', 'خلع', 'Surgery', 'General',
     '{"teeth":"required","fields":[{"name":"extraction_type","kind":"enum","values":["Simple","Surgical","AdvancedSurgical"],"required":true}]}', true, 30),
    (gen_random_uuid(), 'PediatricPulpotomy', 'بتر اللب (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 40),
    (gen_random_uuid(), 'PediatricPulpectomy', 'استئصال اللب (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 41),
    (gen_random_uuid(), 'PediatricSpaceMaintainer', 'حافظ مسافة (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 42),
    (gen_random_uuid(), 'PediatricStainlessSteelCrown', 'تاج ستانلس ستيل (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 43),
    (gen_random_uuid(), 'PediatricExtraction', 'خلع (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 44),
    (gen_random_uuid(), 'PediatricZirconCrown', 'تاج زيركون (أطفال)', 'Pediatric', 'General', '{"teeth":"optional","fields":[]}', true, 45),
    (gen_random_uuid(), 'PediatricOther', 'أخرى (أطفال)', 'Pediatric', 'General',
     '{"teeth":"optional","fields":[{"name":"name","kind":"text","required":true},{"name":"details","kind":"text"}]}', true, 46),
    (gen_random_uuid(), 'Implant', 'زراعة', 'Implant', 'Fixed', '{"teeth":"required","fields":[]}', true, 50),
    (gen_random_uuid(), 'FixedProsthetics', 'تركيبات ثابتة', 'Prosthetics', 'Fixed',
     '{"teeth":"required","fields":[{"name":"material","kind":"enum","values":["PorcelainPFM","Zirconia","Emax"],"required":true}]}', true, 60),
    (gen_random_uuid(), 'RemovableProsthetics', 'تركيبات متحركة', 'Prosthetics', 'Fixed',
     '{"teeth":"optional","fields":[{"name":"type","kind":"enum","values":["Complete","Partial"],"required":true},{"name":"material","kind":"enum","values":["FlexibleAcrylic","StandardAcrylic"],"required":true}]}',
     true, 61),
    (gen_random_uuid(), 'ScalingCleaning', 'تنظيف الجير', 'Periodontics', 'General', '{"teeth":"optional","fields":[]}', true, 70),
    (gen_random_uuid(), 'DeepCleaning', 'تنظيف عميق', 'Periodontics', 'General', '{"teeth":"optional","fields":[]}', true, 71),
    (gen_random_uuid(), 'Polishing', 'تلميع', 'Periodontics', 'General', '{"teeth":"optional","fields":[]}', true, 72),
    (gen_random_uuid(), 'LaserBleaching', 'تبييض بالليزر', 'Cosmetic', 'General',
     '{"teeth":"optional","fields":[{"name":"quality","kind":"enum","values":["Basic","HighQuality"],"required":true}]}', true, 80),
    (gen_random_uuid(), 'Orthodontics', 'تقويم', 'Orthodontics', 'General',
     '{"teeth":"none","fields":[{"name":"area","kind":"enum","values":["UpperArch","LowerArch","Appliance"],"required":true},{"name":"appliance_type","kind":"text","requiredWhen":{"field":"area","equals":"Appliance"},"allowedWhen":{"field":"area","equals":"Appliance"}}]}',
     true, 90),
    (gen_random_uuid(), 'Other', 'أخرى', 'Other', 'General',
     '{"teeth":"optional","fields":[{"name":"name","kind":"text","required":true},{"name":"details","kind":"text"}]}', true, 100);

-- G1 (Q-CLN-1): one shared check in infra. A record owned by a Finalized Visit changes only when the transaction
-- carries clinic.audited_correction = 'on' AND a visit_corrections row of this transaction exists whose correcting
-- Doctor is the Visit's own Doctor. Only the Doctor-correction path satisfies both; the Manager never does.
CREATE FUNCTION infra.assert_visit_editable_or_corrected(p_visit_id uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_status text;
    v_doctor uuid;
BEGIN
    IF p_visit_id IS NULL THEN
        RETURN;
    END IF;
    SELECT v.status, v.doctor_id INTO v_status, v_doctor FROM clinical.visits v WHERE v.id = p_visit_id;
    IF v_status IS DISTINCT FROM 'Finalized' THEN
        RETURN;
    END IF;
    IF coalesce(current_setting('clinic.audited_correction', true), '') = 'on'
       AND EXISTS (SELECT 1 FROM clinical.visit_corrections c
                   WHERE c.visit_id = p_visit_id AND c.correcting_doctor_id = v_doctor AND c.txid = txid_current()) THEN
        RETURN;
    END IF;
    RAISE EXCEPTION 'G1: records of a finalized visit change only through the audited Doctor correction' USING ERRCODE = '23514';
END
$$;

-- Visits: once Finalized, status / finalization / fee never change; notes only through G1.
CREATE FUNCTION clinical.tg_visits_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.status = 'Finalized' THEN
        IF NEW.status IS DISTINCT FROM OLD.status OR NEW.finalized_at IS DISTINCT FROM OLD.finalized_at
           OR NEW.finalized_by_user_id IS DISTINCT FROM OLD.finalized_by_user_id
           OR NEW.consultation_fee IS DISTINCT FROM OLD.consultation_fee THEN
            RAISE EXCEPTION 'G1: a finalized visit is never reopened and its fee never changes' USING ERRCODE = '23514';
        END IF;
        IF NEW.clinical_notes IS DISTINCT FROM OLD.clinical_notes THEN
            PERFORM infra.assert_visit_editable_or_corrected(OLD.id);
        END IF;
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_visits_g1 BEFORE UPDATE ON clinical.visits FOR EACH ROW EXECUTE FUNCTION clinical.tg_visits_g1();

CREATE FUNCTION clinical.tg_treatment_records_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM infra.assert_visit_editable_or_corrected(NEW.visit_id);
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_treatment_records_g1 BEFORE INSERT OR UPDATE ON clinical.treatment_records
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_treatment_records_g1();

-- Steps persist across Visits: the completing Visit owns the completion.
CREATE FUNCTION clinical.tg_treatment_steps_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND OLD.completed_in_visit_id IS NOT NULL THEN
        PERFORM infra.assert_visit_editable_or_corrected(OLD.completed_in_visit_id);
    END IF;
    IF NEW.completed_in_visit_id IS NOT NULL
       AND (TG_OP = 'INSERT' OR NEW.completed_in_visit_id IS DISTINCT FROM OLD.completed_in_visit_id) THEN
        PERFORM infra.assert_visit_editable_or_corrected(NEW.completed_in_visit_id);
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_treatment_steps_g1 BEFORE INSERT OR UPDATE ON clinical.treatment_steps
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_treatment_steps_g1();

-- Measurements become insert-only history after finalization (a new measurement belongs to a new, editable Visit).
CREATE FUNCTION clinical.tg_root_canal_measurements_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM infra.assert_visit_editable_or_corrected(NEW.recorded_in_visit_id);
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_root_canal_measurements_g1 BEFORE INSERT OR UPDATE ON clinical.root_canal_measurements
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_root_canal_measurements_g1();

-- A case's teeth / area / details belong to the Visit that started it (its first treatment record).
CREATE FUNCTION clinical.tg_treatment_cases_g1() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.teeth IS DISTINCT FROM OLD.teeth OR NEW.area IS DISTINCT FROM OLD.area OR NEW.details IS DISTINCT FROM OLD.details THEN
        PERFORM infra.assert_visit_editable_or_corrected(
            (SELECT r.visit_id FROM clinical.treatment_records r WHERE r.treatment_case_id = OLD.id ORDER BY r.created_at, r.id LIMIT 1));
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_treatment_cases_g1 BEFORE UPDATE ON clinical.treatment_cases
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_treatment_cases_g1();

-- G6: the initial price is frozen once the Visit is Finalized (financial values are outside the correction path; a
-- later change is the Manager's Finance correction, Task 4).
CREATE FUNCTION clinical.tg_case_work_items_g6() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.entered_price IS DISTINCT FROM OLD.entered_price
       AND EXISTS (SELECT 1 FROM clinical.treatment_records r JOIN clinical.visits v ON v.id = r.visit_id
                   WHERE r.id = OLD.treatment_record_id AND v.status = 'Finalized') THEN
        RAISE EXCEPTION 'G6: the price of a finalized visit''s work item is frozen' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_case_work_items_g6 BEFORE UPDATE ON clinical.case_work_items
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_case_work_items_g6();

-- G6: no discount entry once the line's Visit is Finalized; the amount never exceeds the line's price.
CREATE FUNCTION clinical.tg_pricing_discount_entries_g6() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status text;
    v_price numeric;
BEGIN
    IF NEW.case_work_item_id IS NOT NULL THEN
        SELECT v.status, w.entered_price INTO v_status, v_price
        FROM clinical.case_work_items w
        JOIN clinical.treatment_records r ON r.id = w.treatment_record_id
        JOIN clinical.visits v ON v.id = r.visit_id
        WHERE w.id = NEW.case_work_item_id;
    ELSE
        SELECT v.status, v.consultation_fee INTO v_status, v_price FROM clinical.visits v WHERE v.id = NEW.consultation_visit_id;
    END IF;
    IF v_status = 'Finalized' THEN
        RAISE EXCEPTION 'G6: no discount entry after the visit is finalized' USING ERRCODE = '23514';
    END IF;
    IF v_price IS NULL OR NEW.amount > v_price THEN
        RAISE EXCEPTION 'G6: a discount never exceeds the line price' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END
$$;
CREATE TRIGGER trg_pricing_discount_entries_g6 BEFORE INSERT ON clinical.pricing_discount_entries
    FOR EACH ROW EXECUTE FUNCTION clinical.tg_pricing_discount_entries_g6();

-- Least privilege: no DELETE; column-restricted UPDATE; ledger / history tables insert-only (R1).
GRANT USAGE ON SCHEMA clinical TO clinic_app, clinic_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA clinical TO clinic_app, clinic_backup;
GRANT INSERT ON clinical.visits, clinical.treatment_cases, clinical.treatment_records, clinical.case_work_items,
    clinical.treatment_steps, clinical.root_canal_measurements, clinical.implant_details, clinical.pricing_discount_entries,
    clinical.visit_corrections TO clinic_app;

GRANT UPDATE (status, finalized_at, finalized_by_user_id, clinical_notes, consultation_fee) ON clinical.visits TO clinic_app;
GRANT UPDATE (status, teeth, area, details, completed_at) ON clinical.treatment_cases TO clinic_app;
GRANT UPDATE (entered_price) ON clinical.case_work_items TO clinic_app;
GRANT UPDATE (status, completed_at, completed_in_visit_id, completed_by_doctor_id) ON clinical.treatment_steps TO clinic_app;
GRANT UPDATE (length_mm) ON clinical.root_canal_measurements TO clinic_app;
GRANT UPDATE (has_bone_graft, has_membrane, procedure_date, healing_months) ON clinical.implant_details TO clinic_app;
-- procedure_types (catalog), treatment_records, pricing_discount_entries and visit_corrections: no UPDATE grant.
