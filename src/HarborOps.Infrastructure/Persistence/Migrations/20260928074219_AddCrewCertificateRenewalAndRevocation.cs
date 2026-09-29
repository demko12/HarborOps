using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrewCertificateRenewalAndRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "crew",
                table: "CrewCertificates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                schema: "crew",
                table: "CrewCertificates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                schema: "crew",
                table: "CrewCertificates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByUserId",
                schema: "crew",
                table: "CrewCertificates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrewCertificateHistories",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PredecessorCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewCertificateHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrewCertificates_CrewMemberId",
                schema: "crew",
                table: "CrewCertificates",
                column: "CrewMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_CrewCertificates_PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates",
                column: "PredecessorCertificateId",
                unique: true,
                filter: "[PredecessorCertificateId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrewCertificateHistories_CertificateId",
                schema: "crew",
                table: "CrewCertificateHistories",
                column: "CertificateId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrewCertificates_CrewCertificates_PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates",
                column: "PredecessorCertificateId",
                principalSchema: "crew",
                principalTable: "CrewCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrewCertificates_CrewCertificates_PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropTable(
                name: "CrewCertificateHistories",
                schema: "crew");

            migrationBuilder.DropIndex(
                name: "IX_CrewCertificates_CrewMemberId",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropIndex(
                name: "IX_CrewCertificates_PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropColumn(
                name: "PredecessorCertificateId",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                schema: "crew",
                table: "CrewCertificates");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                schema: "crew",
                table: "CrewCertificates");
        }
    }
}
