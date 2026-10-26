-- Dual-entry feedback (FR-FDB-001..011). No expiry, no stored status, no result columns on the request.
-- feedback_entries is insert-only (R1); a request moves only through link preparation and mark-sent.
GRANT USAGE ON SCHEMA feedback TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT, INSERT ON feedback.feedback_requests, feedback.feedback_entries TO clinic_app;
GRANT UPDATE (link_token_hash, link_sent_at, sent_by_user_id, delivery_channel) ON feedback.feedback_requests TO clinic_app;

-- J4 re-validation of Feedback Request Pending: existence and submission only; never ratings, comments or tokens.
GRANT SELECT (id) ON feedback.feedback_requests TO clinic_worker;
GRANT SELECT (feedback_request_id) ON feedback.feedback_entries TO clinic_worker;

GRANT SELECT ON feedback.feedback_requests, feedback.feedback_entries TO clinic_backup;
