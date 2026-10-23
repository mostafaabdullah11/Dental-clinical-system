#!/bin/sh
# Development only: runs inside the postgres container on first start (docker-entrypoint-initdb.d).
# Substitutes the role passwords from the environment and creates the `clinic` database.
set -eu

render() {
  sed -e "s/{{CLINIC_MIGRATOR_PASSWORD}}/${CLINIC_MIGRATOR_PASSWORD}/g" \
      -e "s/{{CLINIC_APP_PASSWORD}}/${CLINIC_APP_PASSWORD}/g" \
      -e "s/{{CLINIC_WORKER_PASSWORD}}/${CLINIC_WORKER_PASSWORD}/g" \
      -e "s/{{CLINIC_BACKUP_PASSWORD}}/${CLINIC_BACKUP_PASSWORD}/g" \
      -e "s/{{DATABASE_NAME}}/clinic/g" "$1"
}

render /clinic-db/bootstrap-roles.sql | psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres
render /clinic-db/create-database.sql | grep -v '^--' | psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres
