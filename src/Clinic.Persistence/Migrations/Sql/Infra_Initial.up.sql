-- Data Protection key ring (DEPLOYMENT_ARCHITECTURE.md §1.8): clinic_app SELECT and INSERT only;
-- clinic_worker, clinic_backup and PUBLIC have no access.
GRANT USAGE ON SCHEMA infra TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT, INSERT ON infra.data_protection_keys TO clinic_app;

-- Single migration history: readable by the running roles (readiness check) and the backup role.
GRANT SELECT ON infra."__EFMigrationsHistory" TO clinic_app, clinic_worker, clinic_backup;
