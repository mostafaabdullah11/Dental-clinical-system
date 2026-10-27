using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Patients.Contracts.Contact;
using Clinic.Modules.Payroll.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Payroll.Infrastructure;

internal static class PayrollErrors
{
    public const string DoctorPeriodExists = "payroll.doctor_period_exists";
    public const string SalesPeriodExists = "payroll.sales_period_exists";
    public const string AssistantPeriodExists = "payroll.assistant_period_exists";
    public const string PeriodNotEnded = "payroll.period_not_ended";
    public const string NotDraft = "payroll.not_draft";
    public const string NotFinalized = "payroll.not_finalized";
    public const string EmployeeNotActiveInMonth = "payroll.employee_not_active_in_month";
}

/// <summary>A clinic-local calendar month and its half-open UTC range (FR-PAYROLL-008).</summary>
internal sealed record PayrollMonth(DateOnly Start, DateOnly End, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

internal sealed class PayrollCalendar(IClinicSettings settings, IClock clock)
{
    public async Task<PayrollMonth> MonthAsync(string? month, CancellationToken ct)
    {
        if (month is null || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var start))
        {
            throw AppException.Validation("month", FieldErrorCodes.Invalid);
        }

        return await MonthOfAsync(start, ct);
    }

    public async Task<PayrollMonth> MonthOfAsync(DateOnly start, CancellationToken ct)
    {
        var zone = await settings.GetClinicTimeZoneAsync(ct);
        return new PayrollMonth(start, start.AddMonths(1).AddDays(-1), Midnight(start, zone), Midnight(start.AddMonths(1), zone));
    }

    /// <summary>Finalization only after the month has ended in clinic-local time.</summary>
    public async Task RequireEndedAsync(DateOnly periodEnd, CancellationToken ct)
    {
        if (await settings.GetClinicTodayAsync(clock.UtcNow, ct) <= periodEnd)
        {
            throw AppException.Coded(422, PayrollErrors.PeriodNotEnded);
        }
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

internal static class PayrollMoney
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// FR-PAYROLL-009..011: Doctor payroll consumes recognized <c>doctor_earnings</c> of the month (each at most once, DB unique);
/// it never uses billed revenue and never (Revenue − Lab) × %. Pending fixed-lab entitlement is never included.
/// </summary>
internal sealed class DoctorPayrollService(DbContext db, IPayrollLedgerSource ledger, PayrollCalendar calendar, IAuditWriter audit, ICurrentActor actor,
    IClock clock)
{
    public async Task<DoctorPayroll> CreateDraftAsync(Guid doctorId, string month, CancellationToken ct)
    {
        var period = await calendar.MonthAsync(month, ct);
        if (await db.Set<DoctorPayroll>().AnyAsync(p => p.DoctorId == doctorId && p.PeriodStart == period.Start, ct))
        {
            throw AppException.Coded(409, PayrollErrors.DoctorPeriodExists);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = new DoctorPayroll
        {
            Id = Guid.CreateVersion7(), DoctorId = doctorId, PeriodStart = period.Start, PeriodEnd = period.End, Status = PayrollStatuses.Draft, CreatedAt = clock.UtcNow,
        };
        db.Set<DoctorPayroll>().Add(payroll);
        await SyncAsync(payroll, period, ct);
        audit.Add(new AuditEvent("payroll.doctor.draft_created", "payroll.doctor_payrolls", payroll.Id.ToString(),
            NewValue: new { doctorId, month, payroll.DoctorShareTotal }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_doctor_payrolls_doctor_period"))
        {
            throw AppException.Coded(409, PayrollErrors.DoctorPeriodExists);
        }

        await transaction.CommitAsync(ct);
        return payroll;
    }

    public async Task AddAdjustmentAsync(Guid payrollId, decimal amount, string reason, CancellationToken ct)
    {
        var payroll = await DraftAsync(payrollId, ct);
        db.Set<DoctorPayrollAdjustment>().Add(new DoctorPayrollAdjustment
        {
            Id = Guid.CreateVersion7(), DoctorPayrollId = payrollId, Amount = amount, Reason = reason, CreatedByUserId = actor.UserId!.Value, CreatedAt = clock.UtcNow,
        });
        payroll.AdjustmentsTotal += amount;
        payroll.FinalAmount = payroll.DoctorShareTotal + payroll.AdjustmentsTotal;
        audit.Add(new AuditEvent("payroll.doctor.adjustment_added", "payroll.doctor_payroll_adjustments", payrollId.ToString(), NewValue: new { amount }, Reason: reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task FinalizeAsync(Guid payrollId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = await DraftAsync(payrollId, ct);
        await calendar.RequireEndedAsync(payroll.PeriodEnd, ct);
        await SyncAsync(payroll, await calendar.MonthOfAsync(payroll.PeriodStart, ct), ct);
        payroll.Status = PayrollStatuses.Finalized;
        payroll.FinalizedAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.doctor.finalized", "payroll.doctor_payrolls", payrollId.ToString(),
            NewValue: new { payroll.DoctorShareTotal, payroll.AdjustmentsTotal, payroll.FinalAmount }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task MarkPaidAsync(Guid payrollId, CancellationToken ct)
    {
        var payroll = await db.Set<DoctorPayroll>().SingleOrDefaultAsync(p => p.Id == payrollId, ct) ?? throw AppException.NotFound();
        if (payroll.Status == PayrollStatuses.Paid)
        {
            return;
        }

        if (payroll.Status != PayrollStatuses.Finalized)
        {
            throw AppException.Coded(422, PayrollErrors.NotFinalized);
        }

        payroll.Status = PayrollStatuses.Paid;
        payroll.PaidAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.doctor.paid", "payroll.doctor_payrolls", payrollId.ToString()));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Attaches the month's not-yet-paid earnings and recomputes the Draft totals (informational revenue / lab / clinic).</summary>
    private async Task SyncAsync(DoctorPayroll payroll, PayrollMonth period, CancellationToken ct)
    {
        var earnings = await ledger.EarningsAsync(payroll.DoctorId, period.FromUtc, period.ToUtc, ct);
        var ids = earnings.Select(e => e.EarningId).ToList();
        var taken = await db.Set<DoctorPayrollItem>().AsNoTracking().Where(i => ids.Contains(i.DoctorEarningId)).Select(i => i.DoctorEarningId).ToListAsync(ct);
        foreach (var earning in earnings.Where(e => !taken.Contains(e.EarningId)))
        {
            db.Set<DoctorPayrollItem>().Add(new DoctorPayrollItem { Id = Guid.CreateVersion7(), DoctorPayrollId = payroll.Id, DoctorEarningId = earning.EarningId, Amount = earning.Amount });
        }

        var mine = earnings.Where(e => !taken.Contains(e.EarningId)).Select(e => e.EarningId)
            .Concat(await db.Set<DoctorPayrollItem>().AsNoTracking().Where(i => i.DoctorPayrollId == payroll.Id).Select(i => i.DoctorEarningId).ToListAsync(ct))
            .Distinct().ToList();
        var totals = await ledger.TotalsForEarningsAsync(mine, ct);
        payroll.DoctorShareTotal = earnings.Where(e => mine.Contains(e.EarningId)).Sum(e => e.Amount);
        payroll.RevenueTotal = totals.Revenue;
        payroll.LabTotal = totals.Lab;
        payroll.ClinicShareTotal = totals.ClinicShare;
        payroll.AdjustmentsTotal = await db.Set<DoctorPayrollAdjustment>().AsNoTracking().Where(a => a.DoctorPayrollId == payroll.Id).SumAsync(a => a.Amount, ct);
        payroll.FinalAmount = payroll.DoctorShareTotal + payroll.AdjustmentsTotal;
    }

    private async Task<DoctorPayroll> DraftAsync(Guid id, CancellationToken ct)
    {
        var payroll = await db.Set<DoctorPayroll>().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        return payroll.Status == PayrollStatuses.Draft ? payroll : throw AppException.Coded(422, PayrollErrors.NotDraft);
    }
}

/// <summary>
/// FR-PAYROLL-001..007 / -012: Net = Base + Commission + Bonus − Deductions. One commission item per net allocation of the
/// employee's Lead-sourced patients in the month, at the rate in force at the collection (snapshotted); Walk-In → none;
/// allocations at or after the employee's deactivation → none (never transferred); a reversal uses the original's rate.
/// </summary>
internal sealed class SalesPayrollService(
    DbContext db, IPayrollLedgerSource ledger, IStaffCompensationSource staff, IPatientAttributionSource attribution, PayrollCalendar calendar, IAuditWriter audit,
    ICurrentActor actor, IClock clock)
{
    public async Task<SalesPayroll> CreateDraftAsync(Guid employeeId, string month, CancellationToken ct)
    {
        var period = await calendar.MonthAsync(month, ct);
        var employee = await staff.SalesEmployeeAsync(employeeId, ct) ?? throw AppException.Rule("salesEmployeeId", FieldErrorCodes.Invalid);
        if (employee.CreatedAt >= period.ToUtc || employee.DeactivatedAt is { } d && d < period.FromUtc)
        {
            throw AppException.Coded(422, PayrollErrors.EmployeeNotActiveInMonth);
        }

        if (await db.Set<SalesPayroll>().AnyAsync(p => p.SalesEmployeeId == employeeId && p.PeriodStart == period.Start, ct))
        {
            throw AppException.Coded(409, PayrollErrors.SalesPeriodExists);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = new SalesPayroll
        {
            Id = Guid.CreateVersion7(), SalesEmployeeId = employeeId, PeriodStart = period.Start, PeriodEnd = period.End, BaseSalarySnapshot = employee.BaseMonthlySalary,
            Status = PayrollStatuses.Draft, CreatedAt = clock.UtcNow,
        };
        db.Set<SalesPayroll>().Add(payroll);
        await db.SaveChangesAsync(ct);
        await SyncAsync(payroll, employee, period, ct);
        audit.Add(new AuditEvent("payroll.sales.draft_created", "payroll.sales_payrolls", payroll.Id.ToString(),
            NewValue: new { employeeId, month, payroll.BaseSalarySnapshot, payroll.CommissionTotal }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return payroll;
    }

    public async Task AddAdjustmentAsync(Guid payrollId, string kind, decimal amount, string reason, CancellationToken ct)
    {
        var payroll = await DraftAsync(payrollId, ct);
        db.Set<SalesPayrollAdjustment>().Add(new SalesPayrollAdjustment
        {
            Id = Guid.CreateVersion7(), SalesPayrollId = payrollId, Kind = kind, Amount = amount, Reason = reason, CreatedByUserId = actor.UserId!.Value, CreatedAt = clock.UtcNow,
        });
        if (kind == SalesAdjustmentKinds.Bonus)
        {
            payroll.BonusTotal += amount;
        }
        else
        {
            payroll.DeductionsTotal += amount;
        }

        audit.Add(new AuditEvent("payroll.sales.adjustment_added", "payroll.sales_payroll_adjustments", payrollId.ToString(), NewValue: new { kind, amount }, Reason: reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task FinalizeAsync(Guid payrollId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = await DraftAsync(payrollId, ct);
        await calendar.RequireEndedAsync(payroll.PeriodEnd, ct);
        var employee = (await staff.SalesEmployeeAsync(payroll.SalesEmployeeId, ct))!;
        await SyncAsync(payroll, employee, await calendar.MonthOfAsync(payroll.PeriodStart, ct), ct);
        payroll.Status = PayrollStatuses.Finalized;
        payroll.FinalizedAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.sales.finalized", "payroll.sales_payrolls", payrollId.ToString(),
            NewValue: new { payroll.BaseSalarySnapshot, payroll.CommissionTotal, payroll.BonusTotal, payroll.DeductionsTotal }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task MarkPaidAsync(Guid payrollId, CancellationToken ct)
    {
        var payroll = await db.Set<SalesPayroll>().SingleOrDefaultAsync(p => p.Id == payrollId, ct) ?? throw AppException.NotFound();
        if (payroll.Status == PayrollStatuses.Paid)
        {
            return;
        }

        if (payroll.Status != PayrollStatuses.Finalized)
        {
            throw AppException.Coded(422, PayrollErrors.NotFinalized);
        }

        payroll.Status = PayrollStatuses.Paid;
        payroll.PaidAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.sales.paid", "payroll.sales_payrolls", payrollId.ToString()));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Commission lines the employee earns in a month, computed exactly as the payroll item rule (also used by reports).</summary>
    public async Task<List<SalesPayrollCommissionItem>> CommissionLinesAsync(Guid payrollId, SalesEmployeeCompensation employee, PayrollMonth period,
        CancellationToken ct)
    {
        var allocations = await ledger.AllocationsAsync(period.FromUtc, period.ToUtc, ct);
        var attributions = await attribution.AttributionsAsync(allocations.Select(a => a.PatientId), ct);
        var lines = new List<SalesPayrollCommissionItem>();
        foreach (var a in allocations)
        {
            if (attributions.GetValueOrDefault(a.PatientId) is not { OriginalSalesEmployeeId: { } owner } || owner != employee.Id)
            {
                continue; // Walk-In / Direct (no Original Sales) or another employee's patient: no item (FR-PAYROLL-004)
            }

            if (employee.DeactivatedAt is { } deactivated && a.AllocatedAt >= deactivated)
            {
                continue; // inactive Sales receives no new commission; nothing is redirected (FR-PAYROLL-005)
            }

            var rateAt = a.ReversedAllocationAllocatedAt ?? a.AllocatedAt; // a reversal uses the original collection's rate (FR-PAYROLL-006)
            var rate = await staff.CommissionRateAtAsync(employee.Id, rateAt, ct);
            if (rate is null)
            {
                continue;
            }

            lines.Add(new SalesPayrollCommissionItem
            {
                Id = Guid.CreateVersion7(), SalesPayrollId = payrollId, PatientId = a.PatientId, PaymentAllocationId = a.AllocationId, RateRevisionId = rate.RevisionId,
                BaseAmount = a.Amount, CommissionPercentageApplied = rate.Percentage, CommissionAmount = PayrollMoney.Round(a.Amount * rate.Percentage / 100m),
                AllocatedAt = a.AllocatedAt, BasisDescription = a.ReversesAllocationId is null ? "Collection" : "Reversal",
            });
        }

        return lines;
    }

    private async Task SyncAsync(SalesPayroll payroll, SalesEmployeeCompensation employee, PayrollMonth period, CancellationToken ct)
    {
        var lines = await CommissionLinesAsync(payroll.Id, employee, period, ct);
        var ids = lines.Select(l => l.PaymentAllocationId).ToList();
        var taken = await db.Set<SalesPayrollCommissionItem>().AsNoTracking().Where(i => ids.Contains(i.PaymentAllocationId)).Select(i => i.PaymentAllocationId)
            .ToListAsync(ct);
        db.Set<SalesPayrollCommissionItem>().AddRange(lines.Where(l => !taken.Contains(l.PaymentAllocationId)));
        await db.SaveChangesAsync(ct);
        payroll.CommissionTotal = await db.Set<SalesPayrollCommissionItem>().AsNoTracking().Where(i => i.SalesPayrollId == payroll.Id).SumAsync(i => i.CommissionAmount, ct);
        var adjustments = await db.Set<SalesPayrollAdjustment>().AsNoTracking().Where(a => a.SalesPayrollId == payroll.Id).ToListAsync(ct);
        payroll.BonusTotal = adjustments.Where(a => a.Kind == SalesAdjustmentKinds.Bonus).Sum(a => a.Amount);
        payroll.DeductionsTotal = adjustments.Where(a => a.Kind == SalesAdjustmentKinds.Deduction).Sum(a => a.Amount);
    }

    private async Task<SalesPayroll> DraftAsync(Guid id, CancellationToken ct)
    {
        var payroll = await db.Set<SalesPayroll>().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        return payroll.Status == PayrollStatuses.Draft ? payroll : throw AppException.Coded(422, PayrollErrors.NotDraft);
    }
}

/// <summary>
/// FR-PAYROLL-014..016: Assistant payroll = Base Pay + Overtime. Base Pay defaults to the salary snapshot; for a partial month
/// the Manager enters it with a reason code (never prorated). Overtime = Σ the month's approved entries (each in one payroll).
/// </summary>
internal sealed class AssistantPayrollService(DbContext db, IStaffCompensationSource staff, PayrollCalendar calendar, IAuditWriter audit, ICurrentActor actor,
    IClock clock)
{
    public async Task<AssistantPayroll> CreateDraftAsync(Guid assistantId, string month, CancellationToken ct)
    {
        var period = await calendar.MonthAsync(month, ct);
        var assistant = await staff.AssistantAsync(assistantId, ct) ?? throw AppException.Rule("assistantId", FieldErrorCodes.Invalid);
        if (await db.Set<AssistantPayroll>().AnyAsync(p => p.AssistantId == assistantId && p.PeriodStart == period.Start, ct))
        {
            throw AppException.Coded(409, PayrollErrors.AssistantPeriodExists);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = new AssistantPayroll
        {
            Id = Guid.CreateVersion7(), AssistantId = assistantId, PeriodStart = period.Start, PeriodEnd = period.End, SalarySnapshot = assistant.CurrentSalary,
            BasePayAmount = assistant.CurrentSalary, BasePaySource = BasePaySources.SalarySnapshot, FinalAmount = assistant.CurrentSalary,
            Status = PayrollStatuses.Draft, CreatedAt = clock.UtcNow,
        };
        db.Set<AssistantPayroll>().Add(payroll);
        await db.SaveChangesAsync(ct);
        await SyncAsync(payroll, ct);
        audit.Add(new AuditEvent("payroll.assistant.draft_created", "payroll.assistant_payrolls", payroll.Id.ToString(),
            NewValue: new { assistantId, month, payroll.SalarySnapshot, payroll.OvertimeAmount }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_assistant_payrolls_assistant_period"))
        {
            throw AppException.Coded(409, PayrollErrors.AssistantPeriodExists);
        }

        await transaction.CommitAsync(ct);
        return payroll;
    }

    public async Task SetBasePayAsync(Guid payrollId, decimal amount, string reason, CancellationToken ct)
    {
        var payroll = await DraftAsync(payrollId, ct);
        var previous = new { payroll.BasePayAmount, payroll.BasePaySource, payroll.PartialPeriodReason };
        payroll.BasePayAmount = amount;
        payroll.BasePaySource = BasePaySources.ManagerManual;
        payroll.PartialPeriodReason = reason;
        payroll.BasePayEnteredByUserId = actor.UserId;
        payroll.FinalAmount = payroll.BasePayAmount + payroll.OvertimeAmount;
        audit.Add(new AuditEvent("payroll.assistant.base_pay_entered", "payroll.assistant_payrolls", payrollId.ToString(), PreviousValue: previous,
            NewValue: new { basePayAmount = amount, partialPeriodReason = reason }, Reason: reason));
        await db.SaveChangesAsync(ct);
    }

    public async Task FinalizeAsync(Guid payrollId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payroll = await DraftAsync(payrollId, ct);
        await calendar.RequireEndedAsync(payroll.PeriodEnd, ct);
        await SyncAsync(payroll, ct);
        payroll.Status = PayrollStatuses.Finalized;
        payroll.FinalizedAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.assistant.finalized", "payroll.assistant_payrolls", payrollId.ToString(),
            NewValue: new { payroll.BasePayAmount, payroll.OvertimeAmount, payroll.FinalAmount }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task MarkPaidAsync(Guid payrollId, CancellationToken ct)
    {
        var payroll = await db.Set<AssistantPayroll>().SingleOrDefaultAsync(p => p.Id == payrollId, ct) ?? throw AppException.NotFound();
        if (payroll.Status == PayrollStatuses.Paid)
        {
            return;
        }

        if (payroll.Status != PayrollStatuses.Finalized)
        {
            throw AppException.Coded(422, PayrollErrors.NotFinalized);
        }

        payroll.Status = PayrollStatuses.Paid;
        payroll.PaidAt = clock.UtcNow;
        audit.Add(new AuditEvent("payroll.assistant.paid", "payroll.assistant_payrolls", payrollId.ToString()));
        await db.SaveChangesAsync(ct);
    }

    private async Task SyncAsync(AssistantPayroll payroll, CancellationToken ct)
    {
        var entries = await staff.OvertimeEntriesAsync(payroll.AssistantId, payroll.PeriodStart, payroll.PeriodEnd, ct);
        var ids = entries.Select(e => e.Id).ToList();
        var taken = await db.Set<AssistantPayrollOvertimeItem>().AsNoTracking().Where(i => ids.Contains(i.OvertimeEntryId)).Select(i => i.OvertimeEntryId).ToListAsync(ct);
        foreach (var entry in entries.Where(e => !taken.Contains(e.Id)))
        {
            db.Set<AssistantPayrollOvertimeItem>().Add(new AssistantPayrollOvertimeItem
            {
                Id = Guid.CreateVersion7(), AssistantPayrollId = payroll.Id, OvertimeEntryId = entry.Id, ApprovedHours = entry.ApprovedHours,
                HourlyRateSnapshot = entry.HourlyRateSnapshot, Amount = entry.Amount,
            });
        }

        await db.SaveChangesAsync(ct);
        payroll.OvertimeAmount = await db.Set<AssistantPayrollOvertimeItem>().AsNoTracking().Where(i => i.AssistantPayrollId == payroll.Id).SumAsync(i => i.Amount, ct);
        payroll.FinalAmount = payroll.BasePayAmount + payroll.OvertimeAmount;
    }

    private async Task<AssistantPayroll> DraftAsync(Guid id, CancellationToken ct)
    {
        var payroll = await db.Set<AssistantPayroll>().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        return payroll.Status == PayrollStatuses.Draft ? payroll : throw AppException.Coded(422, PayrollErrors.NotDraft);
    }
}

internal sealed class OvertimeUsageCheck(DbContext db) : IOvertimeUsageCheck
{
    public Task<bool> IsInPayrollAsync(Guid overtimeEntryId, CancellationToken cancellationToken) =>
        db.Set<AssistantPayrollOvertimeItem>().AnyAsync(i => i.OvertimeEntryId == overtimeEntryId, cancellationToken);
}
