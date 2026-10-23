-- Creates the application database owned by clinic_migrator (tables and views are owned by the migrator;
-- owners bypass grants, so the running roles get only what the migrations grant).
-- {{DATABASE_NAME}} is substituted by the caller. Each statement runs outside a transaction.
CREATE DATABASE "{{DATABASE_NAME}}" OWNER clinic_migrator;
REVOKE ALL ON DATABASE "{{DATABASE_NAME}}" FROM PUBLIC;
GRANT CONNECT ON DATABASE "{{DATABASE_NAME}}" TO clinic_app, clinic_worker, clinic_backup;
