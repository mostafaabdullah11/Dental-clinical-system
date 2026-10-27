using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Configuration.Features.Manager;
using Clinic.Modules.Configuration.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Configuration;

public static class ConfigurationModule
{
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services)
    {
        services.AddScoped<IClinicSettings, ClinicSettings>();
        services.AddScoped<IFinanceSettings, FinanceSettingsStore>();
        services.AddValidatorsFromAssemblyContaining<UpdateSettingsValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model, bool task4 = true) => ConfigurationModel.Configure(model, task4);

    public static void MapManagerEndpoints(RouteGroupBuilder manager)
    {
        SettingsEndpoints.Map(manager);
        PaymentMethodEndpoints.Map(manager);
    }
}
