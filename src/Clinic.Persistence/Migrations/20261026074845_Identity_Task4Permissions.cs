using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinic.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_Task4Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Read("Identity_Task4Permissions.up.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identity.role_permissions WHERE permission_key IN ('Finance.Payments.Record', 'Finance.PatientBalance.Read', 'Finance.PatientCredit.Apply', 'Finance.OwnWork.Read', 'Finance.Payments.Correct', 'Finance.Charges.Adjust', 'Finance.LabPercentage.Configure', 'Finance.FixedLab.Revise', 'Finance.Management.Read', 'Orthodontics.Terms.Edit', 'Orthodontics.Installments.Edit', 'Orthodontics.Installments.ApplyPayment', 'Expenses.CreateOperational', 'Expenses.Categories.ReadAllowed', 'Expenses.ReadAll', 'Expenses.Manage', 'ExpenseCategories.Manage', 'Staff.AssistantSalary.Configure', 'Staff.AssistantOvertimeRate.Configure', 'Staff.AssistantOvertime.Manage', 'Staff.Attendance.Manage', 'Payroll.Doctor.ReadOwn', 'Payroll.Doctor.ManageAll', 'Payroll.Sales.ReadOwn', 'Payroll.Assistant.ManageAll', 'Reports.Financial', 'Reports.Attendance', 'Reports.Export', 'Reports.Dashboard.Sales', 'Reports.Dashboard.Manager'); DELETE FROM identity.permissions WHERE key IN ('Finance.Payments.Record', 'Finance.PatientBalance.Read', 'Finance.PatientCredit.Apply', 'Finance.OwnWork.Read', 'Finance.Payments.Correct', 'Finance.Charges.Adjust', 'Finance.LabPercentage.Configure', 'Finance.FixedLab.Revise', 'Finance.Management.Read', 'Orthodontics.Terms.Edit', 'Orthodontics.Installments.Edit', 'Orthodontics.Installments.ApplyPayment', 'Expenses.CreateOperational', 'Expenses.Categories.ReadAllowed', 'Expenses.ReadAll', 'Expenses.Manage', 'ExpenseCategories.Manage', 'Staff.AssistantSalary.Configure', 'Staff.AssistantOvertimeRate.Configure', 'Staff.AssistantOvertime.Manage', 'Staff.Attendance.Manage', 'Payroll.Doctor.ReadOwn', 'Payroll.Doctor.ManageAll', 'Payroll.Sales.ReadOwn', 'Payroll.Assistant.ManageAll', 'Reports.Financial', 'Reports.Attendance', 'Reports.Export', 'Reports.Dashboard.Sales', 'Reports.Dashboard.Manager');");
        }
    }
}
