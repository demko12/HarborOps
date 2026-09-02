using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "OperationAlerts",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Capability = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CrewMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ThresholdDays = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Acknowledgements",
                schema: "notifications",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acknowledgements", x => new { x.AlertId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Acknowledgements_OperationAlerts_AlertId",
                        column: x => x.AlertId,
                        principalSchema: "notifications",
                        principalTable: "OperationAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Acknowledgements_UserId",
                schema: "notifications",
                table: "Acknowledgements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationAlerts_Capability_ExpiryDate",
                schema: "notifications",
                table: "OperationAlerts",
                columns: new[] { "Capability", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationAlerts_SourceId_ExpiryDate_ThresholdDays",
                schema: "notifications",
                table: "OperationAlerts",
                columns: new[] { "SourceId", "ExpiryDate", "ThresholdDays" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Acknowledgements",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "OperationAlerts",
                schema: "notifications");
        }
    }
}
