using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceStocktakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservationConcurrencyToken",
                schema: "maintenance",
                table: "InventoryItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "StockConcurrencyToken",
                schema: "maintenance",
                table: "InventoryItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateTable(
                name: "Stocktakes",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustments",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalSchema: "maintenance",
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeLines",
                schema: "maintenance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    BaselineStockConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineCapturedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    Variance = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    VarianceReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CountedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "maintenance",
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeLines_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalSchema: "maintenance",
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_InventoryItemId_AdjustedUtc",
                schema: "maintenance",
                table: "InventoryAdjustments",
                columns: new[] { "InventoryItemId", "AdjustedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "InventoryAdjustments",
                columns: new[] { "StocktakeId", "InventoryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLines_InventoryItemId",
                schema: "maintenance",
                table: "StocktakeLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeLines_StocktakeId_InventoryItemId",
                schema: "maintenance",
                table: "StocktakeLines",
                columns: new[] { "StocktakeId", "InventoryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_CreatedUtc_Id",
                schema: "maintenance",
                table: "Stocktakes",
                columns: new[] { "CreatedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_Status",
                schema: "maintenance",
                table: "Stocktakes",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAdjustments",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "StocktakeLines",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "Stocktakes",
                schema: "maintenance");

            migrationBuilder.DropColumn(
                name: "ReservationConcurrencyToken",
                schema: "maintenance",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "StockConcurrencyToken",
                schema: "maintenance",
                table: "InventoryItems");
        }
    }
}
