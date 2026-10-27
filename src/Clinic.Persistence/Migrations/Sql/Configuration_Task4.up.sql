-- Payment methods (FR-PAY-001): exactly Cash, Visa and InstaPay, Arabic display names. The Manager may deactivate a
-- method; codes are never added or removed by the application.
INSERT INTO configuration.payment_methods (code, display_name, is_active) VALUES
    ('Cash', 'نقدي', true),
    ('Visa', 'فيزا', true),
    ('InstaPay', 'إنستاباي', true);

-- No lab percentage is seeded: until the Manager sets one, a General-lab collection fails with
-- 422 lab.percentage_not_configured instead of silently using 0 (FR-LAB-002).

-- R1: lab_percentage_history is insert-only (effective-dated; history never rewritten).
GRANT SELECT, INSERT ON configuration.lab_percentage_history TO clinic_app;
GRANT SELECT ON configuration.payment_methods TO clinic_app;
GRANT UPDATE (is_active) ON configuration.payment_methods TO clinic_app;
GRANT SELECT ON configuration.lab_percentage_history, configuration.payment_methods TO clinic_backup;
