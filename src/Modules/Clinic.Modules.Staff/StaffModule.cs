using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Features.Manager;
using Clinic.Modules.Staff.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Staff;

public static class StaffModule
{
    public static IServiceCollection AddStaffModule(this IServiceCollection services)
    {
        services.AddScoped<IStaffProvisioning, StaffProvisioning>();
        services.AddScoped<IStaffLinkInspector, StaffLinkInspector>();
        services.AddScoped<IStaffDirectory, StaffDirectory>();
        services.AddScoped<IDoctorDirectory, DoctorDirectory>();
        services.AddScoped<ICurrentStaff, CurrentStaff>();
        services.AddStaffDirectoriesForJobs();
        services.AddValidatorsFromAssemblyContaining<CreateDoctorRequestValidator>(includeInternalTypes: true);
        return services;
    }

    /// <summary>The Worker's share of Staff: active Sales recipients (through the activity view only).</summary>
    public static IServiceCollection AddStaffDirectoriesForJobs(this IServiceCollection services)
    {
        services.AddScoped<Clinic.Modules.Notifications.Contracts.INotificationRecipientDirectory, NotificationRecipientDirectory>();
        services.AddScoped<IActiveSalesEmployeeDirectory, ActiveSalesEmployeeDirectory>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => StaffModel.Configure(model);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => StaffEndpoints.Map(manager);
}
