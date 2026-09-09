using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoyageEstimateRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RevisedEta",
                schema: "voyages",
                table: "VoyageStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisedEtd",
                schema: "voyages",
                table: "VoyageStops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VoyageEstimateRevisionBatches",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RevisedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoyageEstimateRevisionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoyageEstimateRevisionBatches_Delays_DelayId",
                        column: x => x.DelayId,
                        principalSchema: "voyages",
                        principalTable: "Delays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoyageEstimateRevisionBatches_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoyageEstimateRevisionItems",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousEta = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousEtd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewEta = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewEtd = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoyageEstimateRevisionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoyageEstimateRevisionItems_VoyageEstimateRevisionBatches_RevisionBatchId",
                        column: x => x.RevisionBatchId,
                        principalSchema: "voyages",
                        principalTable: "VoyageEstimateRevisionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoyageEstimateRevisionItems_VoyageStops_VoyageStopId",
                        column: x => x.VoyageStopId,
                        principalSchema: "voyages",
                        principalTable: "VoyageStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoyageEstimateRevisionBatches_DelayId",
                schema: "voyages",
                table: "VoyageEstimateRevisionBatches",
                column: "DelayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoyageEstimateRevisionBatches_VoyageId_RevisedAtUtc",
                schema: "voyages",
                table: "VoyageEstimateRevisionBatches",
                columns: new[] { "VoyageId", "RevisedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VoyageEstimateRevisionItems_RevisionBatchId_VoyageStopId",
                schema: "voyages",
                table: "VoyageEstimateRevisionItems",
                columns: new[] { "RevisionBatchId", "VoyageStopId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoyageEstimateRevisionItems_VoyageStopId",
                schema: "voyages",
                table: "VoyageEstimateRevisionItems",
                column: "VoyageStopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoyageEstimateRevisionItems",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "VoyageEstimateRevisionBatches",
                schema: "voyages");

            migrationBuilder.DropColumn(
                name: "RevisedEta",
                schema: "voyages",
                table: "VoyageStops");

            migrationBuilder.DropColumn(
                name: "RevisedEtd",
                schema: "voyages",
                table: "VoyageStops");
        }
    }
}
