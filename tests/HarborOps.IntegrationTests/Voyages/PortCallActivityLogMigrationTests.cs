using HarborOps.Infrastructure.Persistence;
using HarborOps.Infrastructure.Persistence.Migrations;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class PortCallActivityLogMigrationTests
{
    private static readonly string[] ActivityLogTables =
    [
        "PortCallActivities",
        "PortCallActivityRevisions",
        "PortCallStatementEntries",
        "PortCallStatements",
    ];

    [Fact]
    public void The_migration_creates_only_the_activity_log_tables_in_the_voyages_schema()
    {
        var created = UpOperations()
            .OfType<CreateTableOperation>()
            .ToArray();

        Assert.Equal(ActivityLogTables, created.Select(operation => operation.Name).Order(StringComparer.Ordinal));
        Assert.All(created, operation => Assert.Equal("voyages", operation.Schema));
    }

    [Fact]
    public void The_migration_leaves_existing_tables_untouched()
    {
        var touchedTables = UpOperations()
            .Select(GetTableName)
            .Where(table => table is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(touchedTables, table => Assert.Contains(table, ActivityLogTables));
        Assert.Empty(UpOperations().OfType<SqlOperation>());
    }

    [Fact]
    public void The_database_allows_at_most_one_statement_per_stop()
    {
        var index = Assert.Single(
            UpOperations().OfType<CreateIndexOperation>(),
            operation => operation.Table == "PortCallStatements" && operation.Columns.SequenceEqual(["VoyageStopId"]));

        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData("PortCallActivities", new[] { "VoyageStopId", "Sequence" })]
    [InlineData("PortCallActivityRevisions", new[] { "PortCallActivityId", "Revision" })]
    [InlineData("PortCallStatementEntries", new[] { "PortCallStatementId", "Position" })]
    public void Ordering_keys_are_unique_so_equal_timestamps_stay_deterministic(string table, string[] columns)
    {
        var index = Assert.Single(
            UpOperations().OfType<CreateIndexOperation>(),
            operation => operation.Table == table && operation.Columns.SequenceEqual(columns));

        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData("PortCallActivities")]
    [InlineData("PortCallStatements")]
    public void Mutable_rows_carry_a_database_generated_concurrency_token(string table)
    {
        var column = UpOperations()
            .OfType<CreateTableOperation>()
            .Single(operation => operation.Name == table)
            .Columns
            .Single(candidate => candidate.Name == "ConcurrencyToken");

        Assert.False(column.IsNullable);
        Assert.Equal("NEWID()", column.DefaultValueSql);
    }

    [Fact]
    public void The_model_marks_the_concurrency_tokens_the_services_rely_on()
    {
        using var dbContext = new HarborOpsDbContext(
            new DbContextOptionsBuilder<HarborOpsDbContext>()
                .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
                .Options);

        Assert.True(Token<PortCallActivity>(dbContext).IsConcurrencyToken);
        Assert.True(Token<PortCallStatement>(dbContext).IsConcurrencyToken);
        Assert.Equal("voyages", dbContext.Model.FindEntityType(typeof(PortCallActivityRevision))!.GetSchema());
        Assert.Equal("voyages", dbContext.Model.FindEntityType(typeof(PortCallStatementEntry))!.GetSchema());
    }

    [Fact]
    public void Removing_a_stop_with_an_activity_log_is_restricted_and_history_follows_its_activity()
    {
        var foreignKeys = UpOperations()
            .OfType<CreateTableOperation>()
            .SelectMany(operation => operation.ForeignKeys)
            .ToArray();

        Assert.All(
            foreignKeys.Where(foreignKey => foreignKey.PrincipalTable == "VoyageStops"),
            foreignKey => Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete));
        Assert.Equal(
            ReferentialAction.Cascade,
            foreignKeys.Single(foreignKey => foreignKey.Table == "PortCallActivityRevisions").OnDelete);
    }

    [Fact]
    public void The_migration_can_be_rolled_back_by_dropping_the_activity_log_tables()
    {
        var dropped = new AddPortCallActivityLog().DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ActivityLogTables, dropped);
    }

    private static IProperty Token<TEntity>(DbContext dbContext) =>
        dbContext.Model.FindEntityType(typeof(TEntity))!.FindProperty("ConcurrencyToken")!;

    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new AddPortCallActivityLog().UpOperations;

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
