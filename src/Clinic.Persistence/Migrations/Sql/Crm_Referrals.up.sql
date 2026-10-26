-- Doctor referrals (FR-REF-001..004). The Doctor's notes column is never projected to Sales (architecture test);
-- Sales may change only follow_up_status.
GRANT SELECT, INSERT ON crm.referrals TO clinic_app;
GRANT UPDATE (follow_up_status) ON crm.referrals TO clinic_app;

-- J4 re-validation of New Doctor Referral notifications: existence only, never reason, notes or instructions.
GRANT SELECT (id) ON crm.referrals TO clinic_worker;

GRANT SELECT ON crm.referrals TO clinic_backup;
