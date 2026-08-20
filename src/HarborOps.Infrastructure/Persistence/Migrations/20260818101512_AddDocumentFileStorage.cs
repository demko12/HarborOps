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
            // Documents is a system-versioned temporal table. SQL Server allows ADD/DROP
            // COLUMN directly, but a genuine data-type change (UploadedBy int -> uniqueidentifier)
            // requires system versioning to be off first. Group everything in one OFF/ON
            // block, matching the precedent in ChangeDocumentIdToGuid.cs.
            migrationBuilder.Sql(
                "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = OFF);");

            migrationBuilder.DropColumn(
                name: "FileUrl",
                schema: "documents",
                table: "Documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedBy",
                schema: "documents",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

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

            migrationBuilder.Sql(
                "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = ON " +
                "(HISTORY_TABLE = [documents].[DocumentsHistory], DATA_CONSISTENCY_CHECK = OFF));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = OFF);");

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

            migrationBuilder.Sql(
                "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = ON " +
                "(HISTORY_TABLE = [documents].[DocumentsHistory], DATA_CONSISTENCY_CHECK = OFF));");
        }
    }
}
