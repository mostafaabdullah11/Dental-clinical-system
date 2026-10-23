using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clinic.Modules.Configuration.Contracts;
using Clinic.Modules.Configuration.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Clinic.SharedKernel.Time;
using Clinic.SharedKernel.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Features.Manager;

public sealed record ManagerSettingsDto(string ClinicTimezone, bool DoctorDobVisible, string ClinicName, string ClinicContact);

public sealed record UpdateSettingsRequest(string ClinicTimezone, bool DoctorDobVisible, string ClinicName, string ClinicContact);

internal sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsValidator()
    {
        RuleFor(x => x.ClinicTimezone).NotEmpty()
            .Must(ClinicTimeZones.IsValidIana).WithErrorCode(FieldErrorCodes.Invalid);
        RuleFor(x => x.ClinicName).NotNull().MaximumLength(200);
        RuleFor(x => x.ClinicContact).NotNull().MaximumLength(500);
    }
}

/// <summary>FR-OPS-003: Manager-only settings; the settings resource uses one ETag over all rows.</summary>
internal static class SettingsEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        var group = manager.MapGroup("/settings").RequirePermission(PermissionKeys.ConfigurationManage);

        group.MapGet("/", async (HttpResponse response, DbContext db, CancellationToken ct) =>
        {
            var rows = await Load(db, ct);
            response.Headers.ETag = ETagOf(rows);
            return Results.Ok(ToDto(rows));
        });

        group.MapPut("/", async (UpdateSettingsRequest body, HttpRequest request, HttpResponse response,
            DbContext db, ICurrentActor actor, IClock clock, CancellationToken ct) =>
        {
            var rows = await Load(db, ct);
            ETags.Require(request, ETagOf(rows));

            Set(rows, SettingKeys.ClinicTimezone, body.ClinicTimezone, actor, clock);
            Set(rows, SettingKeys.DoctorDobVisible, body.DoctorDobVisible ? "true" : "false", actor, clock);
            Set(rows, SettingKeys.ClinicName, body.ClinicName, actor, clock);
            Set(rows, SettingKeys.ClinicContact, body.ClinicContact, actor, clock);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw AppException.Stale();
            }

            response.Headers.ETag = ETagOf(rows);
            return Results.Ok(ToDto(rows));
        }).Validate<UpdateSettingsRequest>();
    }

    private static async Task<Dictionary<string, Setting>> Load(DbContext db, CancellationToken ct) =>
        await db.Set<Setting>().ToDictionaryAsync(s => s.Key, ct);

    private static void Set(Dictionary<string, Setting> rows, string key, string value, ICurrentActor actor, IClock clock)
    {
        var row = rows.GetValueOrDefault(key) ?? throw new InvalidOperationException($"Setting '{key}' is not seeded.");
        if (row.Value == value)
        {
            return;
        }

        row.Value = value;
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedAt = clock.UtcNow;
    }

    private static ManagerSettingsDto ToDto(Dictionary<string, Setting> rows) => new(
        rows[SettingKeys.ClinicTimezone].Value,
        rows[SettingKeys.DoctorDobVisible].Value == "true",
        rows[SettingKeys.ClinicName].Value,
        rows[SettingKeys.ClinicContact].Value);

    private static string ETagOf(Dictionary<string, Setting> rows)
    {
        var text = string.Join(';', rows.Values.OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => r.Key + ":" + r.RowVersion.ToString(CultureInfo.InvariantCulture)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"\"s-{Convert.ToHexStringLower(hash.AsSpan(0, 8))}\"";
    }
}
