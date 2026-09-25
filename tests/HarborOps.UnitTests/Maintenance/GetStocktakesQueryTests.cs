using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetStocktakesQueryTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task GetStocktakesAsync_returns_summary_counts()
    {
        await using var dbContext = CreateDbContext();
        var counted = CreateItem(10m);
        var uncounted = CreateItem(10m);
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var countedLine = StocktakeLine.Create(stocktake.Id, counted, UtcNow);
        countedLine.RecordCount(9m, "Missing", ActorUserId, UtcNow);
        await dbContext.Set<InventoryItem>().AddRangeAsync(counted, uncounted);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddRangeAsync(
            countedLine,
            StocktakeLine.Create(stocktake.Id, uncounted, UtcNow));
        await dbContext.SaveChangesAsync();

        var response = await new GetStocktakesQuery(dbContext).GetStocktakesAsync(
            new GetStocktakesRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(stocktake.Id, row.StocktakeId);
        Assert.Equal("Draft", row.Status);
        Assert.Equal(2, row.LineCount);
        Assert.Equal(1, row.CountedLineCount);
        Assert.Equal(1, row.VarianceLineCount);
        Assert.Equal(stocktake.ConcurrencyToken, row.ConcurrencyToken);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(GetStocktakesValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(GetStocktakesValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task GetStocktakesAsync_filters_by_status_and_pages_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var oldest = Stocktake.Create(null, ActorUserId, UtcNow.AddDays(-2));
        var middle = Stocktake.Create(null, ActorUserId, UtcNow.AddDays(-1));
        var newest = Stocktake.Create(null, ActorUserId, UtcNow);
        var cancelled = Stocktake.Create(null, ActorUserId, UtcNow.AddDays(1));
        cancelled.Cancel("Duplicate", ActorUserId, UtcNow.AddDays(1));
        await dbContext.Set<Stocktake>().AddRangeAsync(oldest, middle, newest, cancelled);
        await dbContext.SaveChangesAsync();

        var query = new GetStocktakesQuery(dbContext);

        var firstPage = await query.GetStocktakesAsync(
            new GetStocktakesRequest { Status = "draft", PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await query.GetStocktakesAsync(
            new GetStocktakesRequest { Status = "draft", PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal([newest.Id, middle.Id], firstPage.Items.Select(row => row.StocktakeId));
        Assert.Equal([oldest.Id], secondPage.Items.Select(row => row.StocktakeId));
    }

    [Fact]
    public async Task GetStocktakesAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetStocktakesValidationException>(() =>
            new GetStocktakesQuery(dbContext).GetStocktakesAsync(
                new GetStocktakesRequest { Status = "Archived" },
                CancellationToken.None));

        Assert.Contains(GetStocktakesValidator.StatusField, exception.Errors.Keys);
    }

    private static InventoryItem CreateItem(decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
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
