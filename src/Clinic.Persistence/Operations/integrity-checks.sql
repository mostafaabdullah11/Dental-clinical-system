-- Finance and payroll integrity checks (DEPLOYMENT_ARCHITECTURE.md §3.3; SECURITY_DESIGN.md §17 step 4).
-- Read-only. One row per invariant: (check_name, violations). Any violations > 0 fails the caller:
--   * the nightly backup job (deploy/backup), inside the dump's snapshot, as clinic_backup;
--   * the post-restore-lockdown operator command, as clinic_app;
--   * the restore drill, against the throwaway restored database.
-- The database guards (G2, G5, G8, G8b, G9, G10) already make these impossible to commit; this is the independent
-- after-the-fact check that a restore, a manual intervention or a disabled constraint did not break them.
WITH
billed AS (
    SELECT c.id, c.final_price + COALESCE((SELECT sum(a.amount_delta) FROM finance.charge_adjustments a WHERE a.charge_id = c.id), 0) AS billed,
           COALESCE((SELECT sum(x.amount) FROM finance.payment_allocations x WHERE x.charge_id = c.id), 0) AS collected
    FROM finance.patient_charges c),
fixed_cases AS (
    SELECT c.treatment_case_id,
           (SELECT r.amount FROM finance.fixed_lab_revisions r WHERE r.treatment_case_id = c.treatment_case_id
             ORDER BY r.revision_number DESC LIMIT 1) AS lab_amount,
           sum((SELECT COALESCE(sum(x.amount), 0) FROM finance.payment_allocations x WHERE x.charge_id = c.id)) AS collected,
           sum((SELECT COALESCE(sum(e.amount), 0) FROM finance.doctor_entitlements e WHERE e.charge_id = c.id)) AS entitled
    FROM finance.patient_charges c
    WHERE c.charge_kind = 'Treatment' AND c.lab_model = 'Fixed'
    GROUP BY c.treatment_case_id),
fixed_actual AS (
    SELECT f.treatment_case_id, f.lab_amount, f.collected, f.entitled,
           COALESCE((SELECT sum(d.lab_amount) FROM finance.revenue_distributions d WHERE d.treatment_case_id = f.treatment_case_id), 0) AS lab_paid,
           COALESCE((SELECT sum(er.amount) FROM finance.doctor_earnings er
                       JOIN finance.payment_allocations a ON a.id = er.payment_allocation_id
                       JOIN finance.patient_charges c ON c.id = a.charge_id
                      WHERE c.treatment_case_id = f.treatment_case_id AND c.lab_model = 'Fixed'), 0) AS earned
    FROM fixed_cases f)
SELECT 'g8b_event_identity' AS check_name, count(*) AS violations
FROM finance.distribution_events ev
WHERE ev.basis_amount <> COALESCE((SELECT sum(d.lab_amount + d.clinic_share) FROM finance.revenue_distributions d WHERE d.event_id = ev.id), 0)
                       + COALESCE((SELECT sum(er.amount) FROM finance.doctor_earnings er WHERE er.event_id = ev.id), 0)
UNION ALL
SELECT 'g8_payment_conservation', count(*)
FROM finance.payments p
WHERE p.amount <> COALESCE((SELECT sum(a.amount) FROM finance.payment_allocations a WHERE a.payment_id = p.id), 0)
                + COALESCE((SELECT sum(c.amount) FROM finance.patient_credit_entries c WHERE c.payment_id = p.id), 0)
UNION ALL
SELECT 'negative_patient_credit', count(*)
FROM (SELECT patient_id FROM finance.patient_credit_entries GROUP BY patient_id HAVING sum(amount) < 0) n
UNION ALL
SELECT 'collected_outside_billed', count(*) FROM billed WHERE collected < 0 OR collected > billed
UNION ALL
SELECT 'negative_cumulative_clinic_share', count(*)
FROM (SELECT payment_allocation_id FROM finance.revenue_distributions GROUP BY payment_allocation_id HAVING sum(clinic_share) < 0) n
UNION ALL
SELECT 'earnings_exceed_entitlements', count(*)
FROM finance.payment_allocations a
WHERE COALESCE((SELECT sum(er.amount) FROM finance.doctor_earnings er WHERE er.payment_allocation_id = a.id), 0)
      > COALESCE((SELECT sum(e.amount) FROM finance.doctor_entitlements e WHERE e.payment_allocation_id = a.id), 0)
   OR COALESCE((SELECT sum(er.amount) FROM finance.doctor_earnings er WHERE er.payment_allocation_id = a.id), 0) < 0
UNION ALL
-- Lab-First replay at case level: the Lab is covered first (min(lab, collected)); the Doctor is then paid up to the
-- entitlements from what remains; the Clinic keeps the rest and never goes negative.
SELECT 'fixed_lab_waterfall_mismatch', count(*)
FROM fixed_actual f
WHERE f.lab_amount IS NULL
   OR f.lab_paid <> LEAST(f.lab_amount, f.collected)
   OR f.earned <> LEAST(f.entitled, GREATEST(f.collected - LEAST(f.lab_amount, f.collected), 0))
UNION ALL
SELECT 'assistant_payroll_total_mismatch', count(*)
FROM payroll.assistant_payrolls p
WHERE p.final_amount <> p.base_pay_amount + p.overtime_amount
   OR (p.finalized_at IS NOT NULL
       AND p.overtime_amount <> COALESCE((SELECT sum(i.amount) FROM payroll.assistant_payroll_overtime_items i WHERE i.assistant_payroll_id = p.id), 0))
UNION ALL
SELECT 'overtime_entry_in_wrong_payroll', count(*)
FROM payroll.assistant_payroll_overtime_items i
JOIN payroll.assistant_payrolls p ON p.id = i.assistant_payroll_id
JOIN staff.assistant_overtime_entries o ON o.id = i.overtime_entry_id
WHERE o.assistant_id <> p.assistant_id OR o.work_date NOT BETWEEN p.period_start AND p.period_end
   OR (SELECT count(*) FROM payroll.assistant_payroll_overtime_items x WHERE x.overtime_entry_id = i.overtime_entry_id) > 1
UNION ALL
SELECT 'discount_exceeds_line_price', count(*)
FROM (SELECT DISTINCT ON (case_work_item_id, consultation_visit_id) *
        FROM clinical.pricing_discount_entries ORDER BY case_work_item_id, consultation_visit_id, entry_seq DESC) e
LEFT JOIN clinical.case_work_items w ON w.id = e.case_work_item_id
LEFT JOIN clinical.visits v ON v.id = e.consultation_visit_id
WHERE e.amount > COALESCE(w.entered_price, v.consultation_fee, 0)
UNION ALL
SELECT 'charge_discount_exceeds_price', count(*) FROM finance.patient_charges WHERE discount_amount > original_price;
