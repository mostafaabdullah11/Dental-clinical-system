using System.Text.Json;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Clinical.Infrastructure;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Features.Doctor;

// Doctor audience: an architecture test fails if this namespace depends on patient contact contracts or [ContactData] types.

/// <summary>
/// The Doctor clinical workflow (FR-CLN-001…014, FR-SCH-008, FR-FIN-021 Doctor route). Writes require the Doctor's own
/// editable Visit (the Manager edits non-finalized records only); reads cover every patient's history (FR-CLN-013).
/// </summary>
internal static class DoctorClinicalEndpoints
{
    public static void Map(RouteGroupBuilder doctor)
    {
        doctor.MapGet("/procedure-types", ProcedureTypesAsync).RequirePermission(PermissionKeys.ClinicalVisitsRead).Produces<List<ProcedureTypeDto>>();
        doctor.MapGet("/patients/{id:guid}/visits", PatientVisitsAsync).RequirePermission(PermissionKeys.ClinicalVisitsRead).Produces<List<VisitListItemDto>>();
        doctor.MapGet("/patients/{id:guid}/treatment-cases", PatientCasesAsync).RequirePermission(PermissionKeys.ClinicalVisitsRead).Produces<List<CaseSummaryDto>>();
        doctor.MapGet("/treatment-cases/{id:guid}", CaseAsync).RequirePermission(PermissionKeys.ClinicalVisitsRead).Produces<CaseDetailDto>();
        doctor.MapGet("/visits/{id:guid}", VisitAsync).RequirePermission(PermissionKeys.ClinicalVisitsRead).Produces<VisitDetailDto>();

        doctor.MapPost("/visits", StartAsync).RequirePermission(PermissionKeys.ClinicalVisitsWrite)
            .Produces<VisitDetailDto>(StatusCodes.Status201Created).Validate<StartVisitRequest>();
        doctor.MapPut("/visits/{id:guid}", UpdateAsync).RequirePermission(PermissionKeys.ClinicalVisitsWrite)
            .Produces<VisitDetailDto>().Validate<UpdateVisitRequest>();
        doctor.MapPost("/visits/{id:guid}/finalize", FinalizeAsync).RequirePermission(PermissionKeys.ClinicalVisitsFinalize).Produces<VisitDetailDto>();
        doctor.MapPost("/visits/{visitId:guid}/corrections", CorrectAsync).RequirePermission(PermissionKeys.ClinicalVisitsCorrectFinalized)
            .Produces<VisitDetailDto>().Validate<CorrectionRequest>();

        doctor.MapPost("/visits/{visitId:guid}/treatments", AddTreatmentAsync).RequirePermission(PermissionKeys.ClinicalTreatmentsWrite)
            .Produces<VisitDetailDto>(StatusCodes.Status201Created).Validate<AddTreatmentRequest>();
        doctor.MapPut("/visits/{visitId:guid}/treatments/{recordId:guid}", UpdateTreatmentAsync).RequirePermission(PermissionKeys.ClinicalTreatmentsWrite)
            .Produces<VisitDetailDto>();
        doctor.MapPut("/visits/{visitId:guid}/cases/{caseId:guid}/status", CaseStatusAsync).RequirePermission(PermissionKeys.ClinicalTreatmentsWrite)
            .Produces<VisitDetailDto>().Validate<CaseStatusRequest>();
        doctor.MapPost("/visits/{visitId:guid}/cases/{caseId:guid}/steps/{stepCode}/complete", CompleteStepAsync)
            .RequirePermission(PermissionKeys.ClinicalTreatmentsWrite).Produces<VisitDetailDto>();
        doctor.MapPut("/visits/{visitId:guid}/cases/{caseId:guid}/canal-measurements", MeasurementsAsync)
            .RequirePermission(PermissionKeys.ClinicalTreatmentsWrite).Produces<VisitDetailDto>().Validate<CanalMeasurementsRequest>();
        doctor.MapPut("/visits/{visitId:guid}/cases/{caseId:guid}/implant", ImplantAsync)
            .RequirePermission(PermissionKeys.ClinicalTreatmentsWrite).Produces<VisitDetailDto>();

        doctor.MapPut("/treatment-records/{recordId:guid}/pricing", PriceRecordAsync).RequirePermission(PermissionKeys.ClinicalWorkItemsEnterPrice)
            .Produces<VisitDetailDto>().Validate<PriceRequest>();
        doctor.MapPut("/work-items/{id:guid}/pricing", PriceWorkItemAsync).RequirePermission(PermissionKeys.ClinicalWorkItemsEnterPrice)
            .Produces<VisitDetailDto>().Validate<PriceRequest>();
        doctor.MapPut("/pricing-lines/{lineId}/discount", DiscountAsync).RequirePermission(PermissionKeys.FinanceDiscountSet)
            .Produces<VisitDetailDto>().Validate<DiscountRequest>();

        doctor.MapPost("/appointments/follow-up", FollowUpAsync).RequirePermission(PermissionKeys.SchedulingAppointmentsCreateFollowUp)
            .Produces<CreatedIdDto>(StatusCodes.Status201Created).Validate<FollowUpAppointmentBody>();
    }

    private static async Task<IResult> ProcedureTypesAsync(DbContext db, CancellationToken ct)
    {
        var rows = await db.Set<ProcedureType>().AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync(ct);
        return Results.Ok(rows.Select(t => new ProcedureTypeDto(t.Code, t.Name, t.Category, t.LabModel, JsonDocument.Parse(t.OptionDefinition).RootElement.Clone())).ToList());
    }

    private static async Task<IResult> PatientVisitsAsync(Guid id, DoctorClinicalViews views, CancellationToken ct) =>
        Results.Ok(await views.PatientVisitsAsync(id, ct));

    private static async Task<IResult> PatientCasesAsync(Guid id, DoctorClinicalViews views, CancellationToken ct) =>
        Results.Ok(await views.PatientCasesAsync(id, ct));

    private static async Task<IResult> CaseAsync(Guid id, DoctorClinicalViews views, CancellationToken ct) =>
        Results.Ok(await views.CaseAsync(id, ct) ?? throw AppException.NotFound());

    private static async Task<IResult> VisitAsync(Guid id, HttpResponse response, DbContext db, DoctorClinicalViews views, IAuditWriter audit, CancellationToken ct)
    {
        var visit = await db.Set<Visit>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw AppException.NotFound();
        audit.Add(new AuditEvent("clinical.visit.opened", "clinical.visits", id.ToString()));
        await db.SaveChangesAsync(ct);
        return Ok(response, await views.VisitAsync(visit, ct));
    }

    private static async Task<IResult> StartAsync(StartVisitRequest body, HttpResponse response, VisitService visits, DoctorClinicalViews views, CancellationToken ct)
    {
        var visit = await visits.StartAsync(body.PatientId!.Value, body.AppointmentId, ct);
        response.Headers.ETag = ETags.Format(visit.RowVersion);
        return Results.Created($"/api/doctor/visits/{visit.Id}", await views.VisitAsync(visit, ct));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateVisitRequest body, HttpRequest request, HttpResponse response, VisitService visits,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await visits.UpdateAsync(id, string.IsNullOrWhiteSpace(body.ClinicalNotes) ? null : body.ClinicalNotes.Trim(), body.ConsultationFee,
            request.Headers.IfMatch.ToString(), ct);
        return await ReloadAsync(id, response, db, views, ct);
    }

    private static async Task<IResult> FinalizeAsync(Guid id, HttpRequest request, HttpResponse response, VisitService visits, DbContext db,
        DoctorClinicalViews views, CancellationToken ct)
    {
        await visits.FinalizeAsync(id, request.Headers.IfMatch.ToString(), ct);
        return await ReloadAsync(id, response, db, views, ct);
    }

    private static async Task<IResult> CorrectAsync(Guid visitId, CorrectionRequest body, HttpResponse response, VisitService visits, DbContext db,
        DoctorClinicalViews views, CancellationToken ct)
    {
        await visits.CorrectAsync(visitId, new CorrectionInput(body.RecordType, body.RecordId, body.ClinicalNotes, body.Teeth, body.Area,
            ProcedureCatalog.ParseDetails(body.Details), body.StepStatus, body.LengthMm, body.Reason), ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> AddTreatmentAsync(Guid visitId, AddTreatmentRequest body, HttpResponse response, TreatmentService treatments,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.AddAsync(visitId, body.CaseId, body.ProcedureCode, body.Teeth, body.Area, ProcedureCatalog.ParseDetails(body.Details), ct);
        var visit = await db.Set<Visit>().AsNoTracking().SingleAsync(v => v.Id == visitId, ct);
        response.Headers.ETag = ETags.Format(visit.RowVersion);
        return Results.Created($"/api/doctor/visits/{visitId}", await views.VisitAsync(visit, ct));
    }

    private static async Task<IResult> UpdateTreatmentAsync(Guid visitId, Guid recordId, UpdateTreatmentRequest body, HttpResponse response,
        TreatmentService treatments, DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.UpdateAsync(visitId, recordId, body.Teeth, body.Area, ProcedureCatalog.ParseDetails(body.Details), ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> CaseStatusAsync(Guid visitId, Guid caseId, CaseStatusRequest body, HttpRequest request, HttpResponse response,
        TreatmentService treatments, DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.SetCaseStatusAsync(visitId, caseId, body.Status, request.Headers.IfMatch.ToString(), ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> CompleteStepAsync(Guid visitId, Guid caseId, string stepCode, HttpResponse response, TreatmentService treatments,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.CompleteStepAsync(visitId, caseId, stepCode, ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> MeasurementsAsync(Guid visitId, Guid caseId, CanalMeasurementsRequest body, HttpResponse response,
        TreatmentService treatments, DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.SaveMeasurementsAsync(visitId, caseId, body.Canals.Select(c => new CanalInput(c.CanalNumber, c.LengthMm)).ToList(), ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> ImplantAsync(Guid visitId, Guid caseId, ImplantDetailsRequest body, HttpResponse response, TreatmentService treatments,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await treatments.SaveImplantAsync(visitId, caseId, body.HasBoneGraft, body.HasMembrane, body.ProcedureDate, body.HealingMonths, ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> PriceRecordAsync(Guid recordId, PriceRequest body, HttpRequest request, HttpResponse response, PricingService pricing,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        await pricing.SetPriceForRecordAsync(recordId, body.Price, request.Headers.IfMatch.ToString(), ct);
        var visitId = await db.Set<TreatmentRecord>().Where(r => r.Id == recordId).Select(r => r.VisitId).SingleAsync(ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> PriceWorkItemAsync(Guid id, PriceRequest body, HttpRequest request, HttpResponse response, PricingService pricing,
        DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        var item = await pricing.SetPriceForWorkItemAsync(id, body.Price, request.Headers.IfMatch.ToString(), ct);
        var visitId = await db.Set<TreatmentRecord>().Where(r => r.Id == item.TreatmentRecordId).Select(r => r.VisitId).SingleAsync(ct);
        return await ReloadAsync(visitId, response, db, views, ct);
    }

    private static async Task<IResult> DiscountAsync(string lineId, DiscountRequest body, HttpResponse response, PricingService pricing, DbContext db,
        DoctorClinicalViews views, CancellationToken ct)
    {
        var state = await pricing.SetDiscountAsync(lineId, body.Discount, body.ExpectedEntrySeq, doctorRoute: true, ct);
        return await ReloadAsync(state.VisitId, response, db, views, ct);
    }

    /// <summary>FR-SCH-008: authoring Doctor = principal; own Visit only (404 otherwise, including for the Manager).</summary>
    private static async Task<IResult> FollowUpAsync(FollowUpAppointmentBody body, ClinicalActor actor, DbContext db, IAppointmentBooking booking,
        CancellationToken ct)
    {
        var own = await actor.RequireDoctorAsync(ct);
        var visit = await db.Set<Visit>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == body.VisitId, ct);
        if (visit is null || visit.DoctorId != own)
        {
            throw AppException.NotFound();
        }

        var id = await booking.CreateFollowUpAsync(new FollowUpAppointmentRequest(visit.PatientId, own, body.StartAt!.Value, (short)body.DurationMinutes!.Value,
            body.Reason.Trim(), body.Kind ?? AppointmentKinds.Standard), ct);
        return Results.Created($"/api/doctor/appointments/{id}", new CreatedIdDto(id));
    }

    private static async Task<IResult> ReloadAsync(Guid visitId, HttpResponse response, DbContext db, DoctorClinicalViews views, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var visit = await db.Set<Visit>().AsNoTracking().SingleAsync(v => v.Id == visitId, ct);
        return Ok(response, await views.VisitAsync(visit, ct));
    }

    private static IResult Ok(HttpResponse response, VisitDetailDto dto)
    {
        response.Headers.ETag = dto.Version;
        return Results.Ok(dto);
    }
}
