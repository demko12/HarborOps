using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoReeferMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReeferMonitoringProfiles",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TargetCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeferMonitoringProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeferMonitoringProfiles_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReeferTemperatureReadings",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MeasuredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThresholdMinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ThresholdMaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ThresholdTargetCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ThresholdProfileVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CorrectsReadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByReadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeferTemperatureReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeferTemperatureReadings_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReeferTemperatureReadings_ReeferTemperatureReadings_CorrectsReadingId",
                        column: x => x.CorrectsReadingId,
                        principalSchema: "cargo",
                        principalTable: "ReeferTemperatureReadings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReeferTemperatureReadings_ReeferTemperatureReadings_SupersededByReadingId",
                        column: x => x.SupersededByReadingId,
                        principalSchema: "cargo",
                        principalTable: "ReeferTemperatureReadings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReeferProfileChanges",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReeferMonitoringProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FromMinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FromMaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    FromTargetCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ToMinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ToMaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ToTargetCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeferProfileChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeferProfileChanges_ReeferMonitoringProfiles_ReeferMonitoringProfileId",
                        column: x => x.ReeferMonitoringProfileId,
                        principalSchema: "cargo",
                        principalTable: "ReeferMonitoringProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReeferExcursions",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ThresholdMinimumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ThresholdMaximumCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MeasuredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByExcursionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReeferExcursions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReeferExcursions_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReeferExcursions_ReeferExcursions_SupersededByExcursionId",
                        column: x => x.SupersededByExcursionId,
                        principalSchema: "cargo",
                        principalTable: "ReeferExcursions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReeferExcursions_ReeferTemperatureReadings_ReadingId",
                        column: x => x.ReadingId,
                        principalSchema: "cargo",
                        principalTable: "ReeferTemperatureReadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReeferExcursions_ContainerId_DetectedAtUtc_Id",
                schema: "cargo",
                table: "ReeferExcursions",
                columns: new[] { "ContainerId", "DetectedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReeferExcursions_OpenContainerId",
                schema: "cargo",
                table: "ReeferExcursions",
                column: "ContainerId",
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_ReeferExcursions_ReadingId",
                schema: "cargo",
                table: "ReeferExcursions",
                column: "ReadingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReeferExcursions_SupersededByExcursionId",
                schema: "cargo",
                table: "ReeferExcursions",
                column: "SupersededByExcursionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeferMonitoringProfiles_ContainerId",
                schema: "cargo",
                table: "ReeferMonitoringProfiles",
                column: "ContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReeferProfileChanges_ReeferMonitoringProfileId_Version",
                schema: "cargo",
                table: "ReeferProfileChanges",
                columns: new[] { "ReeferMonitoringProfileId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReeferTemperatureReadings_ContainerId_MeasuredAtUtc_Id",
                schema: "cargo",
                table: "ReeferTemperatureReadings",
                columns: new[] { "ContainerId", "MeasuredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReeferTemperatureReadings_ContainerId_Status",
                schema: "cargo",
                table: "ReeferTemperatureReadings",
                columns: new[] { "ContainerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReeferTemperatureReadings_CorrectsReadingId",
                schema: "cargo",
                table: "ReeferTemperatureReadings",
                column: "CorrectsReadingId");

            migrationBuilder.CreateIndex(
                name: "IX_ReeferTemperatureReadings_SupersededByReadingId",
                schema: "cargo",
                table: "ReeferTemperatureReadings",
                column: "SupersededByReadingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReeferExcursions",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "ReeferProfileChanges",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "ReeferTemperatureReadings",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "ReeferMonitoringProfiles",
                schema: "cargo");
        }
    }
}
