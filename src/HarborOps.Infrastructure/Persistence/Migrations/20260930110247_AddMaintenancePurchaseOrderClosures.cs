using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenancePurchaseOrderClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClosedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PurchaseOrderClosures",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderStatusBefore = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderStatusAfter = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderClosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderClosures_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderClosureLines",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderClosureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOrdered = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ClosedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderClosureLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderClosureLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderClosureLines_PurchaseOrderClosures_PurchaseOrderClosureId",
                        column: x => x.PurchaseOrderClosureId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrderClosures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderClosureLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderClosureLines_InventoryItemId",
                schema: "maintenance",
                table: "PurchaseOrderClosureLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderClosureLines_PurchaseOrderClosureId",
                schema: "maintenance",
                table: "PurchaseOrderClosureLines",
                column: "PurchaseOrderClosureId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderClosureLines_PurchaseOrderLineId",
                schema: "maintenance",
                table: "PurchaseOrderClosureLines",
                column: "PurchaseOrderLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderClosures_ClientRequestId",
                schema: "maintenance",
                table: "PurchaseOrderClosures",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderClosures_PurchaseOrderId",
                schema: "maintenance",
                table: "PurchaseOrderClosures",
                column: "PurchaseOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseOrderClosureLines",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "PurchaseOrderClosures",
                schema: "maintenance");

            migrationBuilder.DropColumn(
                name: "ClosedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines");
        }
    }
}
