using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPscInspectionFollowUpWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NonConformities_PscId",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.AddColumn<string>(
                name: "PscDeficiencyCode",
                schema: "compliance",
                table: "NonConformities",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PscDeficiencyDescription",
                schema: "compliance",
                table: "NonConformities",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PscDetentionDecisions",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PscInspectionId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AuthorityReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PscDetentionDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PscDetentionDecisions_PscInspections_PscInspectionId",
                        column: x => x.PscInspectionId,
                        principalSchema: "compliance",
                        principalTable: "PscInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NonConformities_PscId_ClosureStatus",
                schema: "compliance",
                table: "NonConformities",
                columns: new[] { "PscId", "ClosureStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PscDetentionDecisions_PscInspectionId_OccurredAtUtc",
                schema: "compliance",
                table: "PscDetentionDecisions",
                columns: new[] { "PscInspectionId", "OccurredAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO [compliance].[PscDetentionDecisions]
                    ([Id], [PscInspectionId], [Decision], [AuthorityReference], [Reason], [ReleaseDate], [DocumentId], [ActorUserId], [OccurredAtUtc])
                SELECT NEWID(), [Id], N'Detained', N'Legacy vessel detention flag', N'Backfilled from the legacy VesselDetained value.', NULL, NULL,
                    '00000000-0000-0000-0000-000000000000', TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
                FROM [compliance].[PscInspections]
                WHERE [VesselDetained] = CAST(1 AS bit);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PscDetentionDecisions",
                schema: "compliance");

            migrationBuilder.DropIndex(
                name: "IX_NonConformities_PscId_ClosureStatus",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "PscDeficiencyCode",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "PscDeficiencyDescription",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformities_PscId",
                schema: "compliance",
                table: "NonConformities",
                column: "PscId");
        }
    }
}
