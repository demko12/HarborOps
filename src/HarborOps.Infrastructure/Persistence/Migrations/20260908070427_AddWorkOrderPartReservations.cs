using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderPartReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrderPartReservations",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityRequested = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    QuantityReserved = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    QuantityConsumed = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderPartReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderPartReservations_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderPartReservations_MaintenanceWorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "maintenance",
                        principalTable: "MaintenanceWorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderPartReservations_InventoryItemId",
                schema: "maintenance",
                table: "WorkOrderPartReservations",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderPartReservations_WorkOrderId_InventoryItemId",
                schema: "maintenance",
                table: "WorkOrderPartReservations",
                columns: new[] { "WorkOrderId", "InventoryItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderPartReservations",
                schema: "maintenance");
        }
    }
}
