using HarborOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HarborOps.IntegrationTests.Relational;

/// <summary>
/// Upgrades a database that already holds stocktake history, so the supplier-return migration is
/// proven to keep existing adjustments instead of dropping and recreating their columns.
/// </summary>
public sealed class SupplierReturnMigrationRelationalTests : IAsyncLifetime
{
    private const string StocktakesMigration = "20260923110845_AddMaintenanceStocktakes";

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.GetService<IMigrator>().MigrateAsync(StocktakesMigration);
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
    public async Task Upgrading_keeps_existing_stocktake_adjustments_and_their_counted_quantity()
    {
        var adjustmentId = Guid.NewGuid();
        var stocktakeId = Guid.NewGuid();

        await using (var dbContext = CreateDbContext())
        {
            // Only the history row matters here, so the foreign keys are relaxed for the seed.
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [maintenance].[InventoryAdjustments] NOCHECK CONSTRAINT ALL;");
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [maintenance].[InventoryAdjustments]
                    ([Id], [InventoryItemId], [StocktakeId], [QuantityBefore], [CountedQuantity],
                     [QuantityDelta], [Reason], [ActorUserId], [AdjustedUtc])
                VALUES
                    ({adjustmentId}, {Guid.NewGuid()}, {stocktakeId}, 10.5, 7.125,
                     -3.375, N'Damaged', {Guid.NewGuid()}, {new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc)});
                """);

            await dbContext.Database.MigrateAsync();
        }

        await using var readContext = CreateDbContext();
        var stored = await readContext.Set<HarborOps.Modules.Maintenance.Domain.InventoryAdjustment>()
            .AsNoTracking()
            .SingleAsync(adjustment => adjustment.Id == adjustmentId);

        Assert.Equal(stocktakeId, stored.StocktakeId);
        Assert.Equal(10.5m, stored.QuantityBefore);
        Assert.Equal(7.125m, stored.QuantityAfter);
        Assert.Equal(-3.375m, stored.QuantityDelta);
        Assert.Null(stored.SupplierReturnId);
        Assert.Null(stored.SupplierReturnLineId);
        Assert.Empty(await readContext.Database.GetPendingMigrationsAsync());
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
