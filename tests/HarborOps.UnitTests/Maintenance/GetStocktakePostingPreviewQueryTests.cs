using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetStocktakePostingPreviewQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task GetPreviewAsync_allows_posting_a_complete_count()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 12m, "Found in spare locker"));

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        var line = Assert.Single(preview.Lines);

        Assert.True(preview.CanPost);
        Assert.Empty(preview.Blockers);
        Assert.Equal(stocktake.ConcurrencyToken, preview.ConcurrencyToken);
        Assert.False(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.Equal(UtcNow, preview.GeneratedUtc);
        Assert.Equal(10m, line.BaselineQuantity);
        Assert.Equal(10m, line.CurrentQuantity);
        Assert.Equal(12m, line.CountedQuantity);
        Assert.Equal(2m, line.Variance);
        Assert.Equal(0m, line.OutstandingReservedQuantity);
        Assert.False(line.BaselineIsStale);
    }

    [Fact]
    public async Task GetPreviewAsync_never_changes_inventory()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stockToken = item.StockConcurrencyToken;
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 3m, "Leak"));

        await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        var stored = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync();

        Assert.Equal(10m, stored.Quantity);
        Assert.Equal(stockToken, stored.StockConcurrencyToken);
        Assert.Empty(await dbContext.Set<InventoryAdjustment>().ToListAsync());
    }

    [Fact]
    public async Task GetPreviewAsync_returns_the_same_token_while_nothing_changes()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 10m, null));
        var query = CreateQuery(dbContext);

        var first = await query.GetPreviewAsync(stocktake.Id, CancellationToken.None);
        var second = await query.GetPreviewAsync(stocktake.Id, CancellationToken.None);

        Assert.Equal(first.PreviewToken, second.PreviewToken);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_uncounted_lines()
    {
        await using var dbContext = CreateDbContext();
        var counted = await SeedItemAsync(dbContext, quantity: 10m);
        var uncounted = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (counted, 10m, null), (uncounted, null, null));

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(StocktakeBlockerCodes.NotCounted, blocker.Code);
        Assert.Equal(uncounted.Id, blocker.InventoryItemId);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_a_count_below_outstanding_reservations()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var workOrder = await SeedWorkOrderAsync(dbContext);
        await SeedReservationAsync(dbContext, workOrder.Id, item.Id, reserved: 6m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 5m, "Shrinkage"));

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(6m, preview.Lines[0].OutstandingReservedQuantity);
        Assert.Equal(StocktakeBlockerCodes.BelowReserved, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
    }

    [Fact]
    public async Task GetPreviewAsync_allows_a_count_equal_to_outstanding_reservations()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var workOrder = await SeedWorkOrderAsync(dbContext);
        await SeedReservationAsync(dbContext, workOrder.Id, item.Id, reserved: 6m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 6m, "Shrinkage"));

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        Assert.True(preview.CanPost);
    }

    [Fact]
    public async Task GetPreviewAsync_ignores_reservations_of_canceled_work_orders()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var workOrder = await SeedWorkOrderAsync(dbContext);
        await SeedReservationAsync(dbContext, workOrder.Id, item.Id, reserved: 6m);
        workOrder.Cancel();
        await dbContext.SaveChangesAsync();
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 1m, "Shrinkage"));

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        Assert.True(preview.CanPost);
        Assert.Equal(0m, preview.Lines[0].OutstandingReservedQuantity);
    }

    [Fact]
    public async Task GetPreviewAsync_flags_a_stale_baseline_after_a_stock_movement()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 10m, null));
        item.IncreaseStock(5m);
        await dbContext.SaveChangesAsync();

        var preview = await CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None);

        Assert.False(preview.CanPost);
        Assert.True(preview.Lines[0].BaselineIsStale);
        Assert.Equal(15m, preview.Lines[0].CurrentQuantity);
        Assert.Contains(preview.Blockers, blocker => blocker.Code == StocktakeBlockerCodes.StaleBaseline);
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_a_cancelled_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 10m, null));
        stocktake.Cancel("Duplicate", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeNotDraftException>(() =>
            CreateQuery(dbContext).GetPreviewAsync(stocktake.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetPreviewAsync_reports_an_unknown_stocktake()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<StocktakeNotFoundException>(() =>
            CreateQuery(dbContext).GetPreviewAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static async Task<Stocktake> SeedStocktakeAsync(
        HarborOpsDbContext dbContext,
        params (InventoryItem Item, decimal? Counted, string? Reason)[] counts)
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

    private static async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(HarborOpsDbContext dbContext)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder;
    }

    private static async Task SeedReservationAsync(
        HarborOpsDbContext dbContext,
        Guid workOrderId,
        Guid inventoryItemId,
        decimal reserved)
    {
        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            inventoryItemId,
            reserved,
            availableQuantity: reserved,
            UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation);
        await dbContext.SaveChangesAsync();
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

    private static GetStocktakePostingPreviewQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetStocktakePostingPreviewQuery(dbContext, new FixedTimeProvider(FixedNow));
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
