using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Infrastructure;

public sealed record PricingLineRef(string Kind, Guid Id)
{
    public string LineId => Format(Kind, Id);

    public static string Format(string kind, Guid id) => (kind == PricingLineKinds.WorkItem ? "w_" : "c_") + id.ToString("N");

    public static PricingLineRef Parse(string lineId)
    {
        if (lineId.Length == 34 && lineId[1] == '_' && Guid.TryParseExact(lineId[2..], "N", out var id))
        {
            if (lineId[0] == 'w')
            {
                return new PricingLineRef(PricingLineKinds.WorkItem, id);
            }

            if (lineId[0] == 'c')
            {
                return new PricingLineRef(PricingLineKinds.Consultation, id);
            }
        }

        throw AppException.NotFound();
    }
}

/// <summary>One pricing line as the pricing commands see it (price, current discount, owning Visit and Doctor).</summary>
internal sealed record PricingLineState(
    PricingLineRef Ref, Guid VisitId, Guid PatientId, Guid DoctorId, string VisitStatus, decimal? Price, decimal Discount, int EntrySeq,
    string? LastSetByRole, DateTimeOffset? LastSetAt, Guid? TreatmentCaseId, string? ProcedureCode, string? ProcedureName, string? LabModel);

/// <summary>
/// Doctor-entered price of own work (FR-CLN-007 / -008 / -009), consultation fee checks, and the fixed-EGP discount entries
/// (FR-FIN-021 / -023 / -025; Owner change 1: Doctor on own line, Sales and Manager on any priced line, before finalization).
/// Charges are not created here: Finance establishes them at finalization (Task 4) from these values.
/// </summary>
internal sealed class PricingService(DbContext db, IAuditWriter audit, ICurrentActor actor, IClock clock, IDoctorDirectory doctors, ClinicalActor clinical)
{
    public async Task<CaseWorkItem> SetPriceForRecordAsync(Guid recordId, decimal price, string? ifMatch, CancellationToken ct)
    {
        var own = await clinical.RequireDoctorAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var record = await db.Set<TreatmentRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null || record.DoctorId != own)
        {
            throw AppException.NotFound();
        }

        var visit = await ClinicalLocks.VisitAsync(db, record.VisitId, ct) ?? throw AppException.NotFound();
        if (!VisitStatuses.IsEditable(visit.Status))
        {
            throw AppException.Coded(422, ClinicalErrors.ChargeExists);
        }

        var procedure = await ProcedureCodeOfCaseAsync(record.TreatmentCaseId, ct);
        var item = await db.Set<CaseWorkItem>().SingleOrDefaultAsync(w => w.TreatmentRecordId == recordId, ct);
        if (procedure == ProcedureCodes.Orthodontics && item is null)
        {
            // Orthodontics keeps a single work item per case, created with the terms (FR-CLN-008, FR-ORT-001).
            throw AppException.Coded(422, ClinicalErrors.OrthoSinglePrice);
        }

        if (item is null)
        {
            item = new CaseWorkItem
            {
                Id = Guid.CreateVersion7(), TreatmentCaseId = record.TreatmentCaseId, PatientId = record.PatientId, PerformingDoctorId = own,
                DoctorPercentageSnapshot = await doctors.CurrentPercentageAsync(own, ct), EnteredPrice = price, TreatmentRecordId = recordId,
                CreatedAt = clock.UtcNow,
            };
            db.Set<CaseWorkItem>().Add(item);
            audit.Add(new AuditEvent("clinical.work_item.priced", "clinical.case_work_items", item.Id.ToString(), NewValue: new { price }));
        }
        else
        {
            await UpdatePriceAsync(item, price, ifMatch, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public async Task<CaseWorkItem> SetPriceForWorkItemAsync(Guid workItemId, decimal price, string? ifMatch, CancellationToken ct)
    {
        var own = await clinical.RequireDoctorAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await ClinicalLocks.WorkItemAsync(db, workItemId, ct);
        if (item is null || item.PerformingDoctorId != own)
        {
            throw AppException.NotFound();
        }

        var visitId = await db.Set<TreatmentRecord>().Where(r => r.Id == item.TreatmentRecordId).Select(r => r.VisitId).SingleAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        if (!VisitStatuses.IsEditable(visit.Status))
        {
            throw AppException.Coded(422, ClinicalErrors.ChargeExists);
        }

        await UpdatePriceAsync(item, price, ifMatch, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    /// <summary>
    /// Appends a discount entry. The caller saw <paramref name="expectedEntrySeq"/>; a concurrent writer makes it stale (409).
    /// Doctor: own line only (404 otherwise). Sales / Manager: any line whose price exists.
    /// </summary>
    public async Task<PricingLineState> SetDiscountAsync(string lineId, decimal amount, int expectedEntrySeq, bool doctorRoute, CancellationToken ct)
    {
        var line = PricingLineRef.Parse(lineId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await StateAsync(line, ct) ?? throw AppException.NotFound();
        if (doctorRoute && state.DoctorId != await clinical.RequireDoctorAsync(ct))
        {
            throw AppException.NotFound();
        }

        await ClinicalLocks.VisitAsync(db, state.VisitId, ct);
        state = await StateAsync(line, ct) ?? throw AppException.NotFound();
        if (!VisitStatuses.IsEditable(state.VisitStatus))
        {
            throw AppException.Coded(422, ClinicalErrors.VisitFinalized);
        }

        if (state.Price is not { } price)
        {
            throw AppException.Coded(422, ClinicalErrors.PriceRequired);
        }

        if (amount > price)
        {
            throw AppException.Coded(422, ClinicalErrors.DiscountExceedsPrice);
        }

        if (expectedEntrySeq != state.EntrySeq)
        {
            throw AppException.Coded(409, ClinicalErrors.DiscountStale);
        }

        var entry = new PricingDiscountEntry
        {
            Id = Guid.CreateVersion7(),
            PricingLineKind = line.Kind,
            CaseWorkItemId = line.Kind == PricingLineKinds.WorkItem ? line.Id : null,
            ConsultationVisitId = line.Kind == PricingLineKinds.Consultation ? line.Id : null,
            EntrySeq = state.EntrySeq + 1,
            Amount = amount,
            SetByUserId = actor.UserId!.Value,
            SetByRole = actor.Role!,
            SetAt = clock.UtcNow,
        };
        db.Set<PricingDiscountEntry>().Add(entry);
        audit.Add(new AuditEvent("clinical.discount.set", "clinical.pricing_discount_entries", line.LineId,
            PreviousValue: new { discount = state.Discount }, NewValue: new { discount = amount, role = actor.Role }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_pricing_discount_entries_work_item_seq")
                                         || DbErrors.IsUniqueViolation(e, "ix_pricing_discount_entries_consultation_seq"))
        {
            throw AppException.Coded(409, ClinicalErrors.DiscountStale);
        }

        await transaction.CommitAsync(ct);
        return (await StateAsync(line, ct))!;
    }

    /// <summary>Priced, not-yet-finalized lines of the patient's open Visits (FR-FIN-022). Never clinical content.</summary>
    public async Task<IReadOnlyList<PricingLineState>> OpenLinesForPatientAsync(Guid patientId, CancellationToken ct)
    {
        var visitIds = await db.Set<Visit>().AsNoTracking()
            .Where(v => v.PatientId == patientId && v.Status != VisitStatuses.Finalized).Select(v => v.Id).ToListAsync(ct);
        var result = new List<PricingLineState>();
        foreach (var visitId in visitIds)
        {
            result.AddRange(await LinesForVisitAsync(visitId, ct));
        }

        return result;
    }

    public async Task<IReadOnlyList<PricingLineState>> LinesForVisitAsync(Guid visitId, CancellationToken ct)
    {
        var lines = new List<PricingLineState>();
        var itemIds = await (from w in db.Set<CaseWorkItem>().AsNoTracking()
                             join r in db.Set<TreatmentRecord>().AsNoTracking() on w.TreatmentRecordId equals r.Id
                             where r.VisitId == visitId
                             orderby w.CreatedAt
                             select w.Id).ToListAsync(ct);
        foreach (var id in itemIds)
        {
            lines.Add((await StateAsync(new PricingLineRef(PricingLineKinds.WorkItem, id), ct))!);
        }

        var consultation = await StateAsync(new PricingLineRef(PricingLineKinds.Consultation, visitId), ct);
        if (consultation?.Price is not null)
        {
            lines.Add(consultation);
        }

        return lines;
    }

    public async Task<PricingLineState?> StateAsync(PricingLineRef line, CancellationToken ct)
    {
        if (line.Kind == PricingLineKinds.WorkItem)
        {
            var row = await (from w in db.Set<CaseWorkItem>().AsNoTracking()
                             join r in db.Set<TreatmentRecord>().AsNoTracking() on w.TreatmentRecordId equals r.Id
                             join v in db.Set<Visit>().AsNoTracking() on r.VisitId equals v.Id
                             join c in db.Set<TreatmentCase>().AsNoTracking() on w.TreatmentCaseId equals c.Id
                             join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
                             where w.Id == line.Id
                             select new { v.Id, v.PatientId, v.DoctorId, v.Status, w.EnteredPrice, CaseId = c.Id, t.Code, t.Name, c.LabModelSnapshot })
                .SingleOrDefaultAsync(ct);
            if (row is null)
            {
                return null;
            }

            var last = await LastEntryAsync(e => e.CaseWorkItemId == line.Id, ct);
            return new PricingLineState(line, row.Id, row.PatientId, row.DoctorId, row.Status, row.EnteredPrice, last?.Amount ?? 0m, last?.EntrySeq ?? 0,
                last?.SetByRole, last?.SetAt, row.CaseId, row.Code, row.Name, row.LabModelSnapshot);
        }

        var visit = await db.Set<Visit>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == line.Id, ct);
        if (visit is null)
        {
            return null;
        }

        var lastFee = await LastEntryAsync(e => e.ConsultationVisitId == line.Id, ct);
        return new PricingLineState(line, visit.Id, visit.PatientId, visit.DoctorId, visit.Status, visit.ConsultationFee, lastFee?.Amount ?? 0m,
            lastFee?.EntrySeq ?? 0, lastFee?.SetByRole, lastFee?.SetAt, null, null, null, null);
    }

    public async Task<decimal> CurrentConsultationDiscountAsync(Guid visitId, CancellationToken ct) =>
        (await LastEntryAsync(e => e.ConsultationVisitId == visitId, ct))?.Amount ?? 0m;

    private Task<PricingDiscountEntry?> LastEntryAsync(System.Linq.Expressions.Expression<Func<PricingDiscountEntry, bool>> filter, CancellationToken ct) =>
        db.Set<PricingDiscountEntry>().AsNoTracking().Where(filter).OrderByDescending(e => e.EntrySeq).FirstOrDefaultAsync(ct);

    private async Task UpdatePriceAsync(CaseWorkItem item, decimal price, string? ifMatch, CancellationToken ct)
    {
        ClinicalLocks.RequireIfMatch(ifMatch, item.RowVersion);
        var discount = (await LastEntryAsync(e => e.CaseWorkItemId == item.Id, ct))?.Amount ?? 0m;
        if (price < discount)
        {
            throw AppException.Coded(422, ClinicalErrors.PriceBelowDiscount);
        }

        if (await ProcedureCodeOfCaseAsync(item.TreatmentCaseId, ct) == ProcedureCodes.Orthodontics)
        {
            // An Ortho price is the Gross Total of the terms; it is never edited through the generic price route.
            throw AppException.Coded(422, ClinicalErrors.OrthoSinglePrice);
        }

        var previous = item.EnteredPrice;
        item.EnteredPrice = price;
        audit.Add(new AuditEvent("clinical.work_item.price_changed", "clinical.case_work_items", item.Id.ToString(),
            PreviousValue: new { price = previous }, NewValue: new { price }));
    }

    private Task<string> ProcedureCodeOfCaseAsync(Guid caseId, CancellationToken ct) =>
        (from c in db.Set<TreatmentCase>().AsNoTracking()
         join t in db.Set<ProcedureType>().AsNoTracking() on c.ProcedureTypeId equals t.Id
         where c.Id == caseId
         select t.Code).SingleAsync(ct);
}
