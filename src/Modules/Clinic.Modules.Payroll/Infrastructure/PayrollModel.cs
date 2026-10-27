using Clinic.Modules.Payroll.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Payroll.Infrastructure;

/// <summary>Schema <c>payroll</c> (DATABASE_DESIGN.md §15). Cross-schema FKs (staff, finance, patients) live in CrossModuleReferences.</summary>
internal static class PayrollModel
{
    public const string Schema = "payroll";

    private const string CalendarMonth =
        "period_start = date_trunc('month', period_start)::date AND period_end = (period_start + interval '1 month - 1 day')::date";

    private const string StatusCheck =
        "status IN ('Draft', 'Finalized', 'Paid') AND (status = 'Draft') = (finalized_at IS NULL) AND (status = 'Paid') = (paid_at IS NOT NULL)";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<DoctorPayroll>(e =>
        {
            e.ToTable("doctor_payrolls", Schema, t =>
            {
                t.HasCheckConstraint("ck_doctor_payrolls_month", CalendarMonth);
                t.HasCheckConstraint("ck_doctor_payrolls_status", StatusCheck);
                t.HasCheckConstraint("ck_doctor_payrolls_final", "final_amount = doctor_share_total + adjustments_total");
            });
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.RevenueTotal));
            Money(e.Property(x => x.LabTotal));
            Money(e.Property(x => x.DoctorShareTotal));
            Money(e.Property(x => x.ClinicShareTotal));
            Money(e.Property(x => x.AdjustmentsTotal));
            Money(e.Property(x => x.FinalAmount));
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.DoctorId, x.PeriodStart }).IsUnique().HasDatabaseName("ix_doctor_payrolls_doctor_period");
            e.HasIndex(x => x.Status);
        });

        model.Entity<DoctorPayrollItem>(e =>
        {
            e.ToTable("doctor_payroll_items", Schema);
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.Amount));
            e.HasOne<DoctorPayroll>().WithMany().HasForeignKey(x => x.DoctorPayrollId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.DoctorEarningId).IsUnique().HasDatabaseName("ix_doctor_payroll_items_earning");
            e.HasIndex(x => x.DoctorPayrollId);
        });

        model.Entity<DoctorPayrollAdjustment>(e =>
        {
            e.ToTable("doctor_payroll_adjustments", Schema, t => t.HasCheckConstraint("ck_doctor_payroll_adjustments_amount", "amount <> 0"));
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.Amount));
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<DoctorPayroll>().WithMany().HasForeignKey(x => x.DoctorPayrollId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<SalesPayroll>(e =>
        {
            e.ToTable("sales_payrolls", Schema, t =>
            {
                t.HasCheckConstraint("ck_sales_payrolls_month", CalendarMonth);
                t.HasCheckConstraint("ck_sales_payrolls_status", StatusCheck);
                t.HasCheckConstraint("ck_sales_payrolls_amounts", "base_salary_snapshot >= 0 AND bonus_total >= 0 AND deductions_total >= 0");
            });
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.BaseSalarySnapshot));
            Money(e.Property(x => x.CommissionTotal));
            Money(e.Property(x => x.BonusTotal));
            Money(e.Property(x => x.DeductionsTotal));
            Money(e.Property(x => x.NetSalary)).HasComputedColumnSql("base_salary_snapshot + commission_total + bonus_total - deductions_total", stored: true);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.SalesEmployeeId, x.PeriodStart }).IsUnique().HasDatabaseName("ix_sales_payrolls_employee_period");
            e.HasIndex(x => x.Status);
        });

        model.Entity<SalesPayrollCommissionItem>(e =>
        {
            e.ToTable("sales_payroll_commission_items", Schema, t =>
            {
                t.HasCheckConstraint("ck_sales_payroll_commission_items_pct", "commission_percentage_applied BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_sales_payroll_commission_items_amount",
                    "commission_amount = round(base_amount * commission_percentage_applied / 100, 2)");
            });
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.BaseAmount));
            Money(e.Property(x => x.CommissionAmount));
            e.Property(x => x.CommissionPercentageApplied).HasPrecision(5, 2);
            e.Property(x => x.BasisDescription).HasMaxLength(200);
            e.HasOne<SalesPayroll>().WithMany().HasForeignKey(x => x.SalesPayrollId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.PaymentAllocationId).IsUnique().HasDatabaseName("ix_sales_payroll_commission_items_allocation");
            e.HasIndex(x => x.SalesPayrollId);
        });

        model.Entity<SalesPayrollAdjustment>(e =>
        {
            e.ToTable("sales_payroll_adjustments", Schema, t =>
            {
                t.HasCheckConstraint("ck_sales_payroll_adjustments_kind", "kind IN ('Bonus', 'Deduction')");
                t.HasCheckConstraint("ck_sales_payroll_adjustments_amount", "amount > 0");
            });
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.Amount));
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<SalesPayroll>().WithMany().HasForeignKey(x => x.SalesPayrollId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<AssistantPayroll>(e =>
        {
            e.ToTable("assistant_payrolls", Schema, t =>
            {
                t.HasCheckConstraint("ck_assistant_payrolls_month", CalendarMonth);
                t.HasCheckConstraint("ck_assistant_payrolls_status", StatusCheck);
                t.HasCheckConstraint("ck_assistant_payrolls_amounts", "salary_snapshot > 0 AND base_pay_amount >= 0 AND overtime_amount >= 0");
                t.HasCheckConstraint("ck_assistant_payrolls_final", "final_amount = base_pay_amount + overtime_amount");
                t.HasCheckConstraint("ck_assistant_payrolls_base_source",
                    "(base_pay_source = 'SalarySnapshot' AND base_pay_amount = salary_snapshot AND partial_period_reason IS NULL) OR "
                    + "(base_pay_source = 'ManagerManual' AND partial_period_reason IS NOT NULL AND base_pay_entered_by_user_id IS NOT NULL)");
                t.HasCheckConstraint("ck_assistant_payrolls_reason",
                    "partial_period_reason IS NULL OR partial_period_reason IN ('JoinedMidMonth', 'LeftMidMonth', 'SalaryChangedInMonth', 'OtherPartialMonth')");
            });
            e.HasKey(x => x.Id);
            Money(e.Property(x => x.SalarySnapshot));
            Money(e.Property(x => x.BasePayAmount));
            Money(e.Property(x => x.OvertimeAmount));
            Money(e.Property(x => x.FinalAmount));
            e.Property(x => x.BasePaySource).HasMaxLength(20);
            e.Property(x => x.PartialPeriodReason).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
            e.HasIndex(x => new { x.AssistantId, x.PeriodStart }).IsUnique().HasDatabaseName("ix_assistant_payrolls_assistant_period");
            e.HasIndex(x => x.Status);
        });

        model.Entity<AssistantPayrollOvertimeItem>(e =>
        {
            e.ToTable("assistant_payroll_overtime_items", Schema, t =>
                t.HasCheckConstraint("ck_assistant_payroll_overtime_items_values", "approved_hours > 0 AND hourly_rate_snapshot >= 0 AND amount >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.ApprovedHours).HasPrecision(5, 2);
            Money(e.Property(x => x.HourlyRateSnapshot));
            Money(e.Property(x => x.Amount));
            e.HasOne<AssistantPayroll>().WithMany().HasForeignKey(x => x.AssistantPayrollId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.OvertimeEntryId).IsUnique().HasDatabaseName("ix_assistant_payroll_overtime_items_entry");
            e.HasIndex(x => x.AssistantPayrollId);
        });
    }

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> Money(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> property) => property.HasPrecision(12, 2);
}
