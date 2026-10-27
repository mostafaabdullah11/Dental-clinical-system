using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Finance.Contracts;
using Clinic.Modules.Finance.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Finance.Infrastructure;

/// <summary>
/// FR-FIN-001 / -002 (<c>Finance.EstablishCharges</c>): inside the Visit-finalize transaction, one Treatment charge per priced
/// work item and one Consultation charge when a fee was entered, each copying Doctor, percentage snapshot, lab model,
/// price and the discount snapshot passed by Clinical as data. Idempotent: an existing charge for the work item / Visit
/// is never duplicated (partial unique indexes are the final guard). Publishes <see cref="ChargeEstablished"/>.
/// </summary>
internal sealed class EstablishChargesOnVisitFinalized(DbContext db, IAuditWriter audit, IDomainEventDispatcher events)
    : IDomainEventHandler<VisitFinalized>
{
    public async Task HandleAsync(VisitFinalized e, CancellationToken cancellationToken)
    {
        var established = new List<PatientCharge>();
        foreach (var line in e.Charges)
        {
            if (line.Price <= 0 || line.Discount < 0 || line.Discount > line.Price)
            {
                throw AppException.Coded(422, "discount.exceeds_price");
            }

            var isTreatment = line.Kind == PricingLineKinds.WorkItem;
            var exists = isTreatment
                ? await db.Set<PatientCharge>().AnyAsync(c => c.CaseWorkItemId == line.WorkItemId && c.ChargeKind == ChargeKinds.Treatment, cancellationToken)
                : await db.Set<PatientCharge>().AnyAsync(c => c.VisitId == e.VisitId && c.ChargeKind == ChargeKinds.Consultation, cancellationToken);
            if (exists)
            {
                continue;
            }

            var charge = new PatientCharge
            {
                Id = Guid.CreateVersion7(),
                PatientId = e.PatientId,
                ChargeKind = isTreatment ? ChargeKinds.Treatment : ChargeKinds.Consultation,
                TreatmentCaseId = isTreatment ? line.TreatmentCaseId : null,
                CaseWorkItemId = isTreatment ? line.WorkItemId : null,
                VisitId = e.VisitId,
                PerformingDoctorId = isTreatment ? line.PerformingDoctorId : null,
                DoctorPercentageSnapshot = isTreatment ? line.DoctorPercentageSnapshot : null,
                LabModel = isTreatment ? line.LabModel : null,
                OriginalPrice = line.Price,
                DiscountAmount = line.Discount,
                DiscountSetByUserId = line.Discount > 0 ? line.DiscountSetByUserId : null,
                DiscountSetByRole = line.Discount > 0 ? line.DiscountSetByRole : null,
                DiscountSetAt = line.Discount > 0 ? line.DiscountSetAt : null,
                ChargedAt = e.FinalizedAt,
            };
            db.Set<PatientCharge>().Add(charge);
            audit.Add(new AuditEvent("finance.charge.established", "finance.patient_charges", charge.Id.ToString(),
                NewValue: new
                {
                    kind = charge.ChargeKind, visitId = e.VisitId, originalPrice = charge.OriginalPrice, discount = charge.DiscountAmount,
                    finalPrice = charge.OriginalPrice - charge.DiscountAmount, discountSetByRole = charge.DiscountSetByRole,
                }));
            established.Add(charge);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var charge in established)
        {
            var line = e.Charges.First(l => charge.ChargeKind == ChargeKinds.Treatment ? l.WorkItemId == charge.CaseWorkItemId : l.Kind == PricingLineKinds.Consultation);
            await events.PublishAsync(new ChargeEstablished(charge.Id, charge.PatientId, e.VisitId, charge.TreatmentCaseId, line.ProcedureCode,
                charge.OriginalPrice, charge.DiscountAmount, charge.OriginalPrice - charge.DiscountAmount, charge.ChargedAt, e.ActorUserId), cancellationToken);
        }
    }
}
