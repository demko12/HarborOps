using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoBillOfLading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillsOfLading",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CargoBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ShipperName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShipperAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ConsigneeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConsigneeAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NotifyPartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NotifyPartyAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlaceOfReceipt = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PortOfLoading = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PortOfDischarge = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlaceOfDelivery = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FreightTerms = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CargoMarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CargoDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PackageCount = table.Column<int>(type: "int", nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IssuePlace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedIssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SnapshotBookingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SnapshotVoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SnapshotCargoType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SnapshotBookingDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SnapshotBookingPackageCount = table.Column<int>(type: "int", nullable: true),
                    SnapshotBookingWeightKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    SnapshotBookingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillsOfLading", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillsOfLading_CargoBookings_CargoBookingId",
                        column: x => x.CargoBookingId,
                        principalSchema: "cargo",
                        principalTable: "CargoBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "BillsOfLadingHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "cargo")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "BillOfLadingContainers",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfLadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActiveSelection = table.Column<bool>(type: "bit", nullable: false),
                    SnapshotContainerNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    SnapshotContainerType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfLadingContainers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillOfLadingContainers_BillsOfLading_BillOfLadingId",
                        column: x => x.BillOfLadingId,
                        principalSchema: "cargo",
                        principalTable: "BillsOfLading",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillOfLadingContainers_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillOfLadingEvents",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillOfLadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfLadingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillOfLadingEvents_BillsOfLading_BillOfLadingId",
                        column: x => x.BillOfLadingId,
                        principalSchema: "cargo",
                        principalTable: "BillsOfLading",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfLadingContainers_ActiveContainer",
                schema: "cargo",
                table: "BillOfLadingContainers",
                column: "ContainerId",
                unique: true,
                filter: "[IsActiveSelection] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfLadingContainers_BillOfLadingId_ContainerId",
                schema: "cargo",
                table: "BillOfLadingContainers",
                columns: new[] { "BillOfLadingId", "ContainerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfLadingEvents_BillOfLadingId_OccurredUtc",
                schema: "cargo",
                table: "BillOfLadingEvents",
                columns: new[] { "BillOfLadingId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfLading_BillNumber",
                schema: "cargo",
                table: "BillsOfLading",
                column: "BillNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfLading_CargoBookingId_BillNumber",
                schema: "cargo",
                table: "BillsOfLading",
                columns: new[] { "CargoBookingId", "BillNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillOfLadingContainers",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "BillOfLadingEvents",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "BillsOfLading",
                schema: "cargo")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "BillsOfLadingHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "cargo")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");
        }
    }
}
