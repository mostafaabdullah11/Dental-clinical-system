using Clinic.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Infrastructure;

internal static class StaffModel
{
    public const string Schema = "staff";

    public static void Configure(ModelBuilder model, bool task4)
    {
        model.Entity<Doctor>(e =>
        {
            e.ToTable("doctors", Schema, t => t.HasCheckConstraint(
                "ck_doctors_current_percentage", "current_percentage >= 0 AND current_percentage <= 100"));
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Specialty).HasMaxLength(200);
            e.Property(x => x.CurrentPercentage).HasPrecision(5, 2);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.IsActive);
        });

        model.Entity<SalesEmployee>(e =>
        {
            e.ToTable("sales_employees", Schema, t =>
            {
                t.HasCheckConstraint("ck_sales_employees_active_or_deactivated", "is_active OR deactivated_at IS NOT NULL");
                t.HasCheckConstraint("ck_sales_employees_base_monthly_salary", "base_monthly_salary >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.BaseMonthlySalary).HasPrecision(12, 2);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.IsActive);
        });

        model.Entity<SalesCommissionRate>(e =>
        {
            e.ToTable("sales_commission_rate_history", Schema, t =>
            {
                t.HasCheckConstraint("ck_sales_commission_rate_history_revision_number", "revision_number >= 1");
                t.HasCheckConstraint("ck_sales_commission_rate_history_percentage",
                    "commission_percentage >= 0 AND commission_percentage <= 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.CommissionPercentage).HasPrecision(5, 2);
            e.Property(x => x.EffectiveFrom).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.SalesEmployeeId, x.RevisionNumber }).IsUnique();
            e.HasIndex(x => new { x.SalesEmployeeId, x.EffectiveFrom }).IsUnique();
            e.HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.SalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<SalesEmployeeActivity>(e =>
        {
            e.ToView("v_sales_employee_activity", Schema);
            e.HasNoKey();
        });

        model.Entity<Assistant>(e =>
        {
            e.ToTable("assistants", Schema, t =>
            {
                t.HasCheckConstraint("ck_assistants_current_salary", "current_salary > 0");
                t.HasCheckConstraint("ck_assistants_overtime_hourly_rate", "overtime_hourly_rate IS NULL OR overtime_hourly_rate >= 0");
                t.HasCheckConstraint("ck_assistants_active_or_deactivated", "is_active OR deactivated_at IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.CurrentSalary).HasPrecision(12, 2);
            e.Property(x => x.OvertimeHourlyRate).HasPrecision(12, 2);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => x.IsActive);
        });

        if (!task4)
        {
            return;
        }

        model.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("attendance_records", Schema, t =>
            {
                t.HasCheckConstraint("ck_attendance_records_one_person", "(assistant_id IS NULL) <> (sales_employee_id IS NULL)");
                t.HasCheckConstraint("ck_attendance_records_status", "status IN ('Present', 'Late', 'Absent')");
                t.HasCheckConstraint("ck_attendance_records_times",
                    "check_out_time IS NULL OR check_in_time IS NULL OR check_out_time > check_in_time");
                t.HasCheckConstraint("ck_attendance_records_absent_no_times",
                    "status <> 'Absent' OR (check_in_time IS NULL AND check_out_time IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasOne<Assistant>().WithMany().HasForeignKey(x => x.AssistantId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<SalesEmployee>().WithMany().HasForeignKey(x => x.SalesEmployeeId).OnDelete(DeleteBehavior.NoAction);
            // Declared parent key for the overtime composite FK (a Sales row, assistant_id NULL, can never be referenced).
            e.HasIndex(x => new { x.Id, x.AssistantId, x.AttendanceDate }).IsUnique().HasDatabaseName("ux_attendance_records_id_assistant_date");
            e.HasIndex(x => new { x.AssistantId, x.AttendanceDate }).IsUnique().HasFilter("assistant_id IS NOT NULL")
                .HasDatabaseName("ix_attendance_records_assistant_day");
            e.HasIndex(x => new { x.SalesEmployeeId, x.AttendanceDate }).IsUnique().HasFilter("sales_employee_id IS NOT NULL")
                .HasDatabaseName("ix_attendance_records_sales_day");
            e.HasIndex(x => x.AttendanceDate);
        });

        model.Entity<AssistantOvertimeEntry>(e =>
        {
            e.ToTable("assistant_overtime_entries", Schema, t =>
            {
                t.HasCheckConstraint("ck_assistant_overtime_entries_hours", "approved_hours > 0 AND approved_hours <= 24");
                t.HasCheckConstraint("ck_assistant_overtime_entries_rate", "hourly_rate_snapshot >= 0");
                t.HasCheckConstraint("ck_assistant_overtime_entries_amount", "amount = round(approved_hours * hourly_rate_snapshot, 2)");
                t.HasCheckConstraint("ck_assistant_overtime_entries_void",
                    "(voided_at IS NULL AND voided_by_user_id IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by_user_id IS NOT NULL AND void_reason IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.ApprovedHours).HasPrecision(5, 2);
            e.Property(x => x.HourlyRateSnapshot).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.HasOne<Assistant>().WithMany().HasForeignKey(x => x.AssistantId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.AssistantId, x.WorkDate }).IsUnique().HasFilter("voided_at IS NULL")
                .HasDatabaseName("ix_assistant_overtime_entries_live_day");
            e.HasIndex(x => new { x.AssistantId, x.WorkDate });
        });
    }
}
