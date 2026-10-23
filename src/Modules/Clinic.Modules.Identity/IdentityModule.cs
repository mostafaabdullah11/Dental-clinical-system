using Clinic.Modules.Identity.Auth;
using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Features.Auth;
using Clinic.Modules.Identity.Features.Manager;
using Clinic.Modules.Identity.Infrastructure;
using Clinic.Modules.Identity.Operator;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddSingleton<PasswordHashing>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddSingleton<IPermissionCatalog, PermissionCatalog>();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped(sp => new OperatorCommands(
            sp.GetRequiredService<DbContext>(),
            sp.GetRequiredService<IUserAccounts>(),
            sp.GetRequiredService<IPasswordPolicy>(),
            sp.GetRequiredService<SharedKernel.Persistence.IAuditWriter>(),
            sp.GetRequiredService<PasswordHashing>()));
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(includeInternalTypes: true);
        services.AddOptions<LockoutOptions>().BindConfiguration("Security:Lockout");
        services.AddOptions<SessionOptions>().BindConfiguration("Security:Session");
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => IdentityModel.Configure(model);

    public static void MapAuthEndpoints(IEndpointRouteBuilder app) => AuthEndpoints.Map(app);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => UserEndpoints.Map(manager);
}
