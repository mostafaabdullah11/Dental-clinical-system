using Clinic.Modules.Scheduling.Contracts;
using Clinic.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Scheduling.Infrastructure;

internal static class SchedulingModel
{
    public const string Schema = "scheduling";

    private static string In(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    public static void Configure(ModelBuilder model)
    {
        model.Entity<AvailabilityWeek>(e =>
        {
            e.ToTable("availability_weeks", Schema, t =>
                t.HasCheckConstraint("ck_availability_weeks_status", $"status IN ('{AvailabilityWeekStatuses.Draft}', '{AvailabilityWeekStatuses.Published}')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.DoctorId, x.WeekStartDate }).IsUnique();
        });

        model.Entity<AvailabilityRange>(e =>
        {
            e.ToTable("availability_ranges", Schema, t => t.HasCheckConstraint("ck_availability_ranges_order", "end_time > start_time"));
            e.HasKey(x => x.Id);
            e.HasOne<AvailabilityWeek>().WithMany().HasForeignKey(x => x.WeekId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.DoctorId, x.DayDate });
        });

        model.Entity<Appointment>(e =>
        {
            e.ToTable("appointments", Schema, t =>
            {
                t.HasCheckConstraint("ck_appointments_status", $"status IN ({In(AppointmentStatuses.All)})");
                t.HasCheckConstraint("ck_appointments_kind", $"kind IN ({In(AppointmentKinds.All)})");
                t.HasCheckConstraint("ck_appointments_duration", "duration_minutes BETWEEN 1 AND 480");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.SalesNotes).HasMaxLength(1000);
            e.Property(x => x.StatusChangeReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.Ignore(x => x.NominalEndAt);
            e.Ignore(x => x.EffectiveEndAt);
            e.HasOne<Appointment>().WithOne().HasForeignKey<Appointment>(x => x.PreviousAppointmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PreviousAppointmentId).IsUnique().HasFilter("previous_appointment_id IS NOT NULL");
            e.HasIndex(x => new { x.DoctorId, x.StartAt });
            e.HasIndex(x => new { x.PatientId, x.StartAt }).IsDescending(false, true);
            e.HasIndex(x => x.StartAt);
            e.HasIndex(x => new { x.Status, x.StartAt });
        });

        model.Entity<AppointmentArrival>(e =>
        {
            e.ToTable("appointment_arrivals", Schema);
            e.HasKey(x => x.Id);
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("voided_at IS NULL").HasDatabaseName("ix_appointment_arrivals_live");
            e.HasIndex(x => x.ArrivedAt);
        });

        model.Entity<EffectiveBookedRange>(e =>
        {
            e.HasNoKey();
            e.ToView("v_effective_booked_ranges", Schema);
        });
    }
}
