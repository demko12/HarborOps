using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                schema: "documents",
                table: "Documents",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Draft");

            // Backfill: documents that were already live under the pre-existing lifecycle
            // (Active/Superseded) predate the review workflow and never went through
            // submit/approve, so they are treated as already-approved rather than left in
            // Draft (the column default, correct only for genuinely new/unreviewed rows).
            migrationBuilder.Sql(
                "UPDATE documents.Documents SET ReviewStatus = 'Approved' WHERE Status IN ('Active','Superseded') AND ReviewStatus <> 'Approved';");

            migrationBuilder.CreateTable(
                name: "DocumentReviewEvents",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReviewEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviewEvents_DocumentId_OccurredAtUtc",
                schema: "documents",
                table: "DocumentReviewEvents",
                columns: new[] { "DocumentId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentReviewEvents",
                schema: "documents");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                schema: "documents",
                table: "Documents");
        }
    }
}
