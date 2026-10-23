-- audit.audit_log is insert-only for the application (SECURITY_DESIGN.md §9): INSERT and SELECT only.
GRANT USAGE ON SCHEMA audit TO clinic_app, clinic_backup;
GRANT SELECT, INSERT ON audit.audit_log TO clinic_app;
GRANT SELECT ON audit.audit_log TO clinic_backup;
