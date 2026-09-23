using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrewVesselManningPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VesselManningPlanHistories",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselManningPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousEffectiveEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewEffectiveEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PreviousLinesSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewLinesSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselManningPlanHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VesselManningPlans",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselManningPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VesselManningPlanLines",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselManningPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RankId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredHeadcount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselManningPlanLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VesselManningPlanLines_CrewPositions_RankId",
                        column: x => x.RankId,
                        principalSchema: "crew",
                        principalTable: "CrewPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VesselManningPlanLines_VesselManningPlans_VesselManningPlanId",
                        column: x => x.VesselManningPlanId,
                        principalSchema: "crew",
                        principalTable: "VesselManningPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VesselManningPlanHistories_VesselManningPlanId_OccurredAtUtc",
                schema: "crew",
                table: "VesselManningPlanHistories",
                columns: new[] { "VesselManningPlanId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VesselManningPlanLines_RankId",
                schema: "crew",
                table: "VesselManningPlanLines",
                column: "RankId");

            migrationBuilder.CreateIndex(
                name: "IX_VesselManningPlanLines_VesselManningPlanId_RankId",
                schema: "crew",
                table: "VesselManningPlanLines",
                columns: new[] { "VesselManningPlanId", "RankId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VesselManningPlans_VesselId_EffectiveStartDate_EffectiveEndDate",
                schema: "crew",
                table: "VesselManningPlans",
                columns: new[] { "VesselId", "EffectiveStartDate", "EffectiveEndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VesselManningPlanHistories",
                schema: "crew");

            migrationBuilder.DropTable(
                name: "VesselManningPlanLines",
                schema: "crew");

            migrationBuilder.DropTable(
                name: "VesselManningPlans",
                schema: "crew");
        }
    }
}
