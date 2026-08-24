using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Documents is a system-versioned temporal table. Unlike ChangeDocumentIdToGuid.cs
            // (which is 100% raw SQL), this migration uses the typed DropColumn/AlterColumn/
            // AddColumn operations below — EF Core's SQL Server migrations generator detects
            // that they target a temporal table and automatically wraps them in its own
            // SYSTEM_VERSIONING OFF/ON, so no manual Sql() toggle is needed (or wanted) here.
            migrationBuilder.DropColumn(
                name: "FileUrl",
                schema: "documents",
                table: "Documents");

            // SQL Server has no conversion from int to uniqueidentifier in either direction,
            // so AlterColumn cannot be used here. Drop and re-add the column as a nullable
            // uniqueidentifier with no data — this is destructive for existing UploadedBy
            // values, the same accepted tradeoff as the rollback in Down().
            migrationBuilder.DropColumn(
                name: "UploadedBy",
                schema: "documents",
                table: "Documents");

            migrationBuilder.AddColumn<Guid>(
                name: "UploadedBy",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                schema: "documents",
                table: "Documents",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                schema: "documents",
                table: "Documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                schema: "documents",
                table: "Documents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                schema: "documents",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                schema: "documents",
                table: "Documents");

            // SQL Server has no conversion from uniqueidentifier to int, so the original
            // UploadedBy values cannot be recovered here. Drop and re-add the column as a
            // nullable int with no data — this rollback is destructive for this column,
            // the same accepted tradeoff as the Id column rollback in ChangeDocumentIdToGuid.cs.
            migrationBuilder.DropColumn(
                name: "UploadedBy",
                schema: "documents",
                table: "Documents");

            migrationBuilder.AddColumn<int>(
                name: "UploadedBy",
                schema: "documents",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                schema: "documents",
                table: "Documents",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
}
