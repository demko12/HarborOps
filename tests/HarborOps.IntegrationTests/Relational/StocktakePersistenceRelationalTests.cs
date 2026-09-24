using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class StocktakePersistenceRelationalTests : IAsyncLifetime
{
    private static readonly Guid ActorUserId =
        Guid.Parse("2b3c4d5e-6f70-4819-923a-4b5c6d7e8f90");

    private static readonly DateTime UtcNow =
        new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task The_migration_creates_every_stocktake_table_in_the_maintenance_schema()
    {
        await using var dbContext = CreateDbContext();

        var tables = await QueryStringsAsync(
            dbContext,
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'maintenance'");

        Assert.Contains("Stocktakes", tables);
        Assert.Contains("StocktakeLines", tables);
        Assert.Contains("InventoryAdjustments", tables);
    }

    [RelationalFact]
    public async Task Every_quantity_column_keeps_the_existing_three_decimal_places()
    {
        await using var dbContext = CreateDbContext();

        var columns = await QueryStringsAsync(
            dbContext,
            """
            SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME, ':', NUMERIC_PRECISION, ',', NUMERIC_SCALE)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'maintenance'
              AND TABLE_NAME IN ('StocktakeLines', 'InventoryAdjustments')
              AND DATA_TYPE = 'decimal'
            """);

        Assert.NotEmpty(columns);
        Assert.All(columns, column => Assert.EndsWith(":18,3", column, StringComparison.Ordinal));
    }

    [RelationalFact]
    public async Task A_stocktake_adjusts_an_item_at_most_once()
    {
        await using var dbContext = CreateDbContext();

        var indexes = await QueryStringsAsync(
            dbContext,
            """
            SELECT i.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = 'maintenance'
              AND t.name = 'InventoryAdjustments'
              AND i.is_unique = 1
              AND i.is_primary_key = 0
            """);

        Assert.Single(indexes);

        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<InventoryAdjustment>().AddAsync(
            InventoryAdjustment.CreateFromStocktake(item.Id, stocktake.Id, 10m, 8m, "Missing", ActorUserId, UtcNow));
        await dbContext.SaveChangesAsync();

        await using var duplicateContext = CreateDbContext();
        await duplicateContext.Set<InventoryAdjustment>().AddAsync(
            InventoryAdjustment.CreateFromStocktake(item.Id, stocktake.Id, 8m, 6m, "Missing again", ActorUserId, UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task A_stocktake_lists_an_item_only_once()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(StocktakeLine.Create(stocktake.Id, item, UtcNow));
        await dbContext.SaveChangesAsync();

        await using var duplicateContext = CreateDbContext();
        await duplicateContext.Set<StocktakeLine>().AddAsync(StocktakeLine.Create(stocktake.Id, item, UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    [RelationalFact]
    public async Task Both_inventory_tokens_default_to_a_new_identifier()
    {
        await using var dbContext = CreateDbContext();

        var defaults = await QueryStringsAsync(
            dbContext,
            """
            SELECT CONCAT(c.name, ':', OBJECT_DEFINITION(c.default_object_id))
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID('maintenance.InventoryItems')
              AND c.name LIKE '%ConcurrencyToken'
            """);

        Assert.Equal(2, defaults.Count);
        Assert.All(defaults, value => Assert.EndsWith(":(newid())", value, StringComparison.Ordinal));
    }

    [RelationalFact]
    public async Task The_inventory_history_table_carries_both_tokens()
    {
        await using var dbContext = CreateDbContext();

        var columns = await QueryStringsAsync(
            dbContext,
            """
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = (
                    SELECT history_table_id
                    FROM sys.tables
                    WHERE object_id = OBJECT_ID('maintenance.InventoryItems'))
              AND c.name LIKE '%ConcurrencyToken'
            """);

        Assert.Contains("StockConcurrencyToken", columns);
        Assert.Contains("ReservationConcurrencyToken", columns);
    }

    [RelationalFact]
    public async Task A_counted_quantity_round_trips_with_three_decimal_places()
    {
        Guid stocktakeId;

        await using (var writeContext = CreateDbContext())
        {
            var item = await SeedItemAsync(writeContext, quantity: 10.125m);
            var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
            var line = StocktakeLine.Create(stocktake.Id, item, UtcNow);
            line.RecordCount(7.875m, "Damaged", ActorUserId, UtcNow);

            await writeContext.Set<Stocktake>().AddAsync(stocktake);
            await writeContext.Set<StocktakeLine>().AddAsync(line);
            await writeContext.SaveChangesAsync();

            stocktakeId = stocktake.Id;
        }

        await using var readContext = CreateDbContext();
        var stored = await readContext.Set<StocktakeLine>()
            .SingleAsync(line => line.StocktakeId == stocktakeId);

        Assert.Equal(10.125m, stored.BaselineQuantity);
        Assert.Equal(7.875m, stored.CountedQuantity);
        Assert.Equal(-2.250m, stored.Variance);
    }

    [RelationalFact]
    public async Task A_stocktake_line_cannot_reference_an_unknown_item()
    {
        await using var dbContext = CreateDbContext();
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var unknownItem = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ghost part",
            $"PN-{Guid.NewGuid():N}",
            1m,
            1m,
            1m);

        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(StocktakeLine.Create(stocktake.Id, unknownItem, UtcNow));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
    }

    private static async Task<InventoryItem> SeedItemAsync(HarborOpsDbContext dbContext, decimal quantity)
    {
        var category = ItemCategory.Create($"Spares {Guid.NewGuid():N}", null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var supplier = Supplier.Register($"Supplier {Guid.NewGuid():N}", null, null, null, null);
        var item = InventoryItem.Create(
            category.Id,
            unit.Id,
            supplier.Id,
            "Main engine filter",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity: 1m,
            unitCost: 25m);

        await dbContext.AddRangeAsync(category, unit, supplier, item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(
        HarborOpsDbContext dbContext,
        string sql)
    {
        var results = new List<string>();

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await dbContext.Database.OpenConnectionAsync();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!await reader.IsDBNullAsync(0))
            {
                results.Add(reader.GetString(0));
            }
        }

        return results;
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
