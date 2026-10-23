using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_TimelinePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Read("Identity_TimelinePermissions.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identity.role_permissions WHERE permission_key LIKE 'Patients.Timeline.%'; DELETE FROM identity.permissions WHERE key LIKE 'Patients.Timeline.%';");
        }
    }
}
