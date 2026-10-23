using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Staff.Contracts;
using Clinic.Modules.Staff.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Events;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Staff.Features.Manager;

/// <summary>FR-STAFF-001…006, 009…011 (Manager only). No delete route exists for any staff record.</summary>
internal static class StaffEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        MapDoctors(manager);
        MapSalesEmployees(manager);
        MapAssistants(manager);
        MapActivation(manager);
    }

    private static void MapDoctors(RouteGroupBuilder manager)
    {
        manager.MapGet("/staff/doctors", async (DbContext db, IUserAccounts accounts, CancellationToken ct) =>
        {
            var doctors = await db.Set<Doctor>().AsNoTracking().OrderBy(d => d.FullName).ToListAsync(ct);
            var users = await accounts.FindManyAsync(doctors.Select(d => d.UserId), ct);
            return Results.Ok(doctors.Select(d => ToDto(d, users.GetValueOrDefault(d.UserId)?.UserName)));
        }).RequirePermission(PermissionKeys.StaffManage);

        manager.MapPost("/staff/doctors", async (CreateDoctorRequest body, IStaffProvisioning provisioning,
            DbContext db, IUserAccounts accounts, CancellationToken ct) =>
        {
            var created = await provisioning.AddDoctorAsync(
                new DoctorProfile(body.FullName, body.Specialty, body.CurrentPercentage!.Value),
                body.UserName, body.InitialPassword, ct);
            await db.SaveChangesAsync(ct);

            var doctor = await db.Set<Doctor>().AsNoTracking().SingleAsync(d => d.Id == created.StaffId, ct);
            var user = await accounts.FindAsync(created.UserId, ct);
            return Results.Created($"/api/manager/staff/doctors/{doctor.Id}", ToDto(doctor, user?.UserName));
        })
        .RequirePermission(PermissionKeys.StaffManage, PermissionKeys.IdentityUsersManage)
        .Validate<CreateDoctorRequest>();

        manager.MapPut("/doctors/{id:guid}/percentage", async (Guid id, ChangeDoctorPercentageRequest body,
            DbContext db, IUserAccounts accounts, IAuditWriter audit, CancellationToken ct) =>
        {
            var doctor = await db.Set<Doctor>().SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound();
            var previous = doctor.CurrentPercentage;

            // Affects only work items created afterwards (snapshot at work-item creation, FR-STAFF-002).
            doctor.CurrentPercentage = body.CurrentPercentage!.Value;
            audit.Add(new AuditEvent("staff.doctor.percentage_changed", "staff.doctors", id.ToString(),
                PreviousValue: new { currentPercentage = previous }, NewValue: new { currentPercentage = doctor.CurrentPercentage }));
            await db.SaveChangesAsync(ct);

            var user = await accounts.FindAsync(doctor.UserId, ct);
            return Results.Ok(ToDto(doctor, user?.UserName));
        })
        .RequirePermission(PermissionKeys.StaffDoctorPercentageConfigure)
        .Validate<ChangeDoctorPercentageRequest>();
    }

    private static void MapSalesEmployees(RouteGroupBuilder manager)
    {
        manager.MapGet("/staff/sales-employees", async (DbContext db, IUserAccounts accounts, CancellationToken ct) =>
        {
            var employees = await db.Set<SalesEmployee>().AsNoTracking().OrderBy(s => s.FullName).ToListAsync(ct);
            var rates = await CurrentRatesAsync(db, employees.Select(e => e.Id).ToList(), ct);
            var users = await accounts.FindManyAsync(employees.Select(e => e.UserId), ct);
            return Results.Ok(employees.Select(e => ToDto(e, rates.GetValueOrDefault(e.Id), users.GetValueOrDefault(e.UserId)?.UserName)));
        }).RequirePermission(PermissionKeys.StaffManage);

        manager.MapPost("/staff/sales-employees", async (CreateSalesEmployeeRequest body, IStaffProvisioning provisioning,
            DbContext db, IUserAccounts accounts, CancellationToken ct) =>
        {
            var created = await provisioning.AddSalesEmployeeAsync(
                new SalesEmployeeProfile(body.FullName, body.BaseMonthlySalary!.Value, body.CommissionPercentage!.Value),
                body.UserName, body.InitialPassword, ct);
            await db.SaveChangesAsync(ct);

            var employee = await db.Set<SalesEmployee>().AsNoTracking().SingleAsync(s => s.Id == created.StaffId, ct);
            var user = await accounts.FindAsync(created.UserId, ct);
            return Results.Created($"/api/manager/staff/sales-employees/{employee.Id}",
                ToDto(employee, body.CommissionPercentage.Value, user?.UserName));
        })
        .RequirePermission(PermissionKeys.StaffManage, PermissionKeys.StaffSalesCommissionConfigure, PermissionKeys.IdentityUsersManage)
        .Validate<CreateSalesEmployeeRequest>();

        manager.MapPut("/sales-employees/{id:guid}/commission", async (Guid id, ChangeCommissionRequest body,
            DbContext db, IUserAccounts accounts, IAuditWriter audit, ICurrentActor actor, CancellationToken ct) =>
        {
            var employee = await db.Set<SalesEmployee>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
                           ?? throw AppException.NotFound();
            var latest = await db.Set<SalesCommissionRate>().AsNoTracking().Where(r => r.SalesEmployeeId == id)
                .OrderByDescending(r => r.RevisionNumber).FirstAsync(ct);

            // Appends a revision effective at the database time of the save: future collections only (Q-COMM-1).
            db.Set<SalesCommissionRate>().Add(new SalesCommissionRate
            {
                Id = Guid.CreateVersion7(),
                SalesEmployeeId = id,
                RevisionNumber = latest.RevisionNumber + 1,
                CommissionPercentage = body.CommissionPercentage!.Value,
                SetByUserId = actor.UserId,
                Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            });
            audit.Add(new AuditEvent("staff.sales_employee.commission_rate_changed", "staff.sales_commission_rate_history",
                id.ToString(),
                PreviousValue: new { commissionPercentage = latest.CommissionPercentage, revisionNumber = latest.RevisionNumber },
                NewValue: new { commissionPercentage = body.CommissionPercentage.Value, revisionNumber = latest.RevisionNumber + 1 },
                Reason: body.Reason));
            await db.SaveChangesAsync(ct);

            var user = await accounts.FindAsync(employee.UserId, ct);
            return Results.Ok(ToDto(employee, body.CommissionPercentage.Value, user?.UserName));
        })
        .RequirePermission(PermissionKeys.StaffSalesCommissionConfigure)
        .Validate<ChangeCommissionRequest>();

        manager.MapGet("/sales-employees/{id:guid}/commission-history", async (Guid id, DbContext db, CancellationToken ct) =>
        {
            if (!await db.Set<SalesEmployee>().AnyAsync(s => s.Id == id, ct))
            {
                throw AppException.NotFound();
            }

            var history = await db.Set<SalesCommissionRate>().AsNoTracking().Where(r => r.SalesEmployeeId == id)
                .OrderBy(r => r.RevisionNumber)
                .Select(r => new CommissionRevisionDto(r.RevisionNumber, r.CommissionPercentage, r.EffectiveFrom, r.SetByUserId, r.Reason))
                .ToListAsync(ct);
            return Results.Ok(history);
        }).RequirePermission(PermissionKeys.StaffSalesCommissionConfigure);

        manager.MapPut("/sales-employees/{id:guid}/base-salary", async (Guid id, ChangeBaseSalaryRequest body,
            DbContext db, IUserAccounts accounts, IAuditWriter audit, CancellationToken ct) =>
        {
            var employee = await db.Set<SalesEmployee>().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw AppException.NotFound();
            var previous = employee.BaseMonthlySalary;

            // Later payroll drafts snapshot it; existing payroll snapshots never change (FR-STAFF-005).
            employee.BaseMonthlySalary = body.BaseMonthlySalary!.Value;
            audit.Add(new AuditEvent("staff.sales_employee.base_salary_changed", "staff.sales_employees", id.ToString(),
                PreviousValue: new { baseMonthlySalary = previous }, NewValue: new { baseMonthlySalary = employee.BaseMonthlySalary }));
            await db.SaveChangesAsync(ct);

            var rates = await CurrentRatesAsync(db, [id], ct);
            var user = await accounts.FindAsync(employee.UserId, ct);
            return Results.Ok(ToDto(employee, rates.GetValueOrDefault(id), user?.UserName));
        })
        .RequirePermission(PermissionKeys.PayrollSalesManageAll)
        .Validate<ChangeBaseSalaryRequest>();
    }

    private static void MapAssistants(RouteGroupBuilder manager)
    {
        var assistants = manager.MapGroup("/staff/assistants").RequirePermission(PermissionKeys.StaffManage);

        assistants.MapGet("/", async (DbContext db, CancellationToken ct) =>
            Results.Ok(await db.Set<Assistant>().AsNoTracking().OrderBy(a => a.FullName).Select(a => ToDto(a)).ToListAsync(ct)));

        assistants.MapGet("/{id:guid}", async (Guid id, HttpResponse response, DbContext db, CancellationToken ct) =>
        {
            var assistant = await db.Set<Assistant>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
                            ?? throw AppException.NotFound();
            ETags.Set(response, assistant.RowVersion);
            return Results.Ok(ToDto(assistant));
        });

        assistants.MapPost("/", async (CreateAssistantRequest body, HttpResponse response, DbContext db,
            IAuditWriter audit, ICurrentActor actor, IClock clock, CancellationToken ct) =>
        {
            // A staff record only: no account, login, role or user_id is ever created (FR-AUTH-001).
            var assistant = new Assistant
            {
                Id = Guid.CreateVersion7(),
                FullName = body.FullName.Trim(),
                CurrentSalary = body.CurrentSalary!.Value,
                IsActive = true,
                CreatedAt = clock.UtcNow,
                CreatedByUserId = actor.UserId,
            };
            db.Set<Assistant>().Add(assistant);
            audit.Add(new AuditEvent("staff.assistant.created", "staff.assistants", assistant.Id.ToString(),
                NewValue: new { assistant.FullName, assistant.CurrentSalary }));
            await db.SaveChangesAsync(ct);

            ETags.Set(response, assistant.RowVersion);
            return Results.Created($"/api/manager/staff/assistants/{assistant.Id}", ToDto(assistant));
        }).Validate<CreateAssistantRequest>();

        assistants.MapPut("/{id:guid}", async (Guid id, UpdateAssistantRequest body, HttpRequest request,
            HttpResponse response, DbContext db, IAuditWriter audit, CancellationToken ct) =>
        {
            var assistant = await db.Set<Assistant>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
            ETags.Require(request, assistant.RowVersion);

            var previous = assistant.FullName;
            assistant.FullName = body.FullName.Trim();
            audit.Add(new AuditEvent("staff.assistant.updated", "staff.assistants", id.ToString(),
                PreviousValue: new { fullName = previous }, NewValue: new { fullName = assistant.FullName }));
            await SaveWithConcurrencyAsync(db, ct);

            ETags.Set(response, assistant.RowVersion);
            return Results.Ok(ToDto(assistant));
        }).Validate<UpdateAssistantRequest>();
    }

    private static void MapActivation(RouteGroupBuilder manager)
    {
        manager.MapPost("/staff/{id:guid}/deactivate", DeactivateDoctorOrSalesAsync).RequirePermission(PermissionKeys.StaffManage);
        manager.MapPost("/staff/{id:guid}/reactivate", ReactivateDoctorOrSalesAsync).RequirePermission(PermissionKeys.StaffManage);
        manager.MapPost("/staff/assistants/{id:guid}/deactivate", DeactivateAssistantAsync).RequirePermission(PermissionKeys.StaffManage);
        manager.MapPost("/staff/assistants/{id:guid}/reactivate", ReactivateAssistantAsync).RequirePermission(PermissionKeys.StaffManage);
    }

    /// <summary>
    /// One transaction (SECURITY_DESIGN.md §8): staff inactive, user inactive with maximum lockout and a rotated
    /// stamp, audit, nothing deleted. A Doctor deactivation changes nothing financial. A Sales deactivation
    /// publishes <see cref="SalesEmployeeDeactivated"/> inside the transaction for the CRM redistribution.
    /// </summary>
    private static async Task<IResult> DeactivateDoctorOrSalesAsync(Guid id, DbContext db, IUserAccounts accounts,
        IDomainEventDispatcher events, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (await db.Set<Doctor>().SingleOrDefaultAsync(d => d.Id == id, ct) is { } doctor)
        {
            if (doctor.IsActive)
            {
                await accounts.DeactivateAsync(doctor.UserId, ct);
                doctor.IsActive = false;
                doctor.DeactivatedAt = now;
                audit.Add(new AuditEvent("staff.doctor.deactivated", "staff.doctors", id.ToString()));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new StaffStatusDto(id, StaffKinds.Doctor, doctor.IsActive, doctor.DeactivatedAt));
        }

        var employee = await db.Set<SalesEmployee>().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw AppException.NotFound();
        if (employee.IsActive)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await accounts.DeactivateAsync(employee.UserId, ct);
            employee.IsActive = false;
            employee.DeactivatedAt = now;
            audit.Add(new AuditEvent("staff.sales_employee.deactivated", "staff.sales_employees", id.ToString()));
            await events.PublishAsync(new SalesEmployeeDeactivated(id, employee.UserId, now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return Results.Ok(new StaffStatusDto(id, StaffKinds.SalesEmployee, employee.IsActive, employee.DeactivatedAt));
    }

    /// <summary>
    /// Reactivation (FR-STAFF-010): active again with a rotated stamp; the Manager must set a new password before
    /// login. Redistributed CRM work is not restored and no retroactive commission is created.
    /// </summary>
    private static async Task<IResult> ReactivateDoctorOrSalesAsync(Guid id, DbContext db, IUserAccounts accounts,
        IAuditWriter audit, CancellationToken ct)
    {
        if (await db.Set<Doctor>().SingleOrDefaultAsync(d => d.Id == id, ct) is { } doctor)
        {
            if (!doctor.IsActive)
            {
                await accounts.ReactivateAsync(doctor.UserId, ct);
                doctor.IsActive = true;
                doctor.DeactivatedAt = null;
                audit.Add(new AuditEvent("staff.doctor.reactivated", "staff.doctors", id.ToString()));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new StaffStatusDto(id, StaffKinds.Doctor, doctor.IsActive, doctor.DeactivatedAt));
        }

        var employee = await db.Set<SalesEmployee>().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw AppException.NotFound();
        if (!employee.IsActive)
        {
            await accounts.ReactivateAsync(employee.UserId, ct);
            employee.IsActive = true;
            employee.DeactivatedAt = null;
            audit.Add(new AuditEvent("staff.sales_employee.reactivated", "staff.sales_employees", id.ToString()));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new StaffStatusDto(id, StaffKinds.SalesEmployee, employee.IsActive, employee.DeactivatedAt));
    }

    private static async Task<IResult> DeactivateAssistantAsync(Guid id, DbContext db, IAuditWriter audit, IClock clock, CancellationToken ct)
    {
        var assistant = await db.Set<Assistant>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        if (assistant.IsActive)
        {
            assistant.IsActive = false;
            assistant.DeactivatedAt = clock.UtcNow;
            audit.Add(new AuditEvent("staff.assistant.deactivated", "staff.assistants", id.ToString()));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new StaffStatusDto(id, StaffKinds.Assistant, assistant.IsActive, assistant.DeactivatedAt));
    }

    private static async Task<IResult> ReactivateAssistantAsync(Guid id, DbContext db, IAuditWriter audit, CancellationToken ct)
    {
        var assistant = await db.Set<Assistant>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound();
        if (!assistant.IsActive)
        {
            assistant.IsActive = true;
            assistant.DeactivatedAt = null;
            audit.Add(new AuditEvent("staff.assistant.reactivated", "staff.assistants", id.ToString()));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new StaffStatusDto(id, StaffKinds.Assistant, assistant.IsActive, assistant.DeactivatedAt));
    }

    private static async Task<Dictionary<Guid, decimal>> CurrentRatesAsync(DbContext db, List<Guid> employeeIds, CancellationToken ct)
    {
        var rows = await db.Set<SalesCommissionRate>().AsNoTracking()
            .Where(r => employeeIds.Contains(r.SalesEmployeeId))
            .Select(r => new { r.SalesEmployeeId, r.RevisionNumber, r.CommissionPercentage })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SalesEmployeeId)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.RevisionNumber)!.CommissionPercentage);
    }

    private static async Task SaveWithConcurrencyAsync(DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Stale();
        }
    }

    private static ManagerDoctorDto ToDto(Doctor d, string? userName) =>
        new(d.Id, d.UserId, userName, d.FullName, d.Specialty, d.CurrentPercentage, d.IsActive, d.DeactivatedAt);

    private static ManagerSalesEmployeeDto ToDto(SalesEmployee s, decimal rate, string? userName) =>
        new(s.Id, s.UserId, userName, s.FullName, s.BaseMonthlySalary, rate, s.IsActive, s.DeactivatedAt);

    private static ManagerAssistantDto ToDto(Assistant a) =>
        new(a.Id, a.FullName, a.CurrentSalary, a.OvertimeHourlyRate, a.IsActive, a.DeactivatedAt);
}
