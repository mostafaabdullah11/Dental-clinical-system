-- Task 3 application-side notification writes (FR-NOTIF-007 / API B6): the in-app inbox marks rows read, and a source
-- transaction cancels its own still-Pending reminders (a cancelled or rescheduled appointment). Delivery, retries and
-- errors stay Worker-only.
GRANT UPDATE (status, read_at) ON notifications.notifications TO clinic_app;
