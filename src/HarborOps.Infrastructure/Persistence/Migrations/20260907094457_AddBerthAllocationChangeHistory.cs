using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBerthAllocationChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BerthSchedules_BerthId",
                schema: "voyages",
                table: "BerthSchedules");

            migrationBuilder.CreateTable(
                name: "BerthAllocationChanges",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousBerthId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewBerthId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousPlannedArrivalUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousPlannedDepartureUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewPlannedArrivalUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewPlannedDepartureUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BerthAllocationChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BerthAllocationChanges_Berths_NewBerthId",
                        column: x => x.NewBerthId,
                        principalSchema: "voyages",
                        principalTable: "Berths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BerthAllocationChanges_Berths_PreviousBerthId",
                        column: x => x.PreviousBerthId,
                        principalSchema: "voyages",
                        principalTable: "Berths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BerthAllocationChanges_VoyageStops_VoyageStopId",
                        column: x => x.VoyageStopId,
                        principalSchema: "voyages",
                        principalTable: "VoyageStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BerthAllocationChanges_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BerthSchedules_BerthId_Status_StartTime_EndTime",
                schema: "voyages",
                table: "BerthSchedules",
                columns: new[] { "BerthId", "Status", "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_BerthAllocationChanges_NewBerthId",
                schema: "voyages",
                table: "BerthAllocationChanges",
                column: "NewBerthId");

            migrationBuilder.CreateIndex(
                name: "IX_BerthAllocationChanges_PreviousBerthId",
                schema: "voyages",
                table: "BerthAllocationChanges",
                column: "PreviousBerthId");

            migrationBuilder.CreateIndex(
                name: "IX_BerthAllocationChanges_VoyageId",
                schema: "voyages",
                table: "BerthAllocationChanges",
                column: "VoyageId");

            migrationBuilder.CreateIndex(
                name: "IX_BerthAllocationChanges_VoyageStopId_ChangedAtUtc",
                schema: "voyages",
                table: "BerthAllocationChanges",
                columns: new[] { "VoyageStopId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BerthAllocationChanges",
                schema: "voyages");

            migrationBuilder.DropIndex(
                name: "IX_BerthSchedules_BerthId_Status_StartTime_EndTime",
                schema: "voyages",
                table: "BerthSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_BerthSchedules_BerthId",
                schema: "voyages",
                table: "BerthSchedules",
                column: "BerthId");
        }
    }
}
