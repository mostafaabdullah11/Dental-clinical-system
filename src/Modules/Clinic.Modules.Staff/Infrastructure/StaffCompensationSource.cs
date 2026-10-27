using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Infrastructure;

internal sealed class StaffCompensationSource(DbContext db) : IStaffCompensationSource
{
    public async Task<IReadOnlyList<SalesEmployeeCompensation>> SalesEmployeesAsync(CancellationToken cancellationToken) =>
        await db.Set<SalesEmployee>().AsNoTracking().OrderBy(s => s.FullName)
            .Select(s => new SalesEmployeeCompensation(s.Id, s.FullName, s.BaseMonthlySalary, s.IsActive, s.CreatedAt, s.DeactivatedAt))
            .ToListAsync(cancellationToken);

    public Task<SalesEmployeeCompensation?> SalesEmployeeAsync(Guid salesEmployeeId, CancellationToken cancellationToken) =>
        db.Set<SalesEmployee>().AsNoTracking().Where(s => s.Id == salesEmployeeId)
            .Select(s => new SalesEmployeeCompensation(s.Id, s.FullName, s.BaseMonthlySalary, s.IsActive, s.CreatedAt, s.DeactivatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CommissionRateRevision?> CommissionRateAtAsync(Guid salesEmployeeId, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Set<SalesCommissionRate>().AsNoTracking().Where(r => r.SalesEmployeeId == salesEmployeeId && r.EffectiveFrom <= at)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new CommissionRateRevision(r.Id, r.SalesEmployeeId, r.RevisionNumber, r.CommissionPercentage, r.EffectiveFrom))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CommissionRateRevision>> CommissionHistoryAsync(Guid salesEmployeeId, CancellationToken cancellationToken) =>
        await db.Set<SalesCommissionRate>().AsNoTracking().Where(r => r.SalesEmployeeId == salesEmployeeId).OrderBy(r => r.RevisionNumber)
            .Select(r => new CommissionRateRevision(r.Id, r.SalesEmployeeId, r.RevisionNumber, r.CommissionPercentage, r.EffectiveFrom))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssistantCompensation>> AssistantsAsync(CancellationToken cancellationToken) =>
        await db.Set<Assistant>().AsNoTracking().OrderBy(a => a.FullName)
            .Select(a => new AssistantCompensation(a.Id, a.FullName, a.CurrentSalary, a.OvertimeHourlyRate, a.IsActive)).ToListAsync(cancellationToken);

    public Task<AssistantCompensation?> AssistantAsync(Guid assistantId, CancellationToken cancellationToken) =>
        db.Set<Assistant>().AsNoTracking().Where(a => a.Id == assistantId)
            .Select(a => new AssistantCompensation(a.Id, a.FullName, a.CurrentSalary, a.OvertimeHourlyRate, a.IsActive)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<OvertimeEntryInfo>> OvertimeEntriesAsync(Guid assistantId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Set<AssistantOvertimeEntry>().AsNoTracking()
            .Where(o => o.AssistantId == assistantId && o.VoidedAt == null && o.WorkDate >= from && o.WorkDate <= to).OrderBy(o => o.WorkDate)
            .Select(o => new OvertimeEntryInfo(o.Id, o.AssistantId, o.WorkDate, o.ApprovedHours, o.HourlyRateSnapshot, o.Amount))
            .ToListAsync(cancellationToken);
}
