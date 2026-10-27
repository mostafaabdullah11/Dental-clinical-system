-- Section 24 #12: an overtime entry linked to an attendance row must be the same Assistant and the same date. The parent
-- key ux_attendance_records_id_assistant_date has nullable columns (assistant_id is NULL for Sales rows), so the FK is
-- declared here instead of as an EF alternate key. MATCH SIMPLE: an entry without an attendance link is not checked.
ALTER TABLE staff.assistant_overtime_entries
    ADD CONSTRAINT fk_assistant_overtime_entries_attendance_assistant_date
    FOREIGN KEY (attendance_record_id, assistant_id, work_date)
    REFERENCES staff.attendance_records (id, assistant_id, attendance_date);

-- Manager-maintained attendance (no self-check-in, no automatic deduction) and approved overtime. No DELETE grant: an
-- overtime entry is voided (the void columns are the only updatable ones), never removed.
GRANT SELECT, INSERT ON staff.attendance_records, staff.assistant_overtime_entries TO clinic_app;
GRANT UPDATE (status, check_in_time, check_out_time, notes, updated_by_user_id, updated_at) ON staff.attendance_records TO clinic_app;
GRANT UPDATE (voided_at, voided_by_user_id, void_reason) ON staff.assistant_overtime_entries TO clinic_app;
GRANT SELECT ON staff.attendance_records, staff.assistant_overtime_entries TO clinic_backup;
