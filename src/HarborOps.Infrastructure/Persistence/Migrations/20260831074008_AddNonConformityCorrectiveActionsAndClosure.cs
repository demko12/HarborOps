using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNonConformityCorrectiveActionsAndClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                schema: "compliance",
                table: "NonConformities",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                schema: "compliance",
                table: "NonConformities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureNotes",
                schema: "compliance",
                table: "NonConformities",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinalEvidenceDocumentId",
                schema: "compliance",
                table: "NonConformities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrectiveActions",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NonConformityId = table.Column<int>(type: "int", nullable: false),
                    ActionText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectiveActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrectiveActions_NonConformities_NonConformityId",
                        column: x => x.NonConformityId,
                        principalSchema: "compliance",
                        principalTable: "NonConformities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectiveActions_NonConformityId",
                schema: "compliance",
                table: "CorrectiveActions",
                column: "NonConformityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectiveActions",
                schema: "compliance");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "ClosureNotes",
                schema: "compliance",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "FinalEvidenceDocumentId",
                schema: "compliance",
                table: "NonConformities");
        }
    }
}
