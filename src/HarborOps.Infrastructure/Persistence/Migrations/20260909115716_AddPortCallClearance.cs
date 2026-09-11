using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortCallClearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortCallClearances",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoyageStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEventSequence = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallClearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallClearances_VoyageStops_VoyageStopId",
                        column: x => x.VoyageStopId,
                        principalSchema: "voyages",
                        principalTable: "VoyageStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortCallClearances_Voyages_VoyageId",
                        column: x => x.VoyageId,
                        principalSchema: "voyages",
                        principalTable: "Voyages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortCallClearanceItems",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortCallClearanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuthorityReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AddedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallClearanceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallClearanceItems_PortCallClearances_PortCallClearanceId",
                        column: x => x.PortCallClearanceId,
                        principalSchema: "voyages",
                        principalTable: "PortCallClearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortCallClearanceEvents",
                schema: "voyages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortCallClearanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PortCallClearanceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortCallClearanceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortCallClearanceEvents_PortCallClearanceItems_PortCallClearanceItemId",
                        column: x => x.PortCallClearanceItemId,
                        principalSchema: "voyages",
                        principalTable: "PortCallClearanceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortCallClearanceEvents_PortCallClearances_PortCallClearanceId",
                        column: x => x.PortCallClearanceId,
                        principalSchema: "voyages",
                        principalTable: "PortCallClearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortCallClearanceEvents_PortCallClearanceId_Sequence",
                schema: "voyages",
                table: "PortCallClearanceEvents",
                columns: new[] { "PortCallClearanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortCallClearanceEvents_PortCallClearanceItemId",
                schema: "voyages",
                table: "PortCallClearanceEvents",
                column: "PortCallClearanceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PortCallClearanceItems_PortCallClearanceId_IsRequired_DueAtUtc_Category_Id",
                schema: "voyages",
                table: "PortCallClearanceItems",
                columns: new[] { "PortCallClearanceId", "IsRequired", "DueAtUtc", "Category", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PortCallClearances_VoyageId_Status",
                schema: "voyages",
                table: "PortCallClearances",
                columns: new[] { "VoyageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PortCallClearances_VoyageStopId",
                schema: "voyages",
                table: "PortCallClearances",
                column: "VoyageStopId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO [voyages].[PortCallClearances]
                    ([Id], [VoyageId], [VoyageStopId], [Status], [CompletedByUserId], [CompletedAtUtc], [LastEventSequence], [ConcurrencyToken])
                SELECT
                    NEWID(),
                    [stop].[VoyageId],
                    [stop].[Id],
                    CASE WHEN [stop].[Status] = N'Departed' THEN N'Completed' ELSE N'Pending' END,
                    NULL,
                    NULL,
                    0,
                    NEWID()
                FROM [voyages].[VoyageStops] AS [stop]
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [voyages].[PortCallClearances] AS [clearance]
                    WHERE [clearance].[VoyageStopId] = [stop].[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortCallClearanceEvents",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "PortCallClearanceItems",
                schema: "voyages");

            migrationBuilder.DropTable(
                name: "PortCallClearances",
                schema: "voyages");
        }
    }
}
