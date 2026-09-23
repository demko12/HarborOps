using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CancelStocktakeServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task CancelAsync_cancels_a_draft_without_changing_inventory()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, item, counted: 2m, reason: "Theft");

        var details = await CreateService(dbContext).CancelAsync(
            stocktake.Id,
            new CancelStocktakeRequest
            {
                ExpectedConcurrencyToken = stocktake.ConcurrencyToken,
                Reason = " Counted the wrong locker ",
            },
            ActorUserId,
            CancellationToken.None);

        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal("Cancelled", details.Status);
        Assert.Equal("Counted the wrong locker", details.CancellationReason);
        Assert.Equal(ActorUserId, details.CancelledByUserId);
        Assert.Equal(UtcNow, details.CancelledUtc);
        Assert.Equal(10m, storedItem.Quantity);
        Assert.Empty(await dbContext.Set<InventoryAdjustment>().ToListAsync());
    }

    [Fact]
    public async Task CancelAsync_requires_a_reason()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, item, counted: 10m, reason: null);

        var exception = await Assert.ThrowsAsync<CancelStocktakeValidationException>(() =>
            CreateService(dbContext).CancelAsync(
                stocktake.Id,
                new CancelStocktakeRequest { ExpectedConcurrencyToken = stocktake.ConcurrencyToken, Reason = " " },
                ActorUserId,
                CancellationToken.None));

        Assert.Contains(CancelStocktakeValidator.ReasonField, exception.Errors.Keys);
    }

    [Fact]
    public async Task CancelAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, item, counted: 10m, reason: null);

        await Assert.ThrowsAsync<StocktakeConcurrencyException>(() =>
            CreateService(dbContext).CancelAsync(
                stocktake.Id,
                new CancelStocktakeRequest { ExpectedConcurrencyToken = Guid.NewGuid(), Reason = "Duplicate" },
                ActorUserId,
                CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_rejects_a_posted_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedCountedStocktakeAsync(dbContext, item, counted: 10m, reason: null);
        var lines = await dbContext.Set<StocktakeLine>().ToListAsync();
        stocktake.MarkPosted(lines, ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeNotDraftException>(() =>
            CreateService(dbContext).CancelAsync(
                stocktake.Id,
                new CancelStocktakeRequest { ExpectedConcurrencyToken = stocktake.ConcurrencyToken, Reason = "Undo" },
                ActorUserId,
                CancellationToken.None));
    }

    private static async Task<Stocktake> SeedCountedStocktakeAsync(
        HarborOpsDbContext dbContext,
        InventoryItem item,
        decimal counted,
        string? reason)
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var line = StocktakeLine.Create(stocktake.Id, item, UtcNow);
        line.RecordCount(counted, reason, ActorUserId, UtcNow);

        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(line);
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

    private static CancelStocktakeService CreateService(HarborOpsDbContext dbContext)
    {
        return new CancelStocktakeService(dbContext, new FixedTimeProvider(FixedNow));
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
