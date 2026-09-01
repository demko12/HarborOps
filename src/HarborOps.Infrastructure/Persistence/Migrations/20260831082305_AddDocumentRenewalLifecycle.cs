using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRenewalLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PredecessorDocumentId",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                schema: "documents",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByUserId",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PredecessorDocumentId",
                schema: "documents",
                table: "Documents",
                column: "PredecessorDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Documents_PredecessorDocumentId",
                schema: "documents",
                table: "Documents",
                column: "PredecessorDocumentId",
                principalSchema: "documents",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Documents_PredecessorDocumentId",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_PredecessorDocumentId",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PredecessorDocumentId",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SupersededByUserId",
                schema: "documents",
                table: "Documents");
        }
    }
}
