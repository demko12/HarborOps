using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenancePurchaseOrderReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "maintenance",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<decimal>(
                name: "LegacyReceivedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            // Orders received before receipts were tracked were received in full. Record that as an explicit
            // legacy opening balance per line; inventory is untouched and no receipt, date, or actor is invented.
            migrationBuilder.Sql("""
                UPDATE line
                SET line.[LegacyReceivedQuantity] = line.[QuantityOrdered],
                    line.[ReceivedQuantity] = line.[QuantityOrdered]
                FROM [maintenance].[PurchaseOrderLines] AS line
                INNER JOIN [maintenance].[PurchaseOrders] AS purchaseOrder
                    ON purchaseOrder.[Id] = line.[PurchaseOrderId]
                WHERE purchaseOrder.[Status] = N'Received';
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrderReceipts",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveryReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderStatusBefore = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderStatusAfter = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderReceiptLines",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    LineReceivedQuantityAfter = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderReceiptLines_PurchaseOrderReceipts_PurchaseOrderReceiptId",
                        column: x => x.PurchaseOrderReceiptId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrderReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "PurchaseOrderReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "PurchaseOrderReceiptLineId",
                unique: true,
                filter: "[PurchaseOrderReceiptLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptLines_InventoryItemId",
                schema: "maintenance",
                table: "PurchaseOrderReceiptLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptLines_PurchaseOrderLineId",
                schema: "maintenance",
                table: "PurchaseOrderReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceiptLines_PurchaseOrderReceiptId_PurchaseOrderLineId",
                schema: "maintenance",
                table: "PurchaseOrderReceiptLines",
                columns: new[] { "PurchaseOrderReceiptId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceipts_ClientRequestId",
                schema: "maintenance",
                table: "PurchaseOrderReceipts",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderReceipts_PurchaseOrderId_ReceivedUtc_RecordedUtc",
                schema: "maintenance",
                table: "PurchaseOrderReceipts",
                columns: new[] { "PurchaseOrderId", "ReceivedUtc", "RecordedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustments_PurchaseOrderReceiptLines_PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "PurchaseOrderReceiptLineId",
                principalSchema: "maintenance",
                principalTable: "PurchaseOrderReceiptLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustments_PurchaseOrderReceipts_PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "PurchaseOrderReceiptId",
                principalSchema: "maintenance",
                principalTable: "PurchaseOrderReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [maintenance].[InventoryAdjustments] WHERE [PurchaseOrderReceiptId] IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustments_PurchaseOrderReceiptLines_PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustments_PurchaseOrderReceipts_PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropTable(
                name: "PurchaseOrderReceiptLines",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "PurchaseOrderReceipts",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "maintenance",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "LegacyReceivedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                schema: "maintenance",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderReceiptId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderReceiptLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");
        }
    }
}
