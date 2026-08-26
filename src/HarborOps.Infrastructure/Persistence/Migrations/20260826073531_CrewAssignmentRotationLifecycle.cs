using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrewAssignmentRotationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationNotes",
                schema: "crew",
                table: "CrewRotations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                schema: "crew",
                table: "CrewRotations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                schema: "crew",
                table: "CrewRotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutedAtUtc",
                schema: "crew",
                table: "CrewRotations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutedByUserId",
                schema: "crew",
                table: "CrewRotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementAssignmentId",
                schema: "crew",
                table: "CrewRotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "crew",
                table: "CrewRotations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Planned");

            migrationBuilder.AddColumn<string>(
                name: "CancellationNotes",
                schema: "crew",
                table: "CrewAssignments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                schema: "crew",
                table: "CrewAssignments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                schema: "crew",
                table: "CrewAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [crew].[CrewAssignments]
                SET [Status] = N'Completed'
                WHERE [ActualSignOffDate] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationNotes",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "ExecutedAtUtc",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "ExecutedByUserId",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "ReplacementAssignmentId",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "crew",
                table: "CrewRotations");

            migrationBuilder.DropColumn(
                name: "CancellationNotes",
                schema: "crew",
                table: "CrewAssignments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "crew",
                table: "CrewAssignments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                schema: "crew",
                table: "CrewAssignments");
        }
    }
}
