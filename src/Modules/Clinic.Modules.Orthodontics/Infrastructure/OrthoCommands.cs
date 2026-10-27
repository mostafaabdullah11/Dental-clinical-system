using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Orthodontics.Domain;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Orthodontics.Infrastructure;

public sealed record InstallmentChange(Guid? InstallmentId, decimal? Amount, DateOnly? DueDate, bool? Retire);

public sealed record InstallmentAdd(DateOnly DueDate, decimal Amount);

public sealed record InstallmentApplication(Guid AllocationId, Guid InstallmentId, decimal Amount);

/// <summary>
/// FR-ORT-006 / -008..-018 commands. All run under the patient's money lock; the terms edit is the only route that changes an
/// established Gross / Deposit / Months, the batch the only route that edits the schedule, the application route the only
/// writer of applications. Paid history is never reduced, retired, re-dated or moved.
/// </summary>
internal sealed class OrthoCommands(
    DbContext db, OrthoStateLoader loader, IChargeLedger ledger, IOrthoChargeAdjustments adjustments, IClinicSettings settings, IAuditWriter audit,
    ICurrentActor actor, IClock clock)
{
    public async Task EditTermsAsync(Guid caseId, decimal? gross, decimal? deposit, int? months, string reason, string? ifMatch, CancellationToken ct)
    {
        var state = await loader.LoadAsync(caseId, ct) ?? throw AppException.NotFound();
        if (state.Case.ChargeId is not { } chargeId)
        {
            throw AppException.Coded(422, OrthoErrors.ChargeNotEstablished);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(state.Charge!.PatientId, ct);
        state = (await loader.LoadAsync(caseId, ct))!;
        RequireVersion(ifMatch, state);

        var newGross = gross ?? state.Terms.GrossTotalPrice;
        var newDeposit = deposit ?? state.Terms.DepositAmount;
        var newMonths = months ?? state.Terms.NumberOfMonths;
        if (newGross < state.Terms.GrossTotalPrice)
        {
            throw AppException.Coded(422, OrthoErrors.DecreaseNotSupported); // Q-ORT-3b: V1 rejects before any write
        }

        var delta = newGross - state.Terms.GrossTotalPrice;
        var effective = state.EffectiveTotal + delta;
        if (newDeposit < 0 || newDeposit > effective)
        {
            throw AppException.Coded(422, OrthoErrors.DepositExceedsEffective);
        }

        var paidRegular = state.Active.Count(i => i.IsRegular && i.Applied > 0);
        if (newMonths < 1 || newMonths < paidRegular)
        {
            throw AppException.Coded(422, OrthoErrors.MonthsConflict);
        }

        var now = clock.UtcNow;
        db.Set<OrthoTermRevision>().Add(new OrthoTermRevision
        {
            Id = Guid.CreateVersion7(), OrthoCaseId = caseId, RevisionNumber = state.Terms.RevisionNumber + 1, GrossTotalPrice = newGross, DepositAmount = newDeposit,
            NumberOfMonths = newMonths, EnteredByUserId = actor.UserId!.Value, EnteredByRole = Roles.Manager, EnteredAt = now, Reason = reason,
        });

        if (delta > 0)
        {
            // FR-ORT-009: +Δ adjustment and exactly one PriceChange installment of Δ; nothing else changes or is redistributed.
            await adjustments.RecordOrthoTermsChangeAsync(chargeId, delta, reason, ct);
            var latestDue = state.Active.Select(i => i.Current.DueDate).DefaultIfEmpty(await settings.GetClinicTodayAsync(state.Charge!.ChargedAt, ct)).Max();
            AddInstallment(caseId, state.Installments.Count + 1, InstallmentOrigins.PriceChange, delta, latestDue.AddMonths(1), RevisionSources.PriceChange, reason, now);
        }

        audit.Add(new AuditEvent("orthodontics.terms.edited", "orthodontics.ortho_term_revisions", caseId.ToString(),
            PreviousValue: new { gross = state.Terms.GrossTotalPrice, deposit = state.Terms.DepositAmount, months = state.Terms.NumberOfMonths },
            NewValue: new { gross = newGross, deposit = newDeposit, months = newMonths }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>FR-ORT-014..018: one atomic, audited batch; nothing is written unless the final schedule is valid.</summary>
    public async Task EditScheduleAsync(Guid caseId, string reason, IReadOnlyList<InstallmentChange> changes, IReadOnlyList<InstallmentAdd> adds, string? ifMatch,
        CancellationToken ct)
    {
        var state = await loader.LoadAsync(caseId, ct) ?? throw AppException.NotFound();
        if (state.Case.ChargeId is null)
        {
            throw AppException.Coded(422, OrthoErrors.ChargeNotEstablished);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(state.Charge!.PatientId, ct);
        state = (await loader.LoadAsync(caseId, ct))!;
        RequireVersion(ifMatch, state);
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, ct);
        var now = clock.UtcNow;
        var withHistory = (await db.Set<InstallmentPayment>().AsNoTracking().Where(p => p.OrthoCaseId == caseId).Select(p => p.InstallmentId).Distinct()
            .ToListAsync(ct)).ToHashSet();

        // Simulate the final schedule (amount, active, regular) to validate before writing anything.
        var final = state.Installments.ToDictionary(i => i.Installment.Id, i => (i.Current.AmountDue, i.Current.IsActive, i.IsRegular));
        foreach (var change in changes)
        {
            var current = state.Installments.SingleOrDefault(i => i.Installment.Id == change.InstallmentId)
                          ?? throw AppException.Rule("installmentId", FieldErrorCodes.Invalid);
            if (!current.Current.IsActive)
            {
                throw AppException.Coded(422, OrthoErrors.InstallmentNotActive); // a retired installment is never re-activated
            }

            var hasHistory = withHistory.Contains(current.Installment.Id);
            if (change.Retire == true)
            {
                if (hasHistory)
                {
                    throw AppException.Coded(422, OrthoErrors.HasPaymentHistory);
                }

                final[current.Installment.Id] = (0m, false, current.IsRegular);
                AddRevision(current, 0m, current.Current.DueDate, false, RevisionSources.ManagerRetire, reason, now);
                continue;
            }

            var amount = change.Amount ?? current.Current.AmountDue;
            if (amount < 0)
            {
                throw AppException.Rule("amount", FieldErrorCodes.OutOfRange);
            }

            if (amount < current.Applied)
            {
                throw AppException.Coded(422, OrthoErrors.BelowPaid);
            }

            var due = change.DueDate ?? current.Current.DueDate;
            if (due != current.Current.DueDate && hasHistory)
            {
                throw AppException.Coded(422, OrthoErrors.HasPaymentHistory);
            }

            final[current.Installment.Id] = (amount, true, current.IsRegular);
            AddRevision(current, amount, due, true, RevisionSources.ManagerEdit, reason, now);
        }

        var number = state.Installments.Count;
        foreach (var add in adds)
        {
            if (add.DueDate < today || add.Amount < 0)
            {
                throw AppException.Rule("dueDate", FieldErrorCodes.OutOfRange);
            }

            var id = AddInstallment(caseId, ++number, InstallmentOrigins.ManagerAdded, add.Amount, add.DueDate, RevisionSources.ManagerAdded, reason, now);
            final[id] = (add.Amount, true, true);
        }

        var active = final.Values.Where(v => v.IsActive).ToList();
        if (active.Count(v => v.IsRegular) != state.Terms.NumberOfMonths)
        {
            throw AppException.Coded(422, OrthoErrors.MonthPlanMismatch);
        }

        if (state.Terms.DepositAmount + active.Sum(v => v.AmountDue) != state.EffectiveTotal)
        {
            throw AppException.Coded(422, OrthoErrors.Unbalanced);
        }

        audit.Add(new AuditEvent("orthodontics.schedule.edited", "orthodontics.ortho_installment_revisions", caseId.ToString(),
            NewValue: new { changes = changes.Count, added = adds.Count }, Reason: reason));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>FR-ORT-006: manual application rows, capped by the allocation's remaining amount and the installment's amount (G5).</summary>
    public async Task ApplyAsync(Guid caseId, IReadOnlyList<InstallmentApplication> applications, CancellationToken ct)
    {
        var state = await loader.LoadAsync(caseId, ct) ?? throw AppException.NotFound();
        if (state.Case.ChargeId is not { } chargeId)
        {
            throw AppException.Coded(422, OrthoErrors.ChargeNotEstablished);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockPatientAsync(state.Charge!.PatientId, ct);
        state = (await loader.LoadAsync(caseId, ct))!;
        var byAllocation = new Dictionary<Guid, decimal>(state.AppliedByAllocation);
        var byInstallment = state.Installments.ToDictionary(i => i.Installment.Id, i => i.Applied);
        var now = clock.UtcNow;
        foreach (var application in applications)
        {
            var allocation = state.Allocations.SingleOrDefault(a => a.AllocationId == application.AllocationId && a.ReversesAllocationId == null)
                             ?? throw AppException.Rule("allocationId", FieldErrorCodes.Invalid);
            var installment = state.Installments.SingleOrDefault(i => i.Installment.Id == application.InstallmentId)
                              ?? throw AppException.Rule("installmentId", FieldErrorCodes.Invalid);
            if (!installment.Current.IsActive)
            {
                throw AppException.Coded(422, OrthoErrors.InstallmentNotActive);
            }

            if (application.Amount <= 0 || byAllocation.GetValueOrDefault(allocation.AllocationId) + application.Amount > allocation.EffectiveAmount)
            {
                throw AppException.Coded(422, OrthoErrors.ApplicationExceedsAllocation);
            }

            if (byInstallment[installment.Installment.Id] + application.Amount > installment.Current.AmountDue)
            {
                throw AppException.Coded(422, OrthoErrors.ApplicationExceedsInstallment);
            }

            byAllocation[allocation.AllocationId] = byAllocation.GetValueOrDefault(allocation.AllocationId) + application.Amount;
            byInstallment[installment.Installment.Id] += application.Amount;
            db.Set<InstallmentPayment>().Add(new InstallmentPayment
            {
                Id = Guid.CreateVersion7(), InstallmentId = installment.Installment.Id, OrthoCaseId = caseId, PaymentAllocationId = allocation.AllocationId,
                ChargeId = chargeId, AmountApplied = application.Amount, AppliedByUserId = actor.UserId!.Value, AppliedAt = now,
            });
        }

        audit.Add(new AuditEvent("orthodontics.installment_payments.applied", "orthodontics.installment_payments", caseId.ToString(),
            NewValue: applications.Select(a => new { a.AllocationId, a.InstallmentId, a.Amount })));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_installment_payments_installment_allocation"))
        {
            throw AppException.Coded(409, "ortho.application_exists");
        }

        await transaction.CommitAsync(ct);
    }

    private static void RequireVersion(string? ifMatch, OrthoState state)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw AppException.PreconditionRequired();
        }

        if (!string.Equals(ifMatch.Trim(), state.Version, StringComparison.Ordinal))
        {
            throw AppException.Stale();
        }
    }

    private void AddRevision(InstallmentState current, decimal amount, DateOnly due, bool active, string source, string reason, DateTimeOffset now) =>
        db.Set<OrthoInstallmentRevision>().Add(new OrthoInstallmentRevision
        {
            Id = Guid.CreateVersion7(), InstallmentId = current.Installment.Id, RevisionNumber = current.RevisionCount + 1, AmountDue = amount, DueDate = due,
            IsActive = active, Source = source, EnteredByUserId = actor.UserId!.Value, EnteredByRole = Roles.Manager, EnteredAt = now, Reason = reason,
        });

    private Guid AddInstallment(Guid caseId, int number, string origin, decimal amount, DateOnly due, string source, string reason, DateTimeOffset now)
    {
        var installment = new OrthoInstallment { Id = Guid.CreateVersion7(), OrthoCaseId = caseId, InstallmentNumber = number, Origin = origin, CreatedAt = now };
        db.Set<OrthoInstallment>().Add(installment);
        db.Set<OrthoInstallmentRevision>().Add(new OrthoInstallmentRevision
        {
            Id = Guid.CreateVersion7(), InstallmentId = installment.Id, RevisionNumber = 1, AmountDue = amount, DueDate = due, IsActive = true, Source = source,
            EnteredByUserId = actor.UserId!.Value, EnteredByRole = Roles.Manager, EnteredAt = now, Reason = reason,
        });
        return installment.Id;
    }
}

/// <summary>
/// J2 (API B6, FR-NOTIF-004): active installments due tomorrow (clinic-local) whose ortho charge has Outstanding &gt; 0, one
/// reminder per active Sales user; duplicates are skipped by the dedupe key; retired installments are never reminded.
/// </summary>
internal sealed class InstallmentReminderPlanner(
    DbContext db, IChargeLedger ledger, IClinicSettings settings, IActiveSalesEmployeeDirectory activeSales, INotificationQueue queue, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var tomorrow = (await settings.GetClinicTodayAsync(now, cancellationToken)).AddDays(1);
        var due = await DueTomorrowAsync(db, ledger, tomorrow, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var balances = await ledger.BalancesAsync(due.Select(d => d.ChargeId), cancellationToken);
        var recipients = await activeSales.GetActiveAsync(cancellationToken);
        var inserted = 0;
        foreach (var item in due.Where(d => balances.GetValueOrDefault(d.ChargeId)?.Outstanding > 0))
        {
            foreach (var recipient in recipients)
            {
                if (await queue.EnqueueAsync(new NotificationRequest(NotificationTypes.OrthodonticInstallmentDueTomorrow, recipient.UserId, Roles.SalesReception,
                        item.PatientId, NotificationEntityTypes.OrthoInstallment, item.InstallmentId, tomorrow, now), cancellationToken))
                {
                    inserted++;
                }
            }
        }

        return inserted;
    }

    internal sealed record DueInstallment(Guid InstallmentId, Guid ChargeId, Guid PatientId);

    internal static async Task<List<DueInstallment>> DueTomorrowAsync(DbContext db, IChargeLedger ledger, DateOnly tomorrow, CancellationToken ct)
    {
        var latest = from r in db.Set<OrthoInstallmentRevision>().AsNoTracking()
                     where r.RevisionNumber == db.Set<OrthoInstallmentRevision>().Where(x => x.InstallmentId == r.InstallmentId).Max(x => x.RevisionNumber)
                     select r;
        var rows = await (from r in latest
                          join i in db.Set<OrthoInstallment>().AsNoTracking() on r.InstallmentId equals i.Id
                          join c in db.Set<OrthoCase>().AsNoTracking() on i.OrthoCaseId equals c.TreatmentCaseId
                          where r.IsActive && r.DueDate == tomorrow && c.ChargeId != null
                          select new { i.Id, ChargeId = c.ChargeId!.Value }).ToListAsync(ct);
        var charges = await ledger.BalancesAsync(rows.Select(r => r.ChargeId), ct);
        var result = rows.Select(r => new DueInstallment(r.Id, r.ChargeId, charges[r.ChargeId].PatientId)).ToList();
        return result;
    }
}

/// <summary>J4 re-validation of an installment reminder: still active and its ortho charge still has Outstanding &gt; 0.</summary>
internal sealed class InstallmentNotificationValidator(DbContext db, IChargeLedger ledger) : INotificationSourceValidator
{
    public string RelatedEntityType => NotificationEntityTypes.OrthoInstallment;

    public async Task<bool> IsStillValidAsync(Guid relatedEntityId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        var current = await db.Set<OrthoInstallmentRevision>().AsNoTracking().Where(r => r.InstallmentId == relatedEntityId)
            .OrderByDescending(r => r.RevisionNumber).FirstOrDefaultAsync(cancellationToken);
        var chargeId = await (from i in db.Set<OrthoInstallment>().AsNoTracking()
                              join c in db.Set<OrthoCase>().AsNoTracking() on i.OrthoCaseId equals c.TreatmentCaseId
                              where i.Id == relatedEntityId
                              select c.ChargeId).FirstOrDefaultAsync(cancellationToken);
        if (current is not { IsActive: true } || chargeId is null)
        {
            return false;
        }

        return (await ledger.BalanceAsync(chargeId.Value, cancellationToken))?.Outstanding > 0;
    }
}

/// <summary>FR-PAT-010: installment entries (due date and derived status) for every audience; never shares or percentages.</summary>
internal sealed class InstallmentTimelineSource(DbContext db, OrthoStateLoader loader, IChargeLedger ledger, IClinicSettings settings, IClock clock)
    : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var cases = await db.Set<OrthoCase>().AsNoTracking().Where(o => o.ChargeId != null).Select(o => new { o.TreatmentCaseId, ChargeId = o.ChargeId!.Value })
            .ToListAsync(cancellationToken);
        var balances = await ledger.BalancesAsync(cases.Select(c => c.ChargeId), cancellationToken);
        var today = await settings.GetClinicTodayAsync(clock.UtcNow, cancellationToken);
        var entries = new List<PatientTimelineEntry>();
        foreach (var c in cases.Where(c => balances.GetValueOrDefault(c.ChargeId)?.PatientId == patientId))
        {
            var state = await loader.LoadAsync(c.TreatmentCaseId, cancellationToken);
            entries.AddRange(state!.Active.Select(i =>
                new PatientTimelineEntry("Installment", i.Installment.Origin, null, i.Current.DueDate, i.Installment.Id, i.Status(today))));
        }

        return entries;
    }
}
