using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreventiveWorkGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SourceOccurrenceDueDate",
                schema: "maintenance",
                table: "MaintenanceWorkOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceScheduleId",
                schema: "maintenance",
                table: "MaintenanceWorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScheduleOccurrences",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplacedWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeneratedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_MaintenanceSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "maintenance",
                        principalTable: "MaintenanceSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleOccurrences_MaintenanceWorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "maintenance",
                        principalTable: "MaintenanceWorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceWorkOrders_SourceScheduleId_SourceOccurrenceDueDate",
                schema: "maintenance",
                table: "MaintenanceWorkOrders",
                columns: new[] { "SourceScheduleId", "SourceOccurrenceDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_ScheduleId_DueDate",
                schema: "maintenance",
                table: "ScheduleOccurrences",
                columns: new[] { "ScheduleId", "DueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOccurrences_WorkOrderId",
                schema: "maintenance",
                table: "ScheduleOccurrences",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceWorkOrders_MaintenanceSchedules_SourceScheduleId",
                schema: "maintenance",
                table: "MaintenanceWorkOrders",
                column: "SourceScheduleId",
                principalSchema: "maintenance",
                principalTable: "MaintenanceSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceWorkOrders_MaintenanceSchedules_SourceScheduleId",
                schema: "maintenance",
                table: "MaintenanceWorkOrders");

            migrationBuilder.DropTable(
                name: "ScheduleOccurrences",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceWorkOrders_SourceScheduleId_SourceOccurrenceDueDate",
                schema: "maintenance",
                table: "MaintenanceWorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceOccurrenceDueDate",
                schema: "maintenance",
                table: "MaintenanceWorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceScheduleId",
                schema: "maintenance",
                table: "MaintenanceWorkOrders");
        }
    }
}
