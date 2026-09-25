using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrewQualificationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrewAssignmentQualificationEvidences",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyConcurrencyTokenAtDecision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateIdsSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewAssignmentQualificationEvidences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrewQualificationPolicies",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrewPositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewQualificationPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrewQualificationPolicyHistories",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousEnabled = table.Column<bool>(type: "bit", nullable: true),
                    NewEnabled = table.Column<bool>(type: "bit", nullable: true),
                    PreviousRequiredTypesSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewRequiredTypesSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewQualificationPolicyHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrewQualificationRequirements",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewQualificationRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrewQualificationRequirements_CrewQualificationPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalSchema: "crew",
                        principalTable: "CrewQualificationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrewAssignmentQualificationEvidences_AssignmentId",
                schema: "crew",
                table: "CrewAssignmentQualificationEvidences",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CrewQualificationPolicies_CrewPositionId",
                schema: "crew",
                table: "CrewQualificationPolicies",
                column: "CrewPositionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrewQualificationPolicyHistories_PolicyId_OccurredAtUtc",
                schema: "crew",
                table: "CrewQualificationPolicyHistories",
                columns: new[] { "PolicyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrewQualificationRequirements_PolicyId_RequiredType",
                schema: "crew",
                table: "CrewQualificationRequirements",
                columns: new[] { "PolicyId", "RequiredType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrewAssignmentQualificationEvidences",
                schema: "crew");

            migrationBuilder.DropTable(
                name: "CrewQualificationPolicyHistories",
                schema: "crew");

            migrationBuilder.DropTable(
                name: "CrewQualificationRequirements",
                schema: "crew");

            migrationBuilder.DropTable(
                name: "CrewQualificationPolicies",
                schema: "crew");
        }
    }
}
