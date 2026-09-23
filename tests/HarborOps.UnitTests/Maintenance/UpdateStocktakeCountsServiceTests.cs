using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class UpdateStocktakeCountsServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task UpdateCountsAsync_records_count_variance_and_reason()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);
        var originalToken = stocktake.ConcurrencyToken;

        var details = await CreateService(dbContext).UpdateCountsAsync(
            stocktake.Id,
            RequestFor(originalToken, (item.Id, 8.5m, " Damaged in transit ")),
            ActorUserId,
            CancellationToken.None);

        var line = Assert.Single(details.Lines);

        Assert.Equal(8.5m, line.CountedQuantity);
        Assert.Equal(-1.5m, line.Variance);
        Assert.Equal("Damaged in transit", line.VarianceReason);
        Assert.Equal(ActorUserId, line.CountedByUserId);
        Assert.Equal(UtcNow, line.CountedUtc);
        Assert.NotEqual(originalToken, details.ConcurrencyToken);
        Assert.Equal(ActorUserId, details.LastModifiedByUserId);
    }

    [Fact]
    public async Task UpdateCountsAsync_does_not_change_inventory()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);

        await CreateService(dbContext).UpdateCountsAsync(
            stocktake.Id,
            RequestFor(stocktake.ConcurrencyToken, (item.Id, 4m, "Missing")),
            ActorUserId,
            CancellationToken.None);

        var stored = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal(10m, stored.Quantity);
    }

    [Fact]
    public async Task UpdateCountsAsync_requires_a_reason_for_nonzero_variance()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);

        var exception = await Assert.ThrowsAsync<UpdateStocktakeCountsValidationException>(() =>
            CreateService(dbContext).UpdateCountsAsync(
                stocktake.Id,
                RequestFor(stocktake.ConcurrencyToken, (item.Id, 9m, null)),
                ActorUserId,
                CancellationToken.None));

        Assert.Contains("lines[0].reason", exception.Errors.Keys);
    }

    [Fact]
    public async Task UpdateCountsAsync_rejects_an_item_that_is_not_on_the_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var other = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);

        var exception = await Assert.ThrowsAsync<UpdateStocktakeCountsValidationException>(() =>
            CreateService(dbContext).UpdateCountsAsync(
                stocktake.Id,
                RequestFor(stocktake.ConcurrencyToken, (other.Id, 10m, null)),
                ActorUserId,
                CancellationToken.None));

        Assert.Contains("lines[0].inventoryItemId", exception.Errors.Keys);
    }

    [Fact]
    public async Task UpdateCountsAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);
        var staleToken = stocktake.ConcurrencyToken;
        var service = CreateService(dbContext);

        await service.UpdateCountsAsync(
            stocktake.Id,
            RequestFor(staleToken, (item.Id, 10m, null)),
            ActorUserId,
            CancellationToken.None);

        await Assert.ThrowsAsync<StocktakeConcurrencyException>(() =>
            service.UpdateCountsAsync(
                stocktake.Id,
                RequestFor(staleToken, (item.Id, 9m, "Recount")),
                ActorUserId,
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCountsAsync_rejects_a_cancelled_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, item);
        stocktake.Cancel("Duplicate", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeNotDraftException>(() =>
            CreateService(dbContext).UpdateCountsAsync(
                stocktake.Id,
                RequestFor(stocktake.ConcurrencyToken, (item.Id, 10m, null)),
                ActorUserId,
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCountsAsync_reports_an_unknown_stocktake()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<StocktakeNotFoundException>(() =>
            CreateService(dbContext).UpdateCountsAsync(
                Guid.NewGuid(),
                RequestFor(Guid.NewGuid(), (Guid.NewGuid(), 1m, null)),
                ActorUserId,
                CancellationToken.None));
    }

    private static UpdateStocktakeCountsRequest RequestFor(
        Guid expectedConcurrencyToken,
        params (Guid InventoryItemId, decimal? Counted, string? Reason)[] counts)
    {
        return new UpdateStocktakeCountsRequest
        {
            ExpectedConcurrencyToken = expectedConcurrencyToken,
            Lines = counts
                .Select(count => new StocktakeCountEntry
                {
                    InventoryItemId = count.InventoryItemId,
                    CountedQuantity = count.Counted,
                    Reason = count.Reason,
                })
                .ToList(),
        };
    }

    private static async Task<Stocktake> SeedStocktakeAsync(
        HarborOpsDbContext dbContext,
        params InventoryItem[] items)
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);

        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddRangeAsync(
            items.Select(item => StocktakeLine.Create(stocktake.Id, item, UtcNow)));
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

    private static UpdateStocktakeCountsService CreateService(HarborOpsDbContext dbContext)
    {
        return new UpdateStocktakeCountsService(dbContext, new FixedTimeProvider(FixedNow));
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
