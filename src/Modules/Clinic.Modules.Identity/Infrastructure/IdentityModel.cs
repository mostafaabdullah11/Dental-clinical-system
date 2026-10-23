using Clinic.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Modules.Identity.Infrastructure;

internal static class IdentityModel
{
    public const string Schema = "identity";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.ToTable("users", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(64);
            e.Property(x => x.NormalizedUserName).HasMaxLength(64);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<Role>(e =>
        {
            e.ToTable("roles", Schema, t => t.HasCheckConstraint(
                "ck_roles_name", "name IN ('MANAGER', 'DOCTOR', 'SALES_RECEPTION')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(40);
            e.HasIndex(x => x.Name).IsUnique();
        });

        model.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles", Schema);
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<Permission>(e =>
        {
            e.ToTable("permissions", Schema);
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(120);
        });

        model.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions", Schema);
            e.HasKey(x => new { x.RoleId, x.PermissionKey });
            e.Property(x => x.PermissionKey).HasMaxLength(120);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionKey).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
