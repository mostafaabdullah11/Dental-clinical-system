using Clinic.Modules.Payroll.Features;
using Clinic.Modules.Payroll.Infrastructure;
using Clinic.Modules.Staff.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clinic.Modules.Payroll;

public static class PayrollModule
{
    public static IServiceCollection AddPayrollModule(this IServiceCollection services)
    {
        services.AddScoped<PayrollCalendar>();
        services.AddScoped<DoctorPayrollService>();
        services.AddScoped<SalesPayrollService>();
        services.AddScoped<AssistantPayrollService>();
        services.AddScoped<PayrollViews>();
        services.Replace(ServiceDescriptor.Scoped<IOvertimeUsageCheck, OvertimeUsageCheck>());
        services.AddValidatorsFromAssemblyContaining<DoctorDraftRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => PayrollModel.Configure(model);

    public static void MapManagerEndpoints(RouteGroupBuilder manager) => PayrollEndpoints.MapManager(manager);

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => PayrollEndpoints.MapDoctor(doctor);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => PayrollEndpoints.MapSales(sales);
}
