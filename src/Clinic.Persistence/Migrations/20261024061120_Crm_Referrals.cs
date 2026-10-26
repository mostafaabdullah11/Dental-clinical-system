using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Crm_Referrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "referrals",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referring_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referral_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    follow_up_instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    follow_up_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referrals", x => x.id);
                    table.CheckConstraint("ck_referrals_status", "follow_up_status IN ('New', 'Contacted', 'Booked', 'Completed', 'Lost')");
                    table.CheckConstraint("ck_referrals_type", "referral_type IN ('Surgery', 'Implant', 'Orthodontics', 'Prosthetics', 'Other')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_referral_id",
                schema: "crm",
                table: "follow_ups",
                column: "referral_id");

            migrationBuilder.CreateIndex(
                name: "ix_referrals_follow_up_status_created_at",
                schema: "crm",
                table: "referrals",
                columns: new[] { "follow_up_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_referrals_patient_id",
                schema: "crm",
                table: "referrals",
                column: "patient_id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_referral_referral_id",
                schema: "crm",
                table: "follow_ups",
                column: "referral_id",
                principalSchema: "crm",
                principalTable: "referrals",
                principalColumn: "id");
            migrationBuilder.Sql(MigrationSql.Read("Crm_Referrals.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_referral_referral_id",
                schema: "crm",
                table: "follow_ups");

            migrationBuilder.DropTable(
                name: "referrals",
                schema: "crm");

            migrationBuilder.DropIndex(
                name: "ix_follow_ups_referral_id",
                schema: "crm",
                table: "follow_ups");
        }
    }
}
