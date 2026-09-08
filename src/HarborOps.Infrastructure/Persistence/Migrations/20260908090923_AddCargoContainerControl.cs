using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoContainerControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContainerControls",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SealNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    SealStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LatestInspectionResult = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LatestInspectionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LatestInspectionTemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    LatestInspectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LatestInspectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StateChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainerControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainerControls_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalSchema: "cargo",
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ContainerControlsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "cargo")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "ContainerControlEvents",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerControlId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SealNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    InspectionResult = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainerControlEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainerControlEvents_ContainerControls_ContainerControlId",
                        column: x => x.ContainerControlId,
                        principalSchema: "cargo",
                        principalTable: "ContainerControls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContainerControlEvents_ContainerControlId_OccurredUtc",
                schema: "cargo",
                table: "ContainerControlEvents",
                columns: new[] { "ContainerControlId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContainerControls_ActiveSealNumber",
                schema: "cargo",
                table: "ContainerControls",
                column: "SealNumber",
                unique: true,
                filter: "[SealNumber] IS NOT NULL AND [SealStatus] = 'Intact'");

            migrationBuilder.CreateIndex(
                name: "IX_ContainerControls_ContainerId",
                schema: "cargo",
                table: "ContainerControls",
                column: "ContainerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContainerControlEvents",
                schema: "cargo");

            migrationBuilder.DropTable(
                name: "ContainerControls",
                schema: "cargo")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ContainerControlsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "cargo")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");
        }
    }
}
