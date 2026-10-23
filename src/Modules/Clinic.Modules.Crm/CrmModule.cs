using Clinic.Modules.Crm.Contracts;
using Clinic.Modules.Crm.Features;
using Clinic.Modules.Crm.Features.Manager;
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
        services.AddScoped<IFollowUpDuePlanner, FollowUpDuePlanner>();
        services.AddScoped<IOperationsHealthProbe, CrmOperationsHealthProbe>();
        services.AddScoped<IPatientTimelineSource, CrmTimelineSource>();

        // Completes the Step 2.1 seam: runs inside the Sales-deactivation transaction.
        services.AddScoped<IDomainEventHandler<SalesEmployeeDeactivated>, SalesDeactivationRedistribution>();

        services.AddValidatorsFromAssemblyContaining<CreateLeadRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => CrmModel.Configure(model);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesCrmEndpoints.Map(sales);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => ManagerCrmEndpoints.Map(manager);
}
