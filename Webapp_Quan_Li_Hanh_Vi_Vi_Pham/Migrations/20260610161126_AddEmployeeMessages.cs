using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Migrations
{
    public partial class AddEmployeeMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CHỈ TẠO DUY NHẤT BẢNG EmployeeMessages
            migrationBuilder.CreateTable(
                name: "EmployeeMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeMessages", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NẾU REVERT (ROLLBACK) THÌ CHỈ XÓA BẢNG EmployeeMessages
            migrationBuilder.DropTable(
                name: "EmployeeMessages");
        }
    }
}