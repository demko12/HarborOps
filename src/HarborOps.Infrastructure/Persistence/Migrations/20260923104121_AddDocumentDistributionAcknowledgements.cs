using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDistributionAcknowledgements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileRevisionId",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            // Existing downloadable documents need an immutable baseline revision too;
            // otherwise they could never be issued until their binary was replaced.
            migrationBuilder.Sql("UPDATE [documents].[Documents] SET [FileRevisionId] = NEWID() WHERE [StorageKey] IS NOT NULL AND [FileRevisionId] IS NULL;");

            migrationBuilder.CreateTable(
                name: "DocumentDistributionIssues",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumberSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentTypeSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentExpirySnapshot = table.Column<DateOnly>(type: "date", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDistributionIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentDistributionIssues_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentDistributionRecipients",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StateReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentDistributionRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentDistributionRecipients_DocumentDistributionIssues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "documents",
                        principalTable: "DocumentDistributionIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributionIssues_DocumentId",
                schema: "documents",
                table: "DocumentDistributionIssues",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributionRecipients_IssueId_RecipientUserId",
                schema: "documents",
                table: "DocumentDistributionRecipients",
                columns: new[] { "IssueId", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentDistributionRecipients_RecipientUserId_State",
                schema: "documents",
                table: "DocumentDistributionRecipients",
                columns: new[] { "RecipientUserId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentDistributionRecipients",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "DocumentDistributionIssues",
                schema: "documents");

            migrationBuilder.DropColumn(
                name: "FileRevisionId",
                schema: "documents",
                table: "Documents");
        }
    }
}
