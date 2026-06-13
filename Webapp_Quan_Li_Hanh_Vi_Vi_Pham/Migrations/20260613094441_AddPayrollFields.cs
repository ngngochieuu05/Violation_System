using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualWorkingDays",
                table: "PayrollRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SalaryPerDay",
                table: "PayrollRecords",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StandardWorkingDays",
                table: "PayrollRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualWorkingDays",
                table: "PayrollRecords");

            migrationBuilder.DropColumn(
                name: "SalaryPerDay",
                table: "PayrollRecords");

            migrationBuilder.DropColumn(
                name: "StandardWorkingDays",
                table: "PayrollRecords");
        }
    }
}
