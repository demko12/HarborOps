using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceSupplierReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.AlterColumn<Guid>(
                name: "StocktakeId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierReturns",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "maintenance",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnLines",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "maintenance",
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnLines_SupplierReturns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalSchema: "maintenance",
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                columns: new[] { "StocktakeId", "InventoryItemId" },
                unique: true,
                filter: "[StocktakeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "SupplierReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "SupplierReturnLineId",
                unique: true,
                filter: "[SupplierReturnLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_InventoryItemId",
                schema: "maintenance",
                table: "SupplierReturnLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_PurchaseOrderLineId",
                schema: "maintenance",
                table: "SupplierReturnLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnLines_SupplierReturnId_PurchaseOrderLineId",
                schema: "maintenance",
                table: "SupplierReturnLines",
                columns: new[] { "SupplierReturnId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_CreatedUtc_Id",
                schema: "maintenance",
                table: "SupplierReturns",
                columns: new[] { "CreatedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_PurchaseOrderId_Status",
                schema: "maintenance",
                table: "SupplierReturns",
                columns: new[] { "PurchaseOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_Status",
                schema: "maintenance",
                table: "SupplierReturns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_SupplierId",
                schema: "maintenance",
                table: "SupplierReturns",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustments_SupplierReturnLines_SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "SupplierReturnLineId",
                principalSchema: "maintenance",
                principalTable: "SupplierReturnLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustments_SupplierReturns_SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                column: "SupplierReturnId",
                principalSchema: "maintenance",
                principalTable: "SupplierReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustments_SupplierReturnLines_SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustments_SupplierReturns_SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            // Supplier-return movements have no stocktake and cannot survive StocktakeId becoming required again.
            migrationBuilder.Sql(
                "DELETE FROM [maintenance].[InventoryAdjustments] WHERE [StocktakeId] IS NULL;");

            migrationBuilder.DropTable(
                name: "SupplierReturnLines",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "SupplierReturns",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "SupplierReturnId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "SupplierReturnLineId",
                schema: "maintenance",
                table: "InventoryAdjustments");

            migrationBuilder.AlterColumn<Guid>(
                name: "StocktakeId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                columns: new[] { "StocktakeId", "InventoryItemId" },
                unique: true);
        }
    }
}
