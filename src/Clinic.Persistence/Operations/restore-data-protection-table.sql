-- Restore step (DEPLOYMENT_ARCHITECTURE.md §3.3 / §3.4). The nightly dump EXCLUDES infra.data_protection_keys entirely:
-- pg_dump takes an ACCESS SHARE lock on every table it lists (even with --exclude-table-data), which needs SELECT, and
-- clinic_backup must never be able to read the key ring. After pg_restore, the restore procedure runs this file as
-- clinic_migrator to recreate the (empty) table with the migration's exact definition and grants. The application then
-- creates a fresh key ring on first use (old cookies are rejected; no business data is affected).
-- A test compares this definition with the migrated table so the two can never drift.
CREATE TABLE IF NOT EXISTS infra.data_protection_keys (
    id integer GENERATED ALWAYS AS IDENTITY,
    friendly_name text NULL,
    xml text NULL,
    CONSTRAINT pk_data_protection_keys PRIMARY KEY (id)
);
REVOKE ALL ON infra.data_protection_keys FROM PUBLIC;
GRANT SELECT, INSERT ON infra.data_protection_keys TO clinic_app;
