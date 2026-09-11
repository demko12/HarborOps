using System.Text.RegularExpressions;
using HarborOps.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortCallClearanceMigrationTests
{
    private static readonly string[] ClearanceTables =
    [
        "PortCallClearances",
        "PortCallClearanceItems",
        "PortCallClearanceEvents",
    ];

    [Fact]
    public void The_migration_creates_only_the_three_port_call_clearance_tables()
    {
        var created = UpOperations()
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ClearanceTables.Order(StringComparer.Ordinal), created);
        Assert.All(
            UpOperations().OfType<CreateTableOperation>(),
            operation => Assert.Equal("voyages", operation.Schema));
    }

    [Fact]
    public void The_migration_leaves_existing_voyage_and_stop_tables_untouched()
    {
        var touchedTables = UpOperations()
            .Select(GetTableName)
            .Where(table => table is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(touchedTables, table => Assert.Contains(table, ClearanceTables));
    }

    [Fact]
    public void The_migration_backfills_departed_stops_as_completed_and_every_other_stop_as_pending()
    {
        var backfill = Assert.Single(UpOperations().OfType<SqlOperation>()).Sql;

        Assert.Contains(
            "INSERT INTO [voyages].[PortCallClearances]",
            backfill,
            StringComparison.Ordinal);
        Assert.Contains(
            "FROM [voyages].[VoyageStops]",
            backfill,
            StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN [stop].[Status] = N'Departed' THEN N'Completed' ELSE N'Pending' END",
            backfill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE [voyages].[VoyageStops]", backfill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_backfill_skips_stops_that_already_carry_a_clearance()
    {
        var backfill = Assert.Single(UpOperations().OfType<SqlOperation>()).Sql;

        Assert.Contains("WHERE NOT EXISTS", backfill, StringComparison.Ordinal);
        Assert.Contains(
            "[clearance].[VoyageStopId] = [stop].[Id]",
            backfill,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_backfill_fills_every_column_the_migration_creates_on_the_clearance_table()
    {
        var created = UpOperations()
            .OfType<CreateTableOperation>()
            .Single(operation => string.Equals(
                operation.Name,
                "PortCallClearances",
                StringComparison.Ordinal))
            .Columns
            .Select(column => column.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var backfill = Assert.Single(UpOperations().OfType<SqlOperation>()).Sql;
        var columnList = Regex.Match(
            backfill,
            @"INSERT INTO \[voyages\]\.\[PortCallClearances\]\s*\(([^)]*)\)",
            RegexOptions.Singleline).Groups[1].Value;
        var filled = Regex.Matches(columnList, @"\[(?<name>[^\]]+)\]")
            .Select(match => match.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(created, filled);
    }

    [Fact]
    public void The_migration_can_be_rolled_back_by_dropping_the_clearance_tables()
    {
        var dropped = new AddPortCallClearance().DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ClearanceTables.Order(StringComparer.Ordinal), dropped);
    }

    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new AddPortCallClearance().UpOperations;

    private static string? GetTableName(MigrationOperation operation)
    {
        return operation switch
        {
            CreateTableOperation create => create.Name,
            AlterTableOperation alter => alter.Name,
            DropTableOperation drop => drop.Name,
            AddColumnOperation addColumn => addColumn.Table,
            AlterColumnOperation alterColumn => alterColumn.Table,
            DropColumnOperation dropColumn => dropColumn.Table,
            CreateIndexOperation createIndex => createIndex.Table,
            DropIndexOperation dropIndex => dropIndex.Table,
            AddForeignKeyOperation addForeignKey => addForeignKey.Table,
            DropForeignKeyOperation dropForeignKey => dropForeignKey.Table,
            _ => null,
        };
    }
}
