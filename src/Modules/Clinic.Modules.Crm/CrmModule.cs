using Clinic.Modules.Clinical.Contracts;
using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Features;
using Clinic.Modules.Crm.Features.Manager;
using Clinic.Modules.Crm.Features.Referrals;
using Clinic.Modules.Crm.Features.Sales;
using Clinic.Modules.Crm.Infrastructure;
using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.SharedKernel.Events;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Crm;

public static class CrmModule
{
    public static IServiceCollection AddCrmModule(this IServiceCollection services)
    {
        services.AddScoped<CrmActor>();
        services.AddScoped<CrmViews>();
        services.AddScoped<OwnerResolver>();
        services.AddScoped<ResponsibilityWriter>();
        services.AddCrmJobs();
        services.AddScoped<IPatientTimelineSource, CrmTimelineSource>();
        services.AddScoped<SalesReferralViews>();
        services.AddScoped<IReferralSummarySource, ReferralSummarySource>();
        services.AddScoped<IPatientTimelineSource, ReferralTimelineSource>();

        // Completes the Step 2.1 seam: runs inside the Sales-deactivation transaction.
        services.AddScoped<IDomainEventHandler<SalesEmployeeDeactivated>, SalesDeactivationRedistribution>();

        services.AddValidatorsFromAssemblyContaining<CreateLeadRequestValidator>(includeInternalTypes: true);
        return services;
    }

    /// <summary>The Worker's share of Crm: J3, the J7 probe and the J4 re-validation of referral / follow-up notifications.</summary>
    public static IServiceCollection AddCrmJobs(this IServiceCollection services)
    {
        services.AddScoped<IFollowUpDuePlanner, FollowUpDuePlanner>();
        services.AddScoped<IOperationsHealthProbe, CrmOperationsHealthProbe>();
        services.AddScoped<INotificationSourceValidator, ReferralNotificationValidator>();
        services.AddScoped<INotificationSourceValidator, FollowUpNotificationValidator>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => CrmModel.Configure(model);

    public static void MapSalesEndpoints(RouteGroupBuilder sales)
    {
        SalesCrmEndpoints.Map(sales);
        SalesReferralEndpoints.Map(sales);
    }

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => DoctorReferralEndpoints.Map(doctor);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => ManagerCrmEndpoints.Map(manager);
}
