using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseHub.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_RequesterId",
                table: "ReportRequests",
                column: "RequesterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRequests");
        }
    }
}
