using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Configuration_Task4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lab_percentage_history",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    set_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_percentage_history", x => x.id);
                    table.CheckConstraint("ck_lab_percentage_history_percentage", "percentage BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "configuration",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.code);
                    table.CheckConstraint("ck_payment_methods_code", "code IN ('Cash', 'Visa', 'InstaPay')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lab_percentage_history_effective_from",
                schema: "configuration",
                table: "lab_percentage_history",
                column: "effective_from",
                unique: true);

            migrationBuilder.Sql(MigrationSql.Read("Configuration_Task4.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_percentage_history",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "configuration");
        }
    }
}
