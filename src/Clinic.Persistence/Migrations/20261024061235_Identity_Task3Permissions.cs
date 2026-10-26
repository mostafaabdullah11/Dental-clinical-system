using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_Task3Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Read("Identity_Task3Permissions.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Scheduling.%'; DELETE FROM identity.permissions WHERE key LIKE 'Scheduling.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Clinical.%'; DELETE FROM identity.permissions WHERE key LIKE 'Clinical.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Finance.Discount.%'; DELETE FROM identity.permissions WHERE key LIKE 'Finance.Discount.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Finance.PricingLines.%'; DELETE FROM identity.permissions WHERE key LIKE 'Finance.PricingLines.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Finance.FixedLab.%'; DELETE FROM identity.permissions WHERE key LIKE 'Finance.FixedLab.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Orthodontics.%'; DELETE FROM identity.permissions WHERE key LIKE 'Orthodontics.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Prescriptions.%'; DELETE FROM identity.permissions WHERE key LIKE 'Prescriptions.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Crm.Referrals.%'; DELETE FROM identity.permissions WHERE key LIKE 'Crm.Referrals.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Feedback.%'; DELETE FROM identity.permissions WHERE key LIKE 'Feedback.%'; DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Notifications.%'; DELETE FROM identity.permissions WHERE key LIKE 'Notifications.%';");
        }
    }
}
