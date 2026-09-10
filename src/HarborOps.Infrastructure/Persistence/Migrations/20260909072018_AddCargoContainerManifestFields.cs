using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCargoContainerManifestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PortOfDischargeCode",
                schema: "cargo",
                table: "Containers",
                type: "varchar(5)",
                unicode: false,
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortOfLoadingCode",
                schema: "cargo",
                table: "Containers",
                type: "varchar(5)",
                unicode: false,
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                schema: "cargo",
                table: "Containers",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Containers_ContainerNumber",
                schema: "cargo",
                table: "Containers",
                column: "ContainerNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Containers_ContainerNumber",
                schema: "cargo",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "PortOfDischargeCode",
                schema: "cargo",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "PortOfLoadingCode",
                schema: "cargo",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                schema: "cargo",
                table: "Containers");
        }
    }
}
