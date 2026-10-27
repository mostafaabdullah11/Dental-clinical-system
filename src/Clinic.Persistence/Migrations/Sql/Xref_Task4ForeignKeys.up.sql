-- Section 24 #16: an ortho case's charge is a charge of the same treatment case. The parent key (treatment_case_id, id)
-- has a nullable column (Consultation charges), so it is declared in Finance_Ledger.up.sql and referenced here.
-- MATCH SIMPLE: skipped while charge_id is NULL (before Visit finalization).
ALTER TABLE orthodontics.ortho_cases
    ADD CONSTRAINT fk_ortho_cases_charges_case_charge
    FOREIGN KEY (treatment_case_id, charge_id) REFERENCES finance.patient_charges (treatment_case_id, id);
