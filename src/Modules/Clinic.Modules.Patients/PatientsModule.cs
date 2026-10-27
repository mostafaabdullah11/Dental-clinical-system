using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Patients.Features;
using Clinic.Modules.Patients.Features.Doctor;
using Clinic.Modules.Patients.Features.Manager;
using Clinic.Modules.Patients.Features.Sales;
using Clinic.Modules.Patients.Features.Shared;
using Clinic.Modules.Patients.Infrastructure;
using Clinic.Modules.Patients.Infrastructure.Clinical;
using Clinic.Modules.Patients.Infrastructure.Contact;
using Clinic.Modules.Patients.Infrastructure.MedicalHistory;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Modules.Patients;

public static class PatientsModule
{
    public static IServiceCollection AddPatientsModule(this IServiceCollection services)
    {
        services.AddScoped<IPatientClinicalReader, PatientClinicalReader>();
        services.AddScoped<IPatientContactReader, PatientContactReader>();
        services.AddScoped<PatientRegistration>();
        services.AddScoped<IPatientRegistration>(sp => sp.GetRequiredService<PatientRegistration>());
        services.AddScoped<MedicalHistoryService>();
        services.AddScoped<PatientPresentation>();
        services.AddScoped<IPatientDirectory, PatientDirectory>();
        services.AddScoped<Clinic.Modules.Patients.Contracts.Contact.IPatientContactDirectory, PatientContactDirectory>();
        services.AddScoped<Clinic.Modules.Patients.Contracts.Contact.IPatientAttributionSource, PatientAttributionSource>();
        services.AddScoped<Clinic.Modules.Notifications.Contracts.INotificationSubjectReader, NotificationSubjectReader>();
        services.AddScoped<PatientContactViews>();

        // Ports implemented by later modules; these technical defaults return "not available" / "no Visit".
        services.TryAddScoped<IPatientCreditBalanceReader, FinanceNotAvailableCreditReader>();
        services.TryAddScoped<IEditableVisitPort, ClinicalNotAvailableVisitPort>();

        services.AddValidatorsFromAssemblyContaining<PatientSearchRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => PatientsModel.Configure(model);

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => DoctorPatientEndpoints.Map(doctor);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesPatientEndpoints.Map(sales);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => ManagerPatientEndpoints.Map(manager);
}
