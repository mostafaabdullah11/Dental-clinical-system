-- Database role bootstrap (DEPLOYMENT_ARCHITECTURE.md §1.7). Run ONCE per environment as an administrator,
-- before the first migration. Passwords cannot live in migrations; migrations GRANT to these role names.
--
-- Placeholders {{CLINIC_MIGRATOR_PASSWORD}}, {{CLINIC_APP_PASSWORD}}, {{CLINIC_WORKER_PASSWORD}} and
-- {{CLINIC_BACKUP_PASSWORD}} are substituted by the caller (dev init script or the integration-test harness).
-- Values must not contain a single quote. Never commit real values.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinic_migrator') THEN
        EXECUTE format('CREATE ROLE clinic_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', '{{CLINIC_MIGRATOR_PASSWORD}}');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinic_app') THEN
        EXECUTE format('CREATE ROLE clinic_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', '{{CLINIC_APP_PASSWORD}}');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinic_worker') THEN
        EXECUTE format('CREATE ROLE clinic_worker LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', '{{CLINIC_WORKER_PASSWORD}}');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'clinic_backup') THEN
        EXECUTE format('CREATE ROLE clinic_backup LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 2 PASSWORD %L', '{{CLINIC_BACKUP_PASSWORD}}');
    END IF;
END
$$;
