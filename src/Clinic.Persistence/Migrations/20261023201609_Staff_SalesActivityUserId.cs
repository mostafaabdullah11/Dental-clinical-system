using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Staff_SalesActivityUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Read("Staff_SalesActivityUserId.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW staff.v_sales_employee_activity; CREATE VIEW staff.v_sales_employee_activity AS SELECT s.id AS sales_employee_id, s.is_active FROM staff.sales_employees s; GRANT SELECT ON staff.v_sales_employee_activity TO clinic_worker;");
        }
    }
}
