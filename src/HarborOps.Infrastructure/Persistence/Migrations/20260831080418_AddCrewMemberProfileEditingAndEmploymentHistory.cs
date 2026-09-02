using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrewMemberProfileEditingAndEmploymentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "crew",
                table: "CrewMembers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "crew",
                table: "CrewMembers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Best-effort backfill of the existing single "Name" column into FirstName/LastName
            // before the column is dropped below - splits on the first space, putting any
            // remaining words into LastName (e.g. "Ivan Petrovic" -> "Ivan" / "Petrovic",
            // "Cher" -> "Cher" / "").
            migrationBuilder.Sql(
                """
                UPDATE crew.CrewMembers
                SET FirstName = CASE WHEN CHARINDEX(' ', Name) > 0 THEN LEFT(Name, CHARINDEX(' ', Name) - 1) ELSE Name END,
                    LastName  = CASE WHEN CHARINDEX(' ', Name) > 0 THEN LTRIM(SUBSTRING(Name, CHARINDEX(' ', Name) + 1, LEN(Name))) ELSE '' END;
                """);

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "crew",
                table: "CrewMembers");

            migrationBuilder.CreateTable(
                name: "CrewEmploymentStatusHistories",
                schema: "crew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrewMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewEmploymentStatusHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrewEmploymentStatusHistories_CrewMemberId",
                schema: "crew",
                table: "CrewEmploymentStatusHistories",
                column: "CrewMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrewEmploymentStatusHistories",
                schema: "crew");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "crew",
                table: "CrewMembers");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "crew",
                table: "CrewMembers");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "crew",
                table: "CrewMembers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
