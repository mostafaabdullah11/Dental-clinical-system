-- Backup-grant decision (DEPLOYMENT_ARCHITECTURE.md §3.3 layer 2; SECURITY_DESIGN.md §15): clinic_backup has explicit
-- SELECT on every table AND sequence of every business schema (pg_dump reads identity-sequence state), never
-- pg_read_all_data, never a write privilege. infra keeps only the migration history (granted in Infra_Initial);
-- infra.data_protection_keys stays unreadable (the dump excludes that table entirely).
-- Forward-only; idempotent (GRANT is a no-op when already held).
DO $$
DECLARE
    s text;
BEGIN
    FOREACH s IN ARRAY ARRAY['audit', 'clinical', 'configuration', 'crm', 'expenses', 'feedback', 'finance', 'identity', 'notifications',
                             'orthodontics', 'patients', 'payroll', 'prescriptions', 'scheduling', 'staff']
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO clinic_backup', s);
        EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO clinic_backup', s);
        EXECUTE format('GRANT SELECT ON ALL SEQUENCES IN SCHEMA %I TO clinic_backup', s);
    END LOOP;
END
$$;
