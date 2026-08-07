using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAllDomainEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ArrivalPortCode",
            schema: "voyages",
            table: "Voyages",
            type: "varchar(5)",
            unicode: false,
            maxLength: 5,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedUtc",
            schema: "voyages",
            table: "Voyages",
            type: "datetime2",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<string>(
            name: "DeparturePortCode",
            schema: "voyages",
            table: "Voyages",
            type: "varchar(5)",
            unicode: false,
            maxLength: 5,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<Guid>(
            name: "AssignedEngineerId",
            schema: "maintenance",
            table: "MaintenanceWorkOrders",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedUtc",
            schema: "crew",
            table: "CrewMembers",
            type: "datetime2",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.CreateIndex(
            name: "IX_Voyages_VoyageNumber",
            schema: "voyages",
            table: "Voyages",
            column: "VoyageNumber",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Voyages_VoyageNumber",
            schema: "voyages",
            table: "Voyages");

        migrationBuilder.DropColumn(
            name: "ArrivalPortCode",
            schema: "voyages",
            table: "Voyages");

        migrationBuilder.DropColumn(
            name: "CreatedUtc",
            schema: "voyages",
            table: "Voyages");

        migrationBuilder.DropColumn(
            name: "DeparturePortCode",
            schema: "voyages",
            table: "Voyages");

        migrationBuilder.DropColumn(
            name: "CreatedUtc",
            schema: "crew",
            table: "CrewMembers");

        migrationBuilder.AlterColumn<Guid>(
            name: "AssignedEngineerId",
            schema: "maintenance",
            table: "MaintenanceWorkOrders",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);
    }
}
