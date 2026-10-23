using Clinic.Modules.Identity.Contracts;
using Clinic.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Modules.Identity.Infrastructure;

/// <summary>
/// Role → permission map loaded from identity.role_permissions and cached for the process: permissions
/// change only through reviewed migrations (ADR-006), never at runtime. One role per account, no union.
/// </summary>
internal sealed class PermissionCatalog(IServiceScopeFactory scopes) : IPermissionCatalog
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Snapshot? snapshot;

    public async Task<IReadOnlyCollection<string>> PermissionsOfAsync(string role, CancellationToken cancellationToken)
    {
        var map = await LoadAsync(cancellationToken);
        return map.ByRole.TryGetValue(role, out var keys) ? keys : [];
    }

    public async Task<bool> RoleHasAsync(string role, string permissionKey, CancellationToken cancellationToken)
    {
        var map = await LoadAsync(cancellationToken);
        return map.ByRole.TryGetValue(role, out var keys) && keys.Contains(permissionKey);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> KeyRolesAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).ByKey;

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (snapshot is not null)
        {
            return snapshot;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is not null)
            {
                return snapshot;
            }

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            var keys = await db.Set<Permission>().AsNoTracking().Select(p => p.Key).ToListAsync(cancellationToken);
            var mappings = await (
                from rp in db.Set<RolePermission>().AsNoTracking()
                join r in db.Set<Role>().AsNoTracking() on rp.RoleId equals r.Id
                select new { Role = r.Name, rp.PermissionKey }).ToListAsync(cancellationToken);

            var byRole = mappings.GroupBy(m => m.Role)
                .ToDictionary(g => g.Key, g => g.Select(m => m.PermissionKey).ToHashSet(StringComparer.Ordinal));
            var byKey = keys.ToDictionary(k => k,
                k => (IReadOnlyCollection<string>)mappings.Where(m => m.PermissionKey == k).Select(m => m.Role).ToList());

            snapshot = new Snapshot(byRole, byKey);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record Snapshot(
        Dictionary<string, HashSet<string>> ByRole,
        Dictionary<string, IReadOnlyCollection<string>> ByKey);
}
