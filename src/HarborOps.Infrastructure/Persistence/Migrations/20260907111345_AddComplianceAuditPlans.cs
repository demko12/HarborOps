using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceAuditPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditPlans",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AssignedInspectorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SurveyAuditId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditPlans_SurveyAudits_SurveyAuditId",
                        column: x => x.SurveyAuditId,
                        principalSchema: "compliance",
                        principalTable: "SurveyAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditPlanHistories",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuditPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousPlannedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewPlannedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PreviousDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NewDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PreviousInspectorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewInspectorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditPlanHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditPlanHistories_AuditPlans_AuditPlanId",
                        column: x => x.AuditPlanId,
                        principalSchema: "compliance",
                        principalTable: "AuditPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditPlanHistories_AuditPlanId_OccurredAtUtc",
                schema: "compliance",
                table: "AuditPlanHistories",
                columns: new[] { "AuditPlanId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditPlans_SurveyAuditId",
                schema: "compliance",
                table: "AuditPlans",
                column: "SurveyAuditId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditPlans_VesselId_Type_Status",
                schema: "compliance",
                table: "AuditPlans",
                columns: new[] { "VesselId", "Type", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditPlanHistories",
                schema: "compliance");

            migrationBuilder.DropTable(
                name: "AuditPlans",
                schema: "compliance");
        }
    }
}
