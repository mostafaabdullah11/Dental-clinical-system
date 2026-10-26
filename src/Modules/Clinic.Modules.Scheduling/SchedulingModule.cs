using Clinic.Modules.Notifications.Contracts;
using Clinic.Modules.Patients.Contracts;
using Clinic.Modules.Scheduling.Contracts;
using Clinic.Modules.Scheduling.Features;
using Clinic.Modules.Scheduling.Features.Doctor;
using Clinic.Modules.Scheduling.Features.Manager;
using Clinic.Modules.Scheduling.Features.Sales;
using Clinic.Modules.Scheduling.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Scheduling;

public static class SchedulingModule
{
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        services.AddSchedulingJobs();
        services.AddScoped<AppointmentCommands>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<SalesAppointmentViews>();
        services.AddScoped<DoctorAppointmentViews>();
        services.AddScoped<IAppointmentLifecycle, AppointmentLifecycle>();
        services.AddScoped<IAppointmentBooking, AppointmentBooking>();
        services.AddScoped<IPatientTimelineSource, AppointmentTimelineSource>();
        services.AddValidatorsFromAssemblyContaining<BookAppointmentRequestValidator>(includeInternalTypes: true);
        return services;
    }

    /// <summary>The Worker's share of Scheduling: J1 and the J4 re-validation of appointment reminders.</summary>
    public static IServiceCollection AddSchedulingJobs(this IServiceCollection services)
    {
        services.AddScoped<ClinicCalendar>();
        services.AddScoped<IAppointmentReminderPlanner, AppointmentReminderPlanner>();
        services.AddScoped<INotificationSourceValidator, AppointmentNotificationValidator>();
        return services;
    }

    public static void ConfigureModel(ModelBuilder model) => SchedulingModel.Configure(model);

    public static void MapDoctorEndpoints(RouteGroupBuilder doctor) => DoctorScheduleEndpoints.Map(doctor);

    public static void MapSalesEndpoints(RouteGroupBuilder sales) => SalesAppointmentEndpoints.MapSales(sales);

    public static void MapManagerEndpoints(RouteGroupBuilder manager)
    {
        AvailabilityEndpoints.Map(manager);
        SalesAppointmentEndpoints.MapManager(manager);
    }
}
