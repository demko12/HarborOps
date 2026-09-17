using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoyageListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Voyages_PlannedDeparture_Id",
                schema: "voyages",
                table: "Voyages",
                columns: new[] { "PlannedDeparture", "Id" })
                .Annotation("SqlServer:Include", new[] { "VoyageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Voyages_Status_PlannedDeparture_Id",
                schema: "voyages",
                table: "Voyages",
                columns: new[] { "Status", "PlannedDeparture", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Voyages_PlannedDeparture_Id",
                schema: "voyages",
                table: "Voyages");

            migrationBuilder.DropIndex(
                name: "IX_Voyages_Status_PlannedDeparture_Id",
                schema: "voyages",
                table: "Voyages");
        }
    }
}
