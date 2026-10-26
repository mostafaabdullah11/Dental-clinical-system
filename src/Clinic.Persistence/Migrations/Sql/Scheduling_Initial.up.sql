-- Availability ranges of one Doctor may never overlap while live (withdrawn ranges are history). Appointments are
-- deliberately NOT constrained: overlapping appointments for the same Doctor are allowed (Q-APP-1/2).
-- btree_gist is a trusted extension (database CREATE privilege suffices); installed in infra, owned by the migrator.
CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA infra;

ALTER TABLE scheduling.availability_ranges
    ADD CONSTRAINT ex_availability_ranges_no_overlap
    EXCLUDE USING gist (doctor_id WITH =, tsrange(day_date + start_time, day_date + end_time) WITH &&)
    WHERE (withdrawn_at IS NULL);

-- Booked ranges that still count: released statuses (Cancelled, NoShow, Rescheduled) are excluded; the effective end
-- is LEAST(booked end, visit end) so ending a Visit early releases the unused future time (FR-SCH-013) without
-- rewriting the appointment.
CREATE VIEW scheduling.v_effective_booked_ranges AS
SELECT a.id AS appointment_id,
       a.doctor_id,
       a.start_at,
       a.start_at + make_interval(mins => a.duration_minutes) AS nominal_end_at,
       CASE
           WHEN a.visit_ended_at IS NULL THEN a.start_at + make_interval(mins => a.duration_minutes)
           ELSE LEAST(a.start_at + make_interval(mins => a.duration_minutes), GREATEST(a.visit_ended_at, a.start_at))
       END AS effective_end_at
FROM scheduling.appointments a
WHERE a.status NOT IN ('Cancelled', 'NoShow', 'Rescheduled');

-- Least privilege (SECURITY_DESIGN.md §15): no DELETE; column-restricted UPDATE.
GRANT USAGE ON SCHEMA scheduling TO clinic_app, clinic_worker, clinic_backup;
GRANT SELECT, INSERT ON scheduling.availability_weeks, scheduling.availability_ranges, scheduling.appointments,
    scheduling.appointment_arrivals TO clinic_app;
GRANT SELECT ON scheduling.v_effective_booked_ranges TO clinic_app;

-- A week moves Draft -> Published; ranges are withdrawn, never edited or deleted.
GRANT UPDATE (status, published_at, published_by_user_id) ON scheduling.availability_weeks TO clinic_app;
GRANT UPDATE (withdrawn_at) ON scheduling.availability_ranges TO clinic_app;

-- Start, duration, Doctor, patient, reason, kind and the reschedule link are frozen: a change is a reschedule (new row).
GRANT UPDATE (status, status_changed_at, status_changed_by_user_id, status_change_reason, visit_ended_at)
    ON scheduling.appointments TO clinic_app;

-- An arrival is an event; only voiding moves.
GRANT UPDATE (voided_at, voided_by_user_id) ON scheduling.appointment_arrivals TO clinic_app;

-- Worker (J1 reminders, J4 re-validation): ids, patient, Doctor, time, status and kind only; never reason or notes.
GRANT SELECT (id, patient_id, doctor_id, start_at, status, kind) ON scheduling.appointments TO clinic_worker;

GRANT SELECT ON scheduling.availability_weeks, scheduling.availability_ranges, scheduling.appointments,
    scheduling.appointment_arrivals TO clinic_backup;
