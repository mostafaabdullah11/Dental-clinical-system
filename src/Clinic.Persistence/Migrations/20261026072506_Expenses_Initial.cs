using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Expenses_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "expenses");

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sales_can_record = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clinic_expenses",
                schema: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    payment_method_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    paid_to_vendor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_expenses", x => x.id);
                    table.CheckConstraint("ck_clinic_expenses_amount", "amount > 0");
                    table.CheckConstraint("ck_clinic_expenses_role", "created_by_role IN ('MANAGER', 'SALES_RECEPTION')");
                    table.ForeignKey(
                        name: "fk_clinic_expenses_expense_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "expenses",
                        principalTable: "expense_categories",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_clinic_expenses_category_id_expense_date",
                schema: "expenses",
                table: "clinic_expenses",
                columns: new[] { "category_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "ix_clinic_expenses_expense_date",
                schema: "expenses",
                table: "clinic_expenses",
                column: "expense_date");

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_name",
                schema: "expenses",
                table: "expense_categories",
                column: "name",
                unique: true);

            migrationBuilder.Sql(MigrationSql.Read("Expenses_Initial.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinic_expenses",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "expenses");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS expenses.tg_clinic_expenses_sales_category();");
        }
    }
}
