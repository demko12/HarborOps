using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoyageScheduleChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoyageScheduleChanges",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PreviousRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousDeparturePortId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewDeparturePortId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousArrivalPortId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewArrivalPortId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousPlannedDeparture = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewPlannedDeparture = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousPlannedArrival = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewPlannedArrival = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoyageScheduleChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Ports_NewArrivalPortId",
                        column: x => x.NewArrivalPortId,
                        principalSchema: "voyages",
                        principalTable: "Ports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Ports_NewDeparturePortId",
                        column: x => x.NewDeparturePortId,
                        principalSchema: "voyages",
                        principalTable: "Ports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Ports_PreviousArrivalPortId",
                        column: x => x.PreviousArrivalPortId,
                        principalSchema: "voyages",
                        principalTable: "Ports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Ports_PreviousDeparturePortId",
                        column: x => x.PreviousDeparturePortId,
                        principalSchema: "voyages",
                        principalTable: "Ports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Routes_NewRouteId",
                        column: x => x.NewRouteId,
                        principalSchema: "voyages",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Routes_PreviousRouteId",
                        column: x => x.PreviousRouteId,
                        principalSchema: "voyages",
                        principalTable: "Routes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageScheduleChanges_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_NewArrivalPortId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "NewArrivalPortId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_NewDeparturePortId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "NewDeparturePortId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_NewRouteId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "NewRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_PreviousArrivalPortId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "PreviousArrivalPortId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_PreviousDeparturePortId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "PreviousDeparturePortId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_PreviousRouteId",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                column: "PreviousRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_VoyageScheduleChanges_VoyageId_ChangedAtUtc",
                schema: "voyages",
                table: "VoyageScheduleChanges",
                columns: new[] { "VoyageId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoyageScheduleChanges",
                schema: "voyages");
        }
    }
}
