using Clinic.Modules.Configuration.Domain;
using Clinic.SharedKernel.Errors;
using Clinic.SharedKernel.Persistence;
using Clinic.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Configuration.Features.Manager;

public sealed record PaymentMethodDto(string Code, string DisplayName, bool IsActive, string Version);

public sealed record UpdatePaymentMethodRequest(bool IsActive);

/// <summary>API A8 Settings: the seeded Cash / Visa / InstaPay catalogue. No method can be added or removed; only activated.</summary>
internal static class PaymentMethodEndpoints
{
    public static void Map(RouteGroupBuilder manager)
    {
        manager.MapGet("/payment-methods", ListAsync).RequirePermission(PermissionKeys.ConfigurationManage).Produces<List<PaymentMethodDto>>();
        manager.MapPut("/payment-methods/{code}", UpdateAsync).RequirePermission(PermissionKeys.ConfigurationManage).Produces<PaymentMethodDto>();
    }

    private static async Task<IResult> ListAsync(DbContext db, CancellationToken ct) =>
        Results.Ok(await db.Set<PaymentMethod>().AsNoTracking().OrderBy(m => m.Code).ToListAsync(ct) is var rows
            ? rows.Select(ToDto).ToList() : []);

    private static async Task<IResult> UpdateAsync(string code, UpdatePaymentMethodRequest body, HttpRequest request, DbContext db, IAuditWriter audit,
        CancellationToken ct)
    {
        var method = await db.Set<PaymentMethod>().SingleOrDefaultAsync(m => m.Code == code, ct) ?? throw AppException.NotFound();
        ETags.Require(request, method.RowVersion);
        if (method.IsActive != body.IsActive)
        {
            audit.Add(new AuditEvent("configuration.payment_method.changed", "configuration.payment_methods", code,
                PreviousValue: new { isActive = method.IsActive }, NewValue: new { isActive = body.IsActive }));
            method.IsActive = body.IsActive;
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(ToDto(method));
    }

    private static PaymentMethodDto ToDto(PaymentMethod m) => new(m.Code, m.DisplayName, m.IsActive, ETags.Format(m.RowVersion));
}
