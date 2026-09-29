using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceCertificateRenewals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "compliance",
                table: "ComplianceCertificates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                schema: "compliance",
                table: "ComplianceCertificates",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                schema: "compliance",
                table: "ComplianceCertificates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByUserId",
                schema: "compliance",
                table: "ComplianceCertificates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComplianceCertificateHistories",
                schema: "compliance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SuccessorCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceCertificateHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceCertificateHistories_ComplianceCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalSchema: "compliance",
                        principalTable: "ComplianceCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceCertificates_PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates",
                column: "PredecessorCertificateId",
                unique: true,
                filter: "[PredecessorCertificateId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceCertificateHistories_CertificateId_OccurredAtUtc",
                schema: "compliance",
                table: "ComplianceCertificateHistories",
                columns: new[] { "CertificateId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceCertificates_ComplianceCertificates_PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates",
                column: "PredecessorCertificateId",
                principalSchema: "compliance",
                principalTable: "ComplianceCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceCertificates_ComplianceCertificates_PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropTable(
                name: "ComplianceCertificateHistories",
                schema: "compliance");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceCertificates_PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropColumn(
                name: "PredecessorCertificateId",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                schema: "compliance",
                table: "ComplianceCertificates");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                schema: "compliance",
                table: "ComplianceCertificates");
        }
    }
}
