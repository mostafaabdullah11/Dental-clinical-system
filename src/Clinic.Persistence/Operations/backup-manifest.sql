-- Backup manifest (DEPLOYMENT_ARCHITECTURE.md §3.3): per-table row counts, latest audit id and ledger totals, taken in the
-- same snapshot as the dump. Output: (item, value) rows, compared after a restore drill. Read-only; works as clinic_backup
-- (explicit SELECT grants). infra.data_protection_keys is never read.
SELECT 'rows:' || t.table_schema || '.' || t.table_name AS item,
       (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', t.table_schema, t.table_name), false, true, '')))[1]::text AS value
FROM information_schema.tables t
WHERE t.table_type = 'BASE TABLE'
  AND t.table_schema IN ('audit', 'clinical', 'configuration', 'crm', 'expenses', 'feedback', 'finance', 'identity', 'infra', 'notifications',
                         'orthodontics', 'patients', 'payroll', 'prescriptions', 'scheduling', 'staff')
  AND NOT (t.table_schema = 'infra' AND t.table_name = 'data_protection_keys')
UNION ALL SELECT 'latest_audit_id', COALESCE(max(id), 0)::text FROM audit.audit_log
UNION ALL SELECT 'sum:payments', COALESCE(sum(amount), 0)::text FROM finance.payments
UNION ALL SELECT 'sum:payment_allocations', COALESCE(sum(amount), 0)::text FROM finance.payment_allocations
UNION ALL SELECT 'sum:patient_credit_entries', COALESCE(sum(amount), 0)::text FROM finance.patient_credit_entries
UNION ALL SELECT 'sum:doctor_entitlements', COALESCE(sum(amount), 0)::text FROM finance.doctor_entitlements
UNION ALL SELECT 'sum:doctor_earnings', COALESCE(sum(amount), 0)::text FROM finance.doctor_earnings
UNION ALL SELECT 'sum:revenue_distributions_lab', COALESCE(sum(lab_amount), 0)::text FROM finance.revenue_distributions
UNION ALL SELECT 'sum:revenue_distributions_clinic', COALESCE(sum(clinic_share), 0)::text FROM finance.revenue_distributions
UNION ALL SELECT 'latest_migration', COALESCE(max(migration_id), '') FROM infra."__EFMigrationsHistory"
ORDER BY 1;
