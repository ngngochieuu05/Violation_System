using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComplaintReason",
                table: "ViolationRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ComplaintSubmittedAtUtc",
                table: "ViolationRecords",
                type: "datetime2",
                nullable: true);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplaintReason",
                table: "ViolationRecords");

            migrationBuilder.DropColumn(
                name: "ComplaintSubmittedAtUtc",
                table: "ViolationRecords");


        }
    }
}
