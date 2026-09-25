using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselDocumentRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VesselDocumentRequirements",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VesselId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinimumRemainingValidityDays = table.Column<int>(type: "int", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselDocumentRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VesselDocumentRequirements_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "documents",
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VesselDocumentRequirementHistories",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinimumRemainingValidityDays = table.Column<int>(type: "int", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VesselDocumentRequirementHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VesselDocumentRequirementHistories_VesselDocumentRequirements_RequirementId",
                        column: x => x.RequirementId,
                        principalSchema: "documents",
                        principalTable: "VesselDocumentRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VesselDocumentRequirementHistories_RequirementId_Revision",
                schema: "documents",
                table: "VesselDocumentRequirementHistories",
                columns: new[] { "RequirementId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VesselDocumentRequirements_DocumentTypeId",
                schema: "documents",
                table: "VesselDocumentRequirements",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VesselDocumentRequirements_VesselId_DocumentTypeId",
                schema: "documents",
                table: "VesselDocumentRequirements",
                columns: new[] { "VesselId", "DocumentTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VesselDocumentRequirementHistories",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "VesselDocumentRequirements",
                schema: "documents");
        }
    }
}
