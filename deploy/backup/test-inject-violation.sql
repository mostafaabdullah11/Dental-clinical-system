-- TEST ONLY (deploy/backup/test-backup.sh): plants one payment whose allocations + credit do not equal its amount, with
-- triggers and FKs bypassed, so the nightly integrity check must fail with exit code 3. Never run outside a throwaway DB.
SET session_replication_role = replica;
INSERT INTO patients.patients (id, full_name, date_of_birth, registered_at, is_active, created_at)
VALUES ('00000000-0000-0000-0000-00000000bb01', 'Integrity Test', '1990-01-01', now(), true, now());
INSERT INTO finance.payments (id, patient_id, amount, kind, payment_method_code, paid_at, received_by_user_id)
VALUES ('00000000-0000-0000-0000-00000000bb02', '00000000-0000-0000-0000-00000000bb01', 100, 'Receipt', 'Cash', now(),
        '00000000-0000-0000-0000-00000000bb03');
