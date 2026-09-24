using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetStocktakeDetailsQueryTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task GetDetailsAsync_returns_lines_ordered_by_name_with_units()
    {
        await using var dbContext = CreateDbContext();
        var unit = UnitOfMeasure.Create("Litre", "l");
        var oil = CreateItem(unit.Id, "Lube oil", 40.5m);
        var filter = CreateItem(unit.Id, "Filter", 3m);
        var stocktake = Stocktake.Create("Engine room", ActorUserId, UtcNow);
        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddRangeAsync(oil, filter);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddRangeAsync(
            StocktakeLine.Create(stocktake.Id, oil, UtcNow),
            StocktakeLine.Create(stocktake.Id, filter, UtcNow));
        await dbContext.SaveChangesAsync();

        var details = await new GetStocktakeDetailsQuery(dbContext).GetDetailsAsync(
            stocktake.Id,
            CancellationToken.None);

        Assert.Equal("Draft", details.Status);
        Assert.Equal("Engine room", details.Notes);
        Assert.Equal(stocktake.ConcurrencyToken, details.ConcurrencyToken);
        Assert.Equal(["Filter", "Lube oil"], details.Lines.Select(line => line.Name));
        Assert.All(details.Lines, line => Assert.Equal("l", line.UnitAbbreviation));
        Assert.Equal(40.5m, details.Lines[1].BaselineQuantity);
        Assert.Empty(details.Adjustments);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_the_posted_history_with_adjustments()
    {
        await using var dbContext = CreateDbContext();
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = CreateItem(unit.Id, "Impeller", 10m);
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var line = StocktakeLine.Create(stocktake.Id, item, UtcNow);
        line.RecordCount(7m, "Missing", ActorUserId, UtcNow);
        stocktake.MarkPosted([line], ActorUserId, UtcNow);
        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(line);
        await dbContext.Set<InventoryAdjustment>().AddAsync(
            InventoryAdjustment.CreateFromStocktake(item.Id, stocktake.Id, 10m, 7m, "Missing", ActorUserId, UtcNow));
        await dbContext.SaveChangesAsync();

        var details = await new GetStocktakeDetailsQuery(dbContext).GetDetailsAsync(
            stocktake.Id,
            CancellationToken.None);

        var adjustment = Assert.Single(details.Adjustments);

        Assert.Equal("Posted", details.Status);
        Assert.Equal(ActorUserId, details.PostedByUserId);
        Assert.Equal(UtcNow, details.PostedUtc);
        Assert.Equal(10m, adjustment.QuantityBefore);
        Assert.Equal(7m, adjustment.CountedQuantity);
        Assert.Equal(-3m, adjustment.QuantityDelta);
        Assert.Equal("Missing", adjustment.Reason);
    }

    [Fact]
    public async Task GetDetailsAsync_reports_an_unknown_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var stocktakeId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<StocktakeNotFoundException>(() =>
            new GetStocktakeDetailsQuery(dbContext).GetDetailsAsync(stocktakeId, CancellationToken.None));

        Assert.Equal(stocktakeId, exception.StocktakeId);
    }

    private static InventoryItem CreateItem(Guid unitId, string name, decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            unitId,
            Guid.NewGuid(),
            name,
            $"PN-{Guid.NewGuid():N}",
            quantity,
            1m,
            10m);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
