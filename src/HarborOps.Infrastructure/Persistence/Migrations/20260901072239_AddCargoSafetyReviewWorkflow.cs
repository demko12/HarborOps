using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoSafetyReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                schema: "cargo",
                table: "CargoSafetyInfos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CargoSafetyReviews",
                schema: "cargo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CargoSafetyInfoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoSafetyReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CargoSafetyReviews_CargoSafetyInfos_CargoSafetyInfoId",
                        column: x => x.CargoSafetyInfoId,
                        principalSchema: "cargo",
                        principalTable: "CargoSafetyInfos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CargoSafetyReviews_CargoSafetyInfoId",
                schema: "cargo",
                table: "CargoSafetyReviews",
                column: "CargoSafetyInfoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CargoSafetyReviews",
                schema: "cargo");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                schema: "cargo",
                table: "CargoSafetyInfos");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                schema: "cargo",
                table: "CargoSafetyInfos");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                schema: "cargo",
                table: "CargoSafetyInfos");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                schema: "cargo",
                table: "CargoSafetyInfos");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                schema: "cargo",
                table: "CargoSafetyInfos");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                schema: "cargo",
                table: "CargoSafetyInfos");
        }
    }
}
