using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostStocktakeServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task PostAsync_applies_positive_negative_and_zero_variances_with_traceable_adjustments()
    {
        await using var dbContext = CreateDbContext();
        var surplus = await SeedItemAsync(dbContext, quantity: 10m);
        var shortage = await SeedItemAsync(dbContext, quantity: 10m);
        var exact = await SeedItemAsync(dbContext, quantity: 10m);
        var exactStockToken = exact.StockConcurrencyToken;
        var stocktake = await SeedStocktakeAsync(
            dbContext,
            (surplus, 12.5m, "Unbooked delivery"),
            (shortage, 7.25m, "Damaged"),
            (exact, 10m, null));
        var preview = await PreviewAsync(dbContext, stocktake.Id);

        var response = await PostAsync(dbContext, preview);

        dbContext.ChangeTracker.Clear();
        var storedExact = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(item => item.Id == exact.Id);
        var shortageAdjustment = response.Adjustments.Single(adjustment => adjustment.InventoryItemId == shortage.Id);
        var surplusAdjustment = response.Adjustments.Single(adjustment => adjustment.InventoryItemId == surplus.Id);

        Assert.Equal("Posted", response.Status);
        Assert.Equal(ActorUserId, response.PostedByUserId);
        Assert.Equal(UtcNow, response.PostedUtc);
        Assert.Equal(12.5m, await StoredQuantityAsync(dbContext, surplus.Id));
        Assert.Equal(7.25m, await StoredQuantityAsync(dbContext, shortage.Id));
        Assert.Equal(10m, storedExact.Quantity);
        Assert.Equal(exactStockToken, storedExact.StockConcurrencyToken);
        Assert.Equal(2, response.Adjustments.Count);
        Assert.Equal(stocktake.Id, shortageAdjustment.StocktakeId);
        Assert.Equal(10m, shortageAdjustment.QuantityBefore);
        Assert.Equal(7.25m, shortageAdjustment.CountedQuantity);
        Assert.Equal(-2.75m, shortageAdjustment.QuantityDelta);
        Assert.Equal("Damaged", shortageAdjustment.Reason);
        Assert.Equal(ActorUserId, shortageAdjustment.ActorUserId);
        Assert.Equal(UtcNow, shortageAdjustment.AdjustedUtc);
        Assert.Equal(2.5m, surplusAdjustment.QuantityDelta);
    }

    [Fact]
    public async Task PostAsync_can_count_an_item_down_to_zero()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 4m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 0m, "Empty bin"));

        await PostAsync(dbContext, await PreviewAsync(dbContext, stocktake.Id));

        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_is_applied_exactly_once_when_repeated()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 8m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);

        await PostAsync(dbContext, preview);

        await Assert.ThrowsAsync<StocktakeAlreadyPostedException>(() => PostAsync(dbContext, preview));

        Assert.Equal(8m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Single(await dbContext.Set<InventoryAdjustment>().ToListAsync());
    }

    [Fact]
    public async Task PostAsync_from_a_second_request_cannot_adjust_stock_twice()
    {
        var databaseName = NewDatabaseName();
        Guid itemId;
        Guid stocktakeId;

        await using (var setup = CreateDbContext(databaseName))
        {
            var item = await SeedItemAsync(setup, quantity: 10m);
            var stocktake = await SeedStocktakeAsync(setup, (item, 6m, "Missing"));
            itemId = item.Id;
            stocktakeId = stocktake.Id;
        }

        await using var first = CreateDbContext(databaseName);
        await using var second = CreateDbContext(databaseName);
        var firstPreview = await PreviewAsync(first, stocktakeId);
        var secondPreview = await PreviewAsync(second, stocktakeId);

        await PostAsync(first, firstPreview);

        await Assert.ThrowsAsync<StocktakeAlreadyPostedException>(() => PostAsync(second, secondPreview));

        await using var verification = CreateDbContext(databaseName);

        Assert.Equal(6m, await StoredQuantityAsync(verification, itemId));
        Assert.Single(await verification.Set<InventoryAdjustment>().ToListAsync());
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_receipt_and_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 9m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);
        item.IncreaseStock(4m);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, stocktake.Id, item.Id, expectedQuantity: 14m);
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_when_intermediate_movements_restore_the_same_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 9m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);
        item.IncreaseStock(3m);
        item.ReduceStock(3m);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, stocktake.Id, item.Id, expectedQuantity: 10m);
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_reservation_change()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var workOrder = await SeedWorkOrderAsync(dbContext);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 9m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);

        await new ReplaceWorkOrderPartReservationsService(dbContext, new FixedTimeProvider(FixedNow))
            .ReplaceReservationsAsync(
                workOrder.Id,
                new ReplaceWorkOrderPartReservationsRequest
                {
                    Reservations = [new WorkOrderPartReservationEntry { InventoryItemId = item.Id, Quantity = 2m }],
                },
                CancellationToken.None);

        await Assert.ThrowsAsync<StocktakeStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, stocktake.Id, item.Id, expectedQuantity: 10m);
    }

    [Fact]
    public async Task PostAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 9m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);
        stocktake.RecordChange(ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeConcurrencyException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, stocktake.Id, item.Id, expectedQuantity: 10m);
    }

    [Fact]
    public async Task PostAsync_rolls_back_every_line_when_one_line_is_blocked()
    {
        await using var dbContext = CreateDbContext();
        var valid = await SeedItemAsync(dbContext, quantity: 10m);
        var reserved = await SeedItemAsync(dbContext, quantity: 10m);
        var workOrder = await SeedWorkOrderAsync(dbContext);
        await dbContext.Set<WorkOrderPartReservation>().AddAsync(
            WorkOrderPartReservation.Create(workOrder.Id, reserved.Id, 8m, availableQuantity: 10m, UtcNow));
        await dbContext.SaveChangesAsync();
        var stocktake = await SeedStocktakeAsync(dbContext, (valid, 3m, "Damaged"), (reserved, 5m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);

        var exception = await Assert.ThrowsAsync<StocktakePostingBlockedException>(() =>
            PostAsync(dbContext, preview));

        var blocker = Assert.Single(exception.Blockers);

        Assert.Equal(StocktakeBlockerCodes.BelowReserved, blocker.Code);
        Assert.Equal(reserved.Id, blocker.InventoryItemId);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, valid.Id));
        await AssertNothingPostedAsync(dbContext, stocktake.Id, reserved.Id, expectedQuantity: 10m);
    }

    [Fact]
    public async Task PostAsync_rejects_an_incomplete_count()
    {
        await using var dbContext = CreateDbContext();
        var counted = await SeedItemAsync(dbContext, quantity: 10m);
        var uncounted = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (counted, 4m, "Missing"), (uncounted, null, null));
        var preview = await PreviewAsync(dbContext, stocktake.Id);

        var exception = await Assert.ThrowsAsync<StocktakePostingBlockedException>(() =>
            PostAsync(dbContext, preview));

        Assert.Contains(exception.Blockers, blocker => blocker.Code == StocktakeBlockerCodes.NotCounted);
        await AssertNothingPostedAsync(dbContext, stocktake.Id, counted.Id, expectedQuantity: 10m);
    }

    [Fact]
    public async Task PostAsync_rejects_a_cancelled_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 9m, "Missing"));
        var preview = await PreviewAsync(dbContext, stocktake.Id);
        stocktake.Cancel("Wrong locker", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<StocktakeNotDraftException>(() => PostAsync(dbContext, preview));

        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_validates_the_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostStocktakeValidationException>(() =>
            CreateService(dbContext).PostAsync(
                Guid.NewGuid(),
                new PostStocktakeRequest(),
                ActorUserId,
                CancellationToken.None));

        Assert.Contains(PostStocktakeValidator.PreviewTokenField, exception.Errors.Keys);
    }

    [Fact]
    public async Task Stock_concurrency_token_rejects_a_write_based_on_a_stale_read()
    {
        var databaseName = NewDatabaseName();
        Guid itemId;

        await using (var setup = CreateDbContext(databaseName))
        {
            itemId = (await SeedItemAsync(setup, quantity: 10m)).Id;
        }

        await using var stale = CreateDbContext(databaseName);
        await using var winner = CreateDbContext(databaseName);
        var staleItem = await stale.Set<InventoryItem>().SingleAsync(item => item.Id == itemId);
        var winnerItem = await winner.Set<InventoryItem>().SingleAsync(item => item.Id == itemId);

        // The winner moves stock out and back in: same quantity, different token.
        winnerItem.ReduceStock(2m);
        winnerItem.IncreaseStock(2m);
        await winner.SaveChangesAsync();

        staleItem.ApplyStocktakeCount(8m);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task Posted_balances_feed_the_replenishment_query()
    {
        await using var dbContext = CreateDbContext();
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        var unit = UnitOfMeasure.Create("Piece", "pcs");
        var item = InventoryItem.Create(Guid.NewGuid(), unit.Id, supplier.Id, "Impeller", "PN-IMP", 10m, 5m, 20m);
        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.Set<UnitOfMeasure>().AddAsync(unit);
        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();
        var stocktake = await SeedStocktakeAsync(dbContext, (item, 2m, "Worn out"));

        await PostAsync(dbContext, await PreviewAsync(dbContext, stocktake.Id));

        var replenishment = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(replenishment.Items);

        Assert.Equal(item.Id, row.InventoryItemId);
        Assert.Equal(2m, row.StockOnHand);
    }

    private static Task<StocktakePostingPreview> PreviewAsync(HarborOpsDbContext dbContext, Guid stocktakeId)
    {
        return new GetStocktakePostingPreviewQuery(dbContext, new FixedTimeProvider(FixedNow))
            .GetPreviewAsync(stocktakeId, CancellationToken.None);
    }

    private static Task<PostStocktakeResponse> PostAsync(
        HarborOpsDbContext dbContext,
        StocktakePostingPreview preview)
    {
        return CreateService(dbContext).PostAsync(
            preview.StocktakeId,
            new PostStocktakeRequest
            {
                ExpectedConcurrencyToken = preview.ConcurrencyToken,
                PreviewToken = preview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);
    }

    private static async Task AssertNothingPostedAsync(
        HarborOpsDbContext dbContext,
        Guid stocktakeId,
        Guid inventoryItemId,
        decimal expectedQuantity)
    {
        var stocktake = await dbContext.Set<Stocktake>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stocktakeId);

        Assert.Equal(StocktakeStatus.Draft, stocktake.Status);
        Assert.Equal(expectedQuantity, await StoredQuantityAsync(dbContext, inventoryItemId));
        Assert.Empty(await dbContext.Set<InventoryAdjustment>().AsNoTracking().ToListAsync());
    }

    private static async Task<decimal> StoredQuantityAsync(HarborOpsDbContext dbContext, Guid inventoryItemId)
    {
        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync();
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

    private static PostStocktakeService CreateService(HarborOpsDbContext dbContext)
    {
        return new PostStocktakeService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static string NewDatabaseName()
    {
        return $"HarborOpsUnitTests-{Guid.NewGuid()}";
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? NewDatabaseName())
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
