using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Prescriptions.Domain;
using Clinic.Modules.Prescriptions.Features.Doctor;
using Clinic.Modules.Prescriptions.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Prescriptions;

public static class PrescriptionsModule
{
    public static IServiceCollection AddPrescriptionsModule(this IServiceCollection services)
    {
        services.AddScoped<PrescriptionAccess>();
        services.AddScoped<PrescriptionViews>();
        services.AddScoped<IPatientTimelineSource, PrescriptionTimelineSource>();
        services.AddValidatorsFromAssemblyContaining<SavePrescriptionRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => PrescriptionsModel.Configure(model);

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => PrescriptionEndpoints.Map(doctor);
}

/// <summary>Prescriptions for the clinical and full timelines; never in the operational (Sales) timeline (FR-PAT-010).</summary>
internal sealed class PrescriptionTimelineSource(DbContext db) : IPatientTimelineSource
{
    public async Task<IReadOnlyList<PatientTimelineEntry>> GetEntriesAsync(Guid patientId, string audience, Guid? viewerUserId,
        CancellationToken cancellationToken)
    {
        if (audience == TimelineAudiences.Operational)
        {
            return [];
        }

        return await db.Set<Prescription>().AsNoTracking().Where(p => p.PatientId == patientId && p.VoidedAt == null)
            .Select(p => new PatientTimelineEntry("Prescription", null, p.CreatedAt, null, p.Id, null)).ToListAsync(cancellationToken);
    }
}
