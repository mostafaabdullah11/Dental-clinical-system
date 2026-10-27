using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Configuration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Infrastructure;

internal sealed class FinanceSettingsStore(DbContext db) : IFinanceSettings
{
    public Task<LabPercentageRevisionInfo?> LabPercentageAtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Set<LabPercentageRevision>().AsNoTracking().Where(r => r.EffectiveFrom <= at).OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new LabPercentageRevisionInfo(r.Id, r.Percentage, r.EffectiveFrom, r.SetByUserId, r.Reason))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LabPercentageRevisionInfo>> LabPercentageHistoryAsync(CancellationToken cancellationToken) =>
        await db.Set<LabPercentageRevision>().AsNoTracking().OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new LabPercentageRevisionInfo(r.Id, r.Percentage, r.EffectiveFrom, r.SetByUserId, r.Reason))
            .ToListAsync(cancellationToken);

    public async Task<LabPercentageRevisionInfo> AppendLabPercentageAsync(decimal percentage, DateTimeOffset effectiveFrom, Guid? setByUserId,
        string? reason, CancellationToken cancellationToken)
    {
        var row = new LabPercentageRevision
        {
            Id = Guid.CreateVersion7(), Percentage = percentage, EffectiveFrom = effectiveFrom, SetByUserId = setByUserId, Reason = reason,
            CreatedAt = effectiveFrom,
        };
        db.Set<LabPercentageRevision>().Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new LabPercentageRevisionInfo(row.Id, row.Percentage, row.EffectiveFrom, row.SetByUserId, row.Reason);
    }

    public Task<bool> IsActivePaymentMethodAsync(string code, CancellationToken cancellationToken) =>
        db.Set<PaymentMethod>().AnyAsync(m => m.Code == code && m.IsActive, cancellationToken);
}
