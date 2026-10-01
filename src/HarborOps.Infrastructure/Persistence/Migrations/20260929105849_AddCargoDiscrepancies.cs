using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoDiscrepancies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "cargo",
                table: "CargoBookings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateTable(
                name: "CargoDiscrepancies",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CargoBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceExcursionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceDirection = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SourceTemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    SourceThresholdMinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    SourceThresholdMaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    SourceMeasuredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceDetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoDiscrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CargoDiscrepancies_CargoBookings_CargoBookingId",
                        column: x => x.CargoBookingId,
                        principalSchema: "cargo",
                        principalTable: "CargoBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CargoDiscrepancies_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CargoDiscrepancies_ReeferExcursions_SourceExcursionId",
                        column: x => x.SourceExcursionId,
                        principalSchema: "cargo",
                        principalTable: "ReeferExcursions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CargoDiscrepancyEvents",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscrepancyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoDiscrepancyEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CargoDiscrepancyEvents_CargoDiscrepancies_DiscrepancyId",
                        column: x => x.DiscrepancyId,
                        principalSchema: "cargo",
                        principalTable: "CargoDiscrepancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CargoDiscrepancies_CargoBookingId_ReportedAtUtc_Id",
                schema: "cargo",
                table: "CargoDiscrepancies",
                columns: new[] { "CargoBookingId", "ReportedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CargoDiscrepancies_CargoBookingId_Status",
                schema: "cargo",
                table: "CargoDiscrepancies",
                columns: new[] { "CargoBookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CargoDiscrepancies_ContainerId",
                schema: "cargo",
                table: "CargoDiscrepancies",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_CargoDiscrepancies_SourceExcursionId",
                schema: "cargo",
                table: "CargoDiscrepancies",
                column: "SourceExcursionId",
                unique: true,
                filter: "[SourceExcursionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CargoDiscrepancyEvents_DiscrepancyId_OccurredAtUtc_Id",
                schema: "cargo",
                table: "CargoDiscrepancyEvents",
                columns: new[] { "DiscrepancyId", "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CargoDiscrepancyEvents",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "CargoDiscrepancies",
                schema: "cargo");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "cargo",
                table: "CargoBookings");
        }
    }
}
