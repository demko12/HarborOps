using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreateStocktakeServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task CreateAsync_creates_a_draft_with_one_baseline_line_per_item()
    {
        await using var dbContext = CreateDbContext();
        var filter = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 12.5m);
        var gasket = await SeedItemAsync(dbContext, "Gasket", "PN-2", quantity: 3m);

        var details = await CreateService(dbContext).CreateAsync(
            new CreateStocktakeRequest { InventoryItemIds = [gasket.Id, filter.Id], Notes = "  Locker A  " },
            ActorUserId,
            CancellationToken.None);

        var storedLines = await dbContext.Set<StocktakeLine>().AsNoTracking().ToListAsync();

        Assert.Equal("Draft", details.Status);
        Assert.Equal("Locker A", details.Notes);
        Assert.NotEqual(Guid.Empty, details.ConcurrencyToken);
        Assert.Equal(ActorUserId, details.CreatedByUserId);
        Assert.Equal(UtcNow, details.CreatedUtc);
        Assert.Collection(
            details.Lines,
            line =>
            {
                Assert.Equal(filter.Id, line.InventoryItemId);
                Assert.Equal(12.5m, line.BaselineQuantity);
                Assert.Equal("pcs", line.UnitAbbreviation);
                Assert.Null(line.CountedQuantity);
            },
            line => Assert.Equal(gasket.Id, line.InventoryItemId));
        Assert.Empty(details.Adjustments);
        Assert.Equal(
            filter.StockConcurrencyToken,
            storedLines.Single(line => line.InventoryItemId == filter.Id).BaselineStockConcurrencyToken);
    }

    [Fact]
    public async Task CreateAsync_does_not_change_inventory()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 7m);
        var stockToken = item.StockConcurrencyToken;

        await CreateService(dbContext).CreateAsync(
            new CreateStocktakeRequest { InventoryItemIds = [item.Id] },
            ActorUserId,
            CancellationToken.None);

        var stored = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal(7m, stored.Quantity);
        Assert.Equal(stockToken, stored.StockConcurrencyToken);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_items()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 1m);

        var exception = await Assert.ThrowsAsync<CreateStocktakeValidationException>(() =>
            CreateService(dbContext).CreateAsync(
                new CreateStocktakeRequest { InventoryItemIds = [item.Id, item.Id] },
                ActorUserId,
                CancellationToken.None));

        Assert.Contains(CreateStocktakeValidator.InventoryItemIdsField, exception.Errors.Keys);
        Assert.Empty(await dbContext.Set<Stocktake>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_reports_unknown_items()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, "Filter", "PN-1", quantity: 1m);
        var unknownId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<StocktakeItemsNotFoundException>(() =>
            CreateService(dbContext).CreateAsync(
                new CreateStocktakeRequest { InventoryItemIds = [item.Id, unknownId] },
                ActorUserId,
                CancellationToken.None));

        Assert.Equal([unknownId], exception.InventoryItemIds);
        Assert.Empty(await dbContext.Set<Stocktake>().ToListAsync());
    }

    private static async Task<InventoryItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        string name,
        string partNumber,
        decimal quantity)
    {
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            unit.Id,
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity: 1m,
            unitCost: 10m);

        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static CreateStocktakeService CreateService(HarborOpsDbContext dbContext)
    {
        return new CreateStocktakeService(dbContext, new FixedTimeProvider(FixedNow));
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
