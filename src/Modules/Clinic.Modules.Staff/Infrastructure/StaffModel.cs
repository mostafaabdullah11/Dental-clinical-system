using Clinic.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Infrastructure;

internal static class StaffModel
{
    public const string Schema = "staff";

    public static void Configure(ModelBuilder model)
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
    }
}
