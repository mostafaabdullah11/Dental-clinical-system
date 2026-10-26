using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Clinical.Features.Doctor;
using Clinic.Modules.Clinical.Features.Sales;
using Clinic.Modules.Clinical.Infrastructure;
using Clinic.Modules.Patients.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Modules.Clinical;

public static class ClinicalModule
{
    public static IServiceCollection AddClinicalModule(this IServiceCollection services)
    {
        services.AddScoped<ClinicalActor>();
        services.AddScoped<CorrectionRecorder>();
        services.AddScoped<PricingService>();
        services.AddScoped<TreatmentService>();
        services.AddScoped<VisitService>();
        services.AddScoped<DoctorClinicalViews>();
        services.AddScoped<IClinicalVisitAccess, ClinicalVisitAccess>();
        services.AddScoped<IPatientTimelineSource, ClinicalTimelineSource>();

        // Completes the Patients port (FR-PAT-008): replaces the "no Visit yet" default registered by Patients.
        services.Replace(ServiceDescriptor.Scoped<IEditableVisitPort, EditableVisitPort>());

        // Port implemented by CRM (referral fields for the Sales summary); empty until CRM registers it.
        services.TryAddScoped<IReferralSummarySource, NoReferralSummaries>();

        services.AddValidatorsFromAssemblyContaining<StartVisitRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => ClinicalModel.Configure(model);

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => DoctorClinicalEndpoints.Map(doctor);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesClinicalEndpoints.Map(sales);
}
