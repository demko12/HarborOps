using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialFleet : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "fleet");

        migrationBuilder.CreateTable(
            name: "Vessels",
            schema: "fleet",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ImoNumber = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                FlagState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                VesselType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vessels", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Vessels_ImoNumber",
            schema: "fleet",
            table: "Vessels",
            column: "ImoNumber",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Vessels",
            schema: "fleet");
    }
}
