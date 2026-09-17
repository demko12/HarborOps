using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VesselStatusHistories_VesselId",
                schema: "fleet",
                table: "VesselStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_FleetAssignments_VesselId",
                schema: "fleet",
                table: "FleetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_VesselStatusHistories_VesselId_ChangedAtUtc_Id",
                schema: "fleet",
                table: "VesselStatusHistories",
                columns: new[] { "VesselId", "ChangedAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_CurrentStatus_Name_Id",
                schema: "fleet",
                table: "Vessels",
                columns: new[] { "CurrentStatus", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_Name_Id",
                schema: "fleet",
                table: "Vessels",
                columns: new[] { "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FleetAssignments_VesselId_AssignedFromUtc_Id",
                schema: "fleet",
                table: "FleetAssignments",
                columns: new[] { "VesselId", "AssignedFromUtc", "Id" },
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VesselStatusHistories_VesselId_ChangedAtUtc_Id",
                schema: "fleet",
                table: "VesselStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Vessels_CurrentStatus_Name_Id",
                schema: "fleet",
                table: "Vessels");

            migrationBuilder.DropIndex(
                name: "IX_Vessels_Name_Id",
                schema: "fleet",
                table: "Vessels");

            migrationBuilder.DropIndex(
                name: "IX_FleetAssignments_VesselId_AssignedFromUtc_Id",
                schema: "fleet",
                table: "FleetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_VesselStatusHistories_VesselId",
                schema: "fleet",
                table: "VesselStatusHistories",
                column: "VesselId");

            migrationBuilder.CreateIndex(
                name: "IX_FleetAssignments_VesselId",
                schema: "fleet",
                table: "FleetAssignments",
                column: "VesselId");
        }
    }
}
