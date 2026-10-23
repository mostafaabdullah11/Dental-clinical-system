using Clinic.Modules.Audit.Features.Manager;
using Clinic.Modules.Audit.Infrastructure;
using Clinic.SharedKernel.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        return services;
    }

    /// <summary>The SaveChanges interceptor that writes level-A audit rows.</summary>
    public static IInterceptor ResolveInterceptor(IServiceProvider services) =>
        services.GetRequiredService<AuditSaveChangesInterceptor>();

    public static void ConfigureModel(ModelBuilder model) => AuditModel.Configure(model);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => AuditEndpoints.Map(manager);
}
