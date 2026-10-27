using System.Text.Json.Nodes;
using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Domain;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Clinical.Infrastructure;

public sealed record CorrectionInput(
    string RecordType, Guid RecordId, string? ClinicalNotes, IReadOnlyList<string>? Teeth, string? Area, JsonObject? Details,
    string? StepStatus, decimal? LengthMm, string? Reason);

/// <summary>
/// Visit lifecycle (FR-CLN-001 / -006 / -010 / -012 / -014; FR-SCH-012 / -013). Start and finalize change the linked
/// appointment in the same transaction; finalization publishes <see cref="VisitFinalized"/> to its subscribers inside it.
/// </summary>
internal sealed class VisitService(
    DbContext db, IAuditWriter audit, IClock clock, ClinicalActor actor, IPatientDirectory patients, IAppointmentLifecycle appointments,
    IDomainEventDispatcher events, IEnumerable<IVisitFinalizationGuard> guards, PricingService pricing, TreatmentService treatments,
    CorrectionRecorder corrections)
{
    public async Task<Visit> StartAsync(Guid patientId, Guid? appointmentId, CancellationToken ct)
    {
        var doctorId = await actor.RequireDoctorAsync(ct);
        if (!await patients.ExistsAsync(patientId, ct))
        {
            throw AppException.Validation("patientId", FieldErrorCodes.Invalid);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (appointmentId is { } aid)
        {
            var appointment = await appointments.FindAsync(aid, ct);
            if (appointment is null || appointment.DoctorId != doctorId || appointment.PatientId != patientId)
            {
                throw AppException.NotFound();
            }

            if (await db.Set<Visit>().AnyAsync(v => v.AppointmentId == aid, ct))
            {
                throw AppException.Coded(409, ClinicalErrors.VisitAlreadyStarted);
            }
        }

        var visit = new Visit
        {
            Id = Guid.CreateVersion7(), PatientId = patientId, DoctorId = doctorId, AppointmentId = appointmentId,
            Status = VisitStatuses.InProgress, StartedAt = clock.UtcNow,
        };
        db.Set<Visit>().Add(visit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e, "ix_visits_appointment_id"))
        {
            throw AppException.Coded(409, ClinicalErrors.VisitAlreadyStarted);
        }

        if (appointmentId is { } linked)
        {
            await appointments.MarkInTreatmentAsync(linked, ct);
        }

        audit.Add(new AuditEvent("clinical.visit.started", "clinical.visits", visit.Id.ToString(), NewValue: new { patientId, appointmentId }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return visit;
    }

    /// <summary>Notes (Doctor own / Manager) and the consultation fee (Doctor only; a Manager change is 403).</summary>
    public async Task<Visit> UpdateAsync(Guid visitId, string? clinicalNotes, decimal? consultationFee, string? ifMatch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureCanWriteAsync(visit, ct);
        ClinicalLocks.RequireIfMatch(ifMatch, visit.RowVersion);
        if (consultationFee != visit.ConsultationFee)
        {
            if (actor.IsManager)
            {
                throw new AppException(403, ErrorCodes.Forbidden);
            }

            if (consultationFee is { } fee && fee < await pricing.CurrentConsultationDiscountAsync(visitId, ct))
            {
                throw AppException.Coded(422, ClinicalErrors.PriceBelowDiscount);
            }

            if (consultationFee is null && await pricing.CurrentConsultationDiscountAsync(visitId, ct) > 0)
            {
                throw AppException.Coded(422, ClinicalErrors.PriceBelowDiscount);
            }

            audit.Add(new AuditEvent("clinical.visit.consultation_fee_set", "clinical.visits", visitId.ToString(),
                PreviousValue: new { fee = visit.ConsultationFee }, NewValue: new { fee = consultationFee }));
            visit.ConsultationFee = consultationFee;
        }

        visit.ClinicalNotes = clinicalNotes;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return visit;
    }

    /// <summary>
    /// FR-CLN-012, one transaction: preconditions (discount ≤ price per line; Fixed Lab and Ortho guards) → Visit Finalized
    /// (finalized_at = actual end) → linked appointment Completed with visit_ended_at → <see cref="VisitFinalized"/>
    /// subscribers (Feedback request now; Finance charges in Task 4). Idempotent; any failure rolls everything back.
    /// </summary>
    public async Task<Visit> FinalizeAsync(Guid visitId, string? ifMatch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureOwnAsync(visit, ct);
        if (visit.Status == VisitStatuses.Finalized)
        {
            return visit;
        }

        ClinicalLocks.RequireIfMatch(ifMatch, visit.RowVersion);
        var states = await pricing.LinesForVisitAsync(visitId, ct);
        var lines = states
            .Select(l => new FinalizationLine(l.Ref.Kind, l.Ref.Kind == PricingLineKinds.WorkItem ? l.Ref.Id : null, l.TreatmentCaseId, l.ProcedureCode,
                l.LabModel, l.Price ?? 0m, l.Discount))
            .ToList();
        if (lines.Any(l => l.Discount > l.Price))
        {
            throw AppException.Coded(422, ClinicalErrors.DiscountExceedsPrice);
        }

        var context = new VisitFinalizationContext(visit.Id, visit.PatientId, visit.DoctorId, lines);
        foreach (var guard in guards)
        {
            if (await guard.ValidateAsync(context, ct) is { } code)
            {
                throw AppException.Coded(422, code);
            }
        }

        var now = clock.UtcNow;
        visit.Status = VisitStatuses.Finalized;
        visit.FinalizedAt = now;
        visit.FinalizedByUserId = actor.UserId;
        audit.Add(new AuditEvent("clinical.visit.finalized", "clinical.visits", visitId.ToString(),
            NewValue: new { finalizedAt = now, pricedLines = lines.Count }));
        await db.SaveChangesAsync(ct);

        if (visit.AppointmentId is { } appointmentId)
        {
            await appointments.CompleteForFinalizedVisitAsync(appointmentId, now, ct);
        }

        var charges = states.Where(l => l.Price is > 0).Select(l => new FinalizedChargeLine(
            l.Ref.Kind, l.Ref.Kind == PricingLineKinds.WorkItem ? l.Ref.Id : null, l.TreatmentCaseId, l.PerformingDoctorId, l.PercentageSnapshot, l.LabModel,
            l.ProcedureCode, l.Price!.Value, l.Discount, l.Discount > 0 ? l.LastSetByUserId : null, l.Discount > 0 ? l.LastSetByRole : null,
            l.Discount > 0 ? l.LastSetAt : null)).ToList();
        await events.PublishAsync(new VisitFinalized(visit.Id, visit.PatientId, visit.DoctorId, visit.AppointmentId, now)
        {
            Charges = charges, ActorUserId = actor.UserId ?? Guid.Empty,
        }, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return visit;
    }

    /// <summary>
    /// FR-CLN-014: the Visit's own Doctor corrects one clinical record of a finalized Visit. The Visit stays Finalized; a
    /// <c>visit_corrections</c> row and an audit entry with previous and new values are written in the same transaction.
    /// Price, discount and consultation fee are not part of this path (the request has no such members).
    /// </summary>
    public async Task CorrectAsync(Guid visitId, CorrectionInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var visit = await ClinicalLocks.VisitAsync(db, visitId, ct) ?? throw AppException.NotFound();
        await actor.EnsureOwnAsync(visit, ct);
        if (visit.Status != VisitStatuses.Finalized)
        {
            throw AppException.Coded(422, ClinicalErrors.CorrectionInvalid);
        }

        object? previous;
        object? next;
        string[] changed;
        switch (input.RecordType)
        {
            case CorrectionRecordTypes.VisitNotes:
                if (input.RecordId != visitId || input.ClinicalNotes is null)
                {
                    throw AppException.Coded(422, ClinicalErrors.CorrectionInvalid);
                }

                await corrections.RecordAsync(visitId, visit.DoctorId, input.RecordType, input.RecordId, ["clinicalNotes"], input.Reason, ct);
                previous = new { clinicalNotes = visit.ClinicalNotes };
                visit.ClinicalNotes = input.ClinicalNotes;
                next = new { clinicalNotes = input.ClinicalNotes };
                changed = ["clinicalNotes"];
                break;

            case CorrectionRecordTypes.TreatmentRecord:
            {
                var record = await db.Set<TreatmentRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == input.RecordId && r.VisitId == visitId, ct)
                             ?? throw AppException.NotFound();
                var caseRow = await ClinicalLocks.CaseAsync(db, record.TreatmentCaseId, ct) ?? throw AppException.NotFound();
                previous = new { teeth = caseRow.Teeth, area = caseRow.Area, details = caseRow.Details };
                await corrections.RecordAsync(visitId, visit.DoctorId, input.RecordType, input.RecordId, ["teeth", "area", "details"], input.Reason, ct);
                await treatments.ApplyCaseDetailsAsync(caseRow, visitId, input.Teeth, input.Area, input.Details, ct);
                next = new { teeth = caseRow.Teeth, area = caseRow.Area, details = caseRow.Details };
                changed = ["teeth", "area", "details"];
                break;
            }

            case CorrectionRecordTypes.TreatmentStep:
            {
                var step = await db.Set<TreatmentStep>().SingleOrDefaultAsync(s => s.Id == input.RecordId && s.CompletedInVisitId == visitId, ct)
                           ?? throw AppException.NotFound();
                if (input.StepStatus != StepStatuses.Pending)
                {
                    throw AppException.Coded(422, ClinicalErrors.CorrectionInvalid);
                }

                previous = new { status = step.Status, completedAt = step.CompletedAt };
                await corrections.RecordAsync(visitId, visit.DoctorId, input.RecordType, input.RecordId, ["status"], input.Reason, ct);
                step.Status = StepStatuses.Pending;
                step.CompletedAt = null;
                step.CompletedInVisitId = null;
                step.CompletedByDoctorId = null;
                next = new { status = StepStatuses.Pending };
                changed = ["status"];
                break;
            }

            case CorrectionRecordTypes.RootCanalMeasurement:
            {
                var measurement = await db.Set<RootCanalMeasurement>().SingleOrDefaultAsync(m => m.Id == input.RecordId && m.RecordedInVisitId == visitId, ct)
                                  ?? throw AppException.NotFound();
                if (input.LengthMm is not { } length || length <= 0 || length >= 1000)
                {
                    throw AppException.Coded(422, ClinicalErrors.CorrectionInvalid);
                }

                previous = new { lengthMm = measurement.LengthMm };
                await corrections.RecordAsync(visitId, visit.DoctorId, input.RecordType, input.RecordId, ["lengthMm"], input.Reason, ct);
                measurement.LengthMm = length;
                next = new { lengthMm = length };
                changed = ["lengthMm"];
                break;
            }

            default:
                throw AppException.Coded(422, ClinicalErrors.CorrectionInvalid);
        }

        audit.Add(new AuditEvent("clinical.visit.corrected", "clinical.visits", visitId.ToString(), PreviousValue: previous,
            NewValue: new { recordType = input.RecordType, recordId = input.RecordId, changedFields = changed, value = next, after_finalization = true },
            Reason: input.Reason, IsRestricted: true));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
