using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetPositionReportHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VesselPositionReports",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselPositionReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VesselPositionReports_VesselId_ReportedAtUtc_Sequence",
                schema: "fleet",
                table: "VesselPositionReports",
                columns: new[] { "VesselId", "ReportedAtUtc", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_VesselPositionReports_VesselId_Sequence",
                schema: "fleet",
                table: "VesselPositionReports",
                columns: new[] { "VesselId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VesselPositionReports",
                schema: "fleet");
        }
    }
}
