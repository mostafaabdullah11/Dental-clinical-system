-- Notification queue (API B4 / B5). Rows hold ids and the dedupe key only: never a patient name, phone or clinical value.
GRANT USAGE ON SCHEMA notifications TO clinic_app, clinic_worker, clinic_backup;

-- API: event-driven enqueue in the business transaction. Inbox read-state routes arrive with the inbox (Task 3).
GRANT SELECT, INSERT ON notifications.notifications TO clinic_app;

-- Worker: planners insert (ON CONFLICT DO NOTHING on dedupe_key); the dispatcher moves the delivery lifecycle only.
GRANT SELECT, INSERT ON notifications.notifications TO clinic_worker;
GRANT UPDATE (status, sent_at, retry_count, last_error) ON notifications.notifications TO clinic_worker;

GRANT SELECT ON ALL TABLES IN SCHEMA notifications TO clinic_backup;
