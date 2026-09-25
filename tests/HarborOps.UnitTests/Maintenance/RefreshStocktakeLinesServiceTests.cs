using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RefreshStocktakeLinesServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task RefreshAsync_recaptures_the_baseline_and_invalidates_only_the_refreshed_count()
    {
        await using var dbContext = CreateDbContext();
        var moved = await SeedItemAsync(dbContext, quantity: 10m);
        var untouched = await SeedItemAsync(dbContext, quantity: 5m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, (moved, 9m, "Missing"), (untouched, 5m, null));
        var originalToken = stocktake.ConcurrencyToken;
        moved.ReduceStock(2m);
        await dbContext.SaveChangesAsync();

        var details = await CreateService(dbContext).RefreshAsync(
            stocktake.Id,
            new RefreshStocktakeLinesRequest { ExpectedConcurrencyToken = originalToken, InventoryItemIds = [moved.Id] },
            ActorUserId,
            CancellationToken.None);

        var movedLine = details.Lines.Single(line => line.InventoryItemId == moved.Id);
        var untouchedLine = details.Lines.Single(line => line.InventoryItemId == untouched.Id);
        var storedMovedLine = await dbContext.Set<StocktakeLine>()
            .AsNoTracking()
            .SingleAsync(line => line.InventoryItemId == moved.Id);

        Assert.Equal(8m, movedLine.BaselineQuantity);
        Assert.Null(movedLine.CountedQuantity);
        Assert.Null(movedLine.VarianceReason);
        Assert.Equal(moved.StockConcurrencyToken, storedMovedLine.BaselineStockConcurrencyToken);
        Assert.Equal(5m, untouchedLine.CountedQuantity);
        Assert.NotEqual(originalToken, details.ConcurrencyToken);
    }

    [Fact]
    public async Task RefreshAsync_rejects_items_that_are_not_on_the_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, (item, 10m, null));

        var exception = await Assert.ThrowsAsync<RefreshStocktakeLinesValidationException>(() =>
            CreateService(dbContext).RefreshAsync(
                stocktake.Id,
                new RefreshStocktakeLinesRequest
                {
                    ExpectedConcurrencyToken = stocktake.ConcurrencyToken,
                    InventoryItemIds = [Guid.NewGuid()],
                },
                ActorUserId,
                CancellationToken.None));

        Assert.Contains(RefreshStocktakeLinesValidator.InventoryItemIdsField, exception.Errors.Keys);
    }

    [Fact]
    public async Task RefreshAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, (item, 10m, null));
        var staleToken = stocktake.ConcurrencyToken;
        stocktake.RecordChange(ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeConcurrencyException>(() =>
            CreateService(dbContext).RefreshAsync(
                stocktake.Id,
                new RefreshStocktakeLinesRequest { ExpectedConcurrencyToken = staleToken, InventoryItemIds = [item.Id] },
                ActorUserId,
                CancellationToken.None));
    }

    private static async Task<Stocktake> SeedCountedStocktakeAsync(
        HarborOpsDbContext dbContext,
        params (InventoryItem Item, decimal Counted, string? Reason)[] counts)
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var lines = counts
            .Select(count =>
            {
                var line = StocktakeLine.Create(stocktake.Id, count.Item, UtcNow);
                line.RecordCount(count.Counted, count.Reason, ActorUserId, UtcNow);
                return line;
            })
            .ToList();

        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddRangeAsync(lines);
        await dbContext.SaveChangesAsync();

        return stocktake;
    }

    private static async Task<InventoryItem> SeedItemAsync(HarborOpsDbContext dbContext, decimal quantity)
    {
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            Guid.NewGuid(),
            "Main engine filter",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            minimumQuantity: 1m,
            unitCost: 10m);

        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static RefreshStocktakeLinesService CreateService(HarborOpsDbContext dbContext)
    {
        return new RefreshStocktakeLinesService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
