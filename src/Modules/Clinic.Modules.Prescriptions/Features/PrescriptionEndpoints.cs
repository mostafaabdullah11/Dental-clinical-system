using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Prescriptions.Domain;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Prescriptions.Features.Doctor;

// Doctor audience: no patient contact data anywhere, including the print model (FR-RX-003).

public sealed record MedicationItemInput(Guid? Id, string MedicationName, string? Dosage, string? Frequency, string? Duration, string? Instructions);

public sealed record SavePrescriptionRequest(string? GeneralNotes, IReadOnlyList<MedicationItemInput> Items);

public sealed record CorrectPrescriptionRequest(string? GeneralNotes, IReadOnlyList<MedicationItemInput> Items, string? Reason);

public sealed record MedicationItemDto(Guid Id, int SortOrder, string MedicationName, string? Dosage, string? Frequency, string? Duration, string? Instructions);

public sealed record PrescriptionDoctorDto(Guid Id, string Name);

public sealed record PrescriptionDto(
    Guid Id, Guid PatientId, Guid VisitId, PrescriptionDoctorDto Doctor, string? GeneralNotes, DateTimeOffset CreatedAt, bool VisitFinalized, bool IsOwn,
    IReadOnlyList<MedicationItemDto> Items, string Version);

/// <summary>
/// FR-RX-003 print model: clinic branding and contact, patient name and age, Doctor name and specialty, date, medications.
/// It has no patient contact field at all, so no contact value can reach a printed prescription.
/// </summary>
public sealed record PrescriptionPrintModel(
    string ClinicName, string ClinicContact, string PatientName, string PatientCode, AgeDisplay PatientAge, string DoctorName, string? DoctorSpecialty,
    DateTimeOffset Date, string? GeneralNotes, IReadOnlyList<MedicationItemDto> Medications);

internal sealed class SavePrescriptionRequestValidator : AbstractValidator<SavePrescriptionRequest>
{
    public SavePrescriptionRequestValidator()
    {
        RuleFor(x => x.GeneralNotes).MaximumLength(2000);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new MedicationItemInputValidator());
    }
}

internal sealed class CorrectPrescriptionRequestValidator : AbstractValidator<CorrectPrescriptionRequest>
{
    public CorrectPrescriptionRequestValidator()
    {
        RuleFor(x => x.GeneralNotes).MaximumLength(2000);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new MedicationItemInputValidator());
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class MedicationItemInputValidator : AbstractValidator<MedicationItemInput>
{
    public MedicationItemInputValidator()
    {
        RuleFor(x => x.MedicationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Dosage).MaximumLength(200);
        RuleFor(x => x.Frequency).MaximumLength(200);
        RuleFor(x => x.Duration).MaximumLength(200);
        RuleFor(x => x.Instructions).MaximumLength(1000);
    }
}

/// <summary>FR-RX-001…004. Read breadth (every Doctor reads every prescription) never implies write rights.</summary>
internal static class PrescriptionEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapGet("/patients/{id:guid}/prescriptions", ListAsync).RequirePermission(PermissionKeys.PrescriptionsRead).Produces<List<PrescriptionDto>>();
        doctor.MapGet("/prescriptions/{id:guid}", GetAsync).RequirePermission(PermissionKeys.PrescriptionsRead).Produces<PrescriptionDto>();
        doctor.MapGet("/prescriptions/{id:guid}/print", PrintAsync).RequirePermission(PermissionKeys.PrescriptionsPrint).Produces<PrescriptionPrintModel>();
        doctor.MapPost("/visits/{visitId:guid}/prescriptions", CreateAsync).RequirePermission(PermissionKeys.PrescriptionsWrite)
            .Produces<PrescriptionDto>(StatusCodes.Status201Created).Validate<SavePrescriptionRequest>();
        doctor.MapPut("/prescriptions/{id:guid}", UpdateAsync).RequirePermission(PermissionKeys.PrescriptionsWrite)
            .Produces<PrescriptionDto>().Validate<SavePrescriptionRequest>();
        doctor.MapPost("/prescriptions/{id:guid}/corrections", CorrectAsync).RequirePermission(PermissionKeys.PrescriptionsCorrectFinalized)
            .Produces<PrescriptionDto>().Validate<CorrectPrescriptionRequest>();
    }

    private static async Task<IResult> ListAsync(Guid id, DbContext db, PrescriptionViews views, CancellationToken ct)
    {
        var rows = await db.Set<Prescription>().AsNoTracking().Where(p => p.PatientId == id && p.VoidedAt == null)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        return Results.Ok(await views.ProjectAsync(rows, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, PrescriptionViews views, CancellationToken ct)
    {
        var row = await db.Set<Prescription>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw AppException.NotFound();
        return Results.Ok((await views.ProjectAsync([row], ct))[0]);
    }

    /// <summary>Print model generated on demand and never stored; each print is audited (FR-RX-003).</summary>
    private static async Task<IResult> PrintAsync(Guid id, DbContext db, IPatientDirectory patients, IDoctorDirectory doctors, IClinicSettings settings,
        IAuditWriter audit, CancellationToken ct)
    {
        var row = await db.Set<Prescription>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.VoidedAt == null, ct) ?? throw AppException.NotFound();
        var items = await ActiveItemsAsync(db, id, ct);
        var patient = (await patients.FindClinicalManyAsync([row.PatientId], ct))[row.PatientId];
        var doctor = await doctors.FindAsync(row.DoctorId, ct);
        var clinic = await settings.GetClinicProfileAsync(ct);
        audit.Add(new AuditEvent("prescriptions.printed", "prescriptions.prescriptions", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new PrescriptionPrintModel(clinic.Name, clinic.Contact, patient.FullName, patient.PatientCode, patient.AgeDisplay,
            doctor?.FullName ?? string.Empty, doctor?.Specialty, row.CreatedAt, row.GeneralNotes, items));
    }

    private static async Task<IResult> CreateAsync(Guid visitId, SavePrescriptionRequest body, HttpResponse response, DbContext db,
        PrescriptionAccess access, PrescriptionViews views, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var visit = await access.RequireWritableVisitAsync(visitId, ct);
        var prescription = new Prescription
        {
            Id = Guid.CreateVersion7(), PatientId = visit.PatientId, DoctorId = visit.DoctorId, VisitId = visitId,
            GeneralNotes = Clean(body.GeneralNotes), CreatedAt = clock.UtcNow,
        };
        db.Set<Prescription>().Add(prescription);
        var order = 1;
        foreach (var item in body.Items)
        {
            db.Set<PrescriptionItem>().Add(NewItem(prescription.Id, order++, item));
        }

        audit.Add(new AuditEvent("prescriptions.created", "prescriptions.prescriptions", prescription.Id.ToString(), IsRestricted: true));
        await db.SaveChangesAsync(ct);
        var dto = (await views.ProjectAsync([prescription], ct))[0];
        response.Headers.ETag = dto.Version;
        return Results.Created($"/api/doctor/prescriptions/{prescription.Id}", dto);
    }

    /// <summary>Editable Visit only; a finalized Visit's prescription is 404 here (use the correction route, FR-RX-001).</summary>
    private static async Task<IResult> UpdateAsync(Guid id, SavePrescriptionRequest body, HttpRequest request, HttpResponse response, DbContext db,
        PrescriptionAccess access, PrescriptionViews views, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var prescription = await LockAsync(db, id, ct) ?? throw AppException.NotFound();
        var visit = await access.FindVisitAsync(prescription.VisitId, ct);
        if (visit is null || !VisitStatuses.IsEditable(visit.Status) || !await access.CanWriteAsync(visit, ct))
        {
            throw AppException.NotFound();
        }

        RequireIfMatch(request.Headers.IfMatch.ToString(), prescription.RowVersion);
        prescription.GeneralNotes = Clean(body.GeneralNotes);
        await ReplaceItemsAsync(db, prescription.Id, body.Items, clock.UtcNow, ct);
        audit.Add(new AuditEvent("prescriptions.updated", "prescriptions.prescriptions", id.ToString(), IsRestricted: true));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        var dto = (await views.ProjectAsync([await db.Set<Prescription>().AsNoTracking().SingleAsync(p => p.Id == id, ct)], ct))[0];
        response.Headers.ETag = dto.Version;
        return Results.Ok(dto);
    }

    /// <summary>
    /// FR-RX-004: the owning Doctor corrects their prescription on a finalized Visit. One visit_corrections row per corrected
    /// record (Prescription / PrescriptionItem) and an audit entry holding the previous version, flagged after_finalization.
    /// </summary>
    private static async Task<IResult> CorrectAsync(Guid id, CorrectPrescriptionRequest body, DbContext db, PrescriptionAccess access,
        IClinicalVisitAccess clinical, PrescriptionViews views, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var prescription = await LockAsync(db, id, ct) ?? throw AppException.NotFound();
        var own = await access.DoctorIdAsync(ct);
        var visit = await access.FindVisitAsync(prescription.VisitId, ct);
        if (visit is null || prescription.DoctorId != own)
        {
            throw AppException.NotFound();
        }

        if (visit.Status != VisitStatuses.Finalized)
        {
            throw AppException.Coded(422, "correction.invalid");
        }

        var before = await ActiveItemsAsync(db, id, ct);
        var previous = new { generalNotes = prescription.GeneralNotes, items = before };
        var notes = Clean(body.GeneralNotes);
        if (notes != prescription.GeneralNotes)
        {
            await clinical.RecordCorrectionAsync(visit.Id, visit.DoctorId, CorrectionRecordTypes.Prescription, id, ["generalNotes"], body.Reason, ct);
        }

        var touched = body.Items.Where(i => i.Id is not null).Select(i => i.Id!.Value).Concat(before.Select(b => b.Id)).Distinct().ToList();
        foreach (var itemId in touched)
        {
            await clinical.RecordCorrectionAsync(visit.Id, visit.DoctorId, CorrectionRecordTypes.PrescriptionItem, itemId, ["item"], body.Reason, ct);
        }

        // New items have no id yet: one correction row on the prescription record covers their insertion.
        if (body.Items.Any(i => i.Id is null))
        {
            await clinical.RecordCorrectionAsync(visit.Id, visit.DoctorId, CorrectionRecordTypes.Prescription, id, ["items"], body.Reason, ct);
        }

        prescription.GeneralNotes = notes;
        await ReplaceItemsAsync(db, id, body.Items, clock.UtcNow, ct);
        audit.Add(new AuditEvent("prescriptions.corrected", "prescriptions.prescriptions", id.ToString(), PreviousValue: previous,
            NewValue: new { after_finalization = true }, Reason: body.Reason, IsRestricted: true));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return Results.Ok((await views.ProjectAsync([await db.Set<Prescription>().AsNoTracking().SingleAsync(p => p.Id == id, ct)], ct))[0]);
    }

    private static async Task ReplaceItemsAsync(DbContext db, Guid prescriptionId, IReadOnlyList<MedicationItemInput> items, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Set<PrescriptionItem>().Where(i => i.PrescriptionId == prescriptionId && i.RemovedAt == null).ToListAsync(ct);
        var keep = items.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToHashSet();
        if (keep.Any(k => existing.All(e => e.Id != k)))
        {
            throw AppException.Validation("items", FieldErrorCodes.Invalid);
        }

        foreach (var row in existing.Where(e => !keep.Contains(e.Id)))
        {
            row.RemovedAt = now; // soft removal, never deleted
        }

        var order = 1;
        foreach (var input in items)
        {
            if (input.Id is { } itemId)
            {
                var row = existing.Single(e => e.Id == itemId);
                row.SortOrder = order++;
                row.MedicationName = input.MedicationName.Trim();
                row.Dosage = Clean(input.Dosage);
                row.Frequency = Clean(input.Frequency);
                row.Duration = Clean(input.Duration);
                row.Instructions = Clean(input.Instructions);
            }
            else
            {
                db.Set<PrescriptionItem>().Add(NewItem(prescriptionId, order++, input));
            }
        }
    }

    internal static async Task<IReadOnlyList<MedicationItemDto>> ActiveItemsAsync(DbContext db, Guid prescriptionId, CancellationToken ct) =>
        await db.Set<PrescriptionItem>().AsNoTracking().Where(i => i.PrescriptionId == prescriptionId && i.RemovedAt == null).OrderBy(i => i.SortOrder)
            .Select(i => new MedicationItemDto(i.Id, i.SortOrder, i.MedicationName, i.Dosage, i.Frequency, i.Duration, i.Instructions)).ToListAsync(ct);

    private static PrescriptionItem NewItem(Guid prescriptionId, int order, MedicationItemInput input) => new()
    {
        Id = Guid.CreateVersion7(), PrescriptionId = prescriptionId, SortOrder = order, MedicationName = input.MedicationName.Trim(),
        Dosage = Clean(input.Dosage), Frequency = Clean(input.Frequency), Duration = Clean(input.Duration), Instructions = Clean(input.Instructions),
    };

    private static Task<Prescription?> LockAsync(DbContext db, Guid id, CancellationToken ct) =>
        db.Set<Prescription>().FromSqlInterpolated($"SELECT p.*, p.xmin FROM prescriptions.prescriptions p WHERE p.id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    private static void RequireIfMatch(string? ifMatch, uint rowVersion)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw AppException.PreconditionRequired();
        }

        if (!string.Equals(ifMatch.Trim(), ETags.Format(rowVersion), StringComparison.Ordinal))
        {
            throw AppException.Stale();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Visit scope for prescription writes: the Doctor's own editable Visit; the Manager edits non-finalized only.</summary>
internal sealed class PrescriptionAccess(IClinicalVisitAccess clinical, ICurrentStaff staff)
{
    public Task<VisitAccess?> FindVisitAsync(Guid visitId, CancellationToken ct) => clinical.FindVisitAsync(visitId, ct);

    public Task<Guid?> DoctorIdAsync(CancellationToken ct) => staff.DoctorIdAsync(ct);

    public async Task<bool> CanWriteAsync(VisitAccess visit, CancellationToken ct) => staff.IsManager || visit.DoctorId == await staff.DoctorIdAsync(ct);

    public async Task<VisitAccess> RequireWritableVisitAsync(Guid visitId, CancellationToken ct)
    {
        var visit = await clinical.FindVisitAsync(visitId, ct);
        if (visit is null || !await CanWriteAsync(visit, ct))
        {
            throw AppException.NotFound();
        }

        if (!VisitStatuses.IsEditable(visit.Status))
        {
            throw AppException.Coded(422, "visit.finalized");
        }

        return visit;
    }
}

internal sealed class PrescriptionViews(DbContext db, IClinicalVisitAccess clinical, IStaffDirectory staff, ICurrentStaff current)
{
    public async Task<IReadOnlyList<PrescriptionDto>> ProjectAsync(IReadOnlyList<Prescription> rows, CancellationToken ct)
    {
        var names = await staff.DoctorNamesAsync(rows.Select(r => r.DoctorId), ct);
        var own = await current.DoctorIdAsync(ct);
        var result = new List<PrescriptionDto>();
        foreach (var p in rows)
        {
            var visit = await clinical.FindVisitAsync(p.VisitId, ct);
            result.Add(new PrescriptionDto(p.Id, p.PatientId, p.VisitId, new PrescriptionDoctorDto(p.DoctorId, names.GetValueOrDefault(p.DoctorId) ?? string.Empty),
                p.GeneralNotes, p.CreatedAt, visit?.Status == VisitStatuses.Finalized, p.DoctorId == own,
                await PrescriptionEndpoints.ActiveItemsAsync(db, p.Id, ct), ETags.Format(p.RowVersion)));
        }

        return result;
    }
}
