using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortCallActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortCallActivities",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallActivities_VoyageStops_VoyageStopId",
                        column: x => x.VoyageStopId,
                        principalSchema: "voyages",
                        principalTable: "VoyageStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortCallActivities_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortCallStatements",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoyageNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StopOrder = table.Column<int>(type: "int", nullable: true),
                    PortName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PortUnLocode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    PortTimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BerthName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualArrivalUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualDepartureUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallStatements_VoyageStops_VoyageStopId",
                        column: x => x.VoyageStopId,
                        principalSchema: "voyages",
                        principalTable: "VoyageStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortCallStatements_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortCallActivityRevisions",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortCallActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallActivityRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallActivityRevisions_PortCallActivities_PortCallActivityId",
                        column: x => x.PortCallActivityId,
                        principalSchema: "voyages",
                        principalTable: "PortCallActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortCallStatementEntries",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortCallStatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceRevision = table.Column<int>(type: "int", nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallStatementEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallStatementEntries_PortCallStatements_PortCallStatementId",
                        column: x => x.PortCallStatementId,
                        principalSchema: "voyages",
                        principalTable: "PortCallStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortCallActivities_VoyageId",
                schema: "voyages",
                table: "PortCallActivities",
                column: "VoyageId");

            migrationBuilder.CreateIndex(
                name: "IX_PortCallActivities_VoyageStopId_Sequence",
                schema: "voyages",
                table: "PortCallActivities",
                columns: new[] { "VoyageStopId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortCallActivityRevisions_PortCallActivityId_Revision",
                schema: "voyages",
                table: "PortCallActivityRevisions",
                columns: new[] { "PortCallActivityId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortCallStatementEntries_PortCallStatementId_Position",
                schema: "voyages",
                table: "PortCallStatementEntries",
                columns: new[] { "PortCallStatementId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortCallStatements_VoyageId",
                schema: "voyages",
                table: "PortCallStatements",
                column: "VoyageId");

            migrationBuilder.CreateIndex(
                name: "IX_PortCallStatements_VoyageStopId",
                schema: "voyages",
                table: "PortCallStatements",
                column: "VoyageStopId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortCallActivityRevisions",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "PortCallStatementEntries",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "PortCallActivities",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "PortCallStatements",
                schema: "voyages");
        }
    }
}
