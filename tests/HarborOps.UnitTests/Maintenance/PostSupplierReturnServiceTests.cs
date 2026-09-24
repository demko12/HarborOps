using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class PostSupplierReturnServiceTests
{
    [Fact]
    public async Task PostAsync_partially_returns_stock_with_traceable_movements()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 8m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2.375m));
        var returnLineId = await dbContext.Set<SupplierReturnLine>().Select(line => line.Id).SingleAsync();
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var response = await PostAsync(dbContext, preview);

        var movement = Assert.Single(response.Movements);

        Assert.Equal("Posted", response.Status);
        Assert.False(response.AlreadyPosted);
        Assert.Equal(ActorUserId, response.PostedByUserId);
        Assert.Equal(UtcNow, response.PostedUtc);
        Assert.NotEqual(preview.ConcurrencyToken, response.ConcurrencyToken);
        Assert.Equal(7.625m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(item.Id, movement.InventoryItemId);
        Assert.Equal(supplierReturn.Id, movement.SupplierReturnId);
        Assert.Equal(returnLineId, movement.SupplierReturnLineId);
        Assert.Equal(10m, movement.QuantityBefore);
        Assert.Equal(7.625m, movement.QuantityAfter);
        Assert.Equal(-2.375m, movement.QuantityDelta);
        Assert.Equal(Reason, movement.Reason);
        Assert.Equal(ActorUserId, movement.ActorUserId);
        Assert.Equal(UtcNow, movement.MovedUtc);
    }

    [Fact]
    public async Task PostAsync_can_return_the_full_received_quantity_down_to_zero_stock()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 5m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 5m));

        await PostAsync(dbContext, await PreviewAsync(dbContext, supplierReturn.Id));

        Assert.Equal(0m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_reduces_a_part_once_and_chains_movements_for_each_line()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var other = await SeedItemAsync(dbContext, quantity: 4m);
        var (order, lines) = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Received,
            (item, 5m),
            (item, 5m),
            (other, 4m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m), (lines[1], 3m), (lines[2], 1m));

        var response = await PostAsync(dbContext, await PreviewAsync(dbContext, supplierReturn.Id));

        var itemMovements = response.Movements.Where(movement => movement.InventoryItemId == item.Id).ToList();

        Assert.Equal(3, response.Movements.Count);
        Assert.Equal(5m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(3m, await StoredQuantityAsync(dbContext, other.Id));
        Assert.Equal(2, itemMovements.Count);
        Assert.Equal(10m, itemMovements[0].QuantityBefore);
        Assert.Equal(itemMovements[0].QuantityAfter, itemMovements[1].QuantityBefore);
        Assert.Equal(5m, itemMovements[1].QuantityAfter);
        Assert.Equal(-5m, itemMovements.Sum(movement => movement.QuantityDelta));
    }

    [Fact]
    public async Task PostAsync_returns_the_existing_result_when_repeated()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var first = await PostAsync(dbContext, preview);
        var retry = await PostAsync(dbContext, preview);

        Assert.False(first.AlreadyPosted);
        Assert.True(retry.AlreadyPosted);
        Assert.Equal(first.ConcurrencyToken, retry.ConcurrencyToken);
        Assert.Equal(first.PostedUtc, retry.PostedUtc);
        Assert.Equal(
            first.Movements.Select(movement => movement.MovementId),
            retry.Movements.Select(movement => movement.MovementId));
        Assert.Equal(8m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(1, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task PostAsync_from_a_second_request_reports_the_first_result_without_moving_stock_again()
    {
        var databaseName = NewDatabaseName();
        Guid itemId;
        Guid supplierReturnId;

        await using (var setup = CreateDbContext(databaseName))
        {
            var item = await SeedItemAsync(setup, quantity: 10m);
            var (order, lines) = await SeedOrderAsync(setup, PurchaseOrderStatus.Received, (item, 5m));
            itemId = item.Id;
            supplierReturnId = (await SeedReturnAsync(setup, order, (lines[0], 4m))).Id;
        }

        await using var first = CreateDbContext(databaseName);
        await using var second = CreateDbContext(databaseName);
        var firstPreview = await PreviewAsync(first, supplierReturnId);

        // The second request has already read the draft and its item when the first one commits.
        await second.Set<SupplierReturn>().SingleAsync(candidate => candidate.Id == supplierReturnId);
        await second.Set<InventoryItem>().SingleAsync(candidate => candidate.Id == itemId);
        var secondPreview = await PreviewAsync(second, supplierReturnId);

        var firstResponse = await PostAsync(first, firstPreview);
        var secondResponse = await PostAsync(second, secondPreview);

        await using var verification = CreateDbContext(databaseName);

        // The in-memory provider has no transactions, so the movement-count guarantee of the rolled-back
        // losing save is covered by the relational tests; here the loser must report the winner's result.
        Assert.True(secondResponse.AlreadyPosted);
        Assert.Equal(firstResponse.ConcurrencyToken, secondResponse.ConcurrencyToken);
        Assert.Equal(firstResponse.PostedUtc, secondResponse.PostedUtc);
        Assert.Equal(6m, await StoredQuantityAsync(verification, itemId));
        Assert.Equal(SupplierReturnStatus.Posted, await StoredStatusAsync(verification, supplierReturnId));
    }

    [Fact]
    public async Task PostAsync_prevents_competing_returns_from_exceeding_the_received_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var firstReturn = await SeedReturnAsync(dbContext, order, (lines[0], 3m));
        var secondReturn = await SeedReturnAsync(dbContext, order, (lines[0], 3m));
        var firstPreview = await PreviewAsync(dbContext, firstReturn.Id);
        var secondPreview = await PreviewAsync(dbContext, secondReturn.Id);

        await PostAsync(dbContext, firstPreview);

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, secondPreview));

        var exception = await Assert.ThrowsAsync<SupplierReturnPostingBlockedException>(async () =>
            await PostAsync(dbContext, await PreviewAsync(dbContext, secondReturn.Id)));

        Assert.Equal(SupplierReturnBlockerCodes.ExceedsReturnable, Assert.Single(exception.Blockers).Code);
        Assert.Equal(17m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(SupplierReturnStatus.Draft, await StoredStatusAsync(dbContext, secondReturn.Id));
    }

    [Fact]
    public async Task PostAsync_allows_cumulative_returns_up_to_the_received_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 2.5m));
        var last = await SeedReturnAsync(dbContext, order, (lines[0], 2.5m));

        await PostAsync(dbContext, await PreviewAsync(dbContext, last.Id));

        Assert.Equal(15m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rolls_back_every_line_when_reserved_stock_blocks_one_part()
    {
        await using var dbContext = CreateDbContext();
        var free = await SeedItemAsync(dbContext, quantity: 10m);
        var reserved = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (free, 5m), (reserved, 5m));
        await SeedReservationAsync(dbContext, reserved, 8m);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 5m), (lines[1], 5m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var exception = await Assert.ThrowsAsync<SupplierReturnPostingBlockedException>(() =>
            PostAsync(dbContext, preview));

        var blocker = Assert.Single(exception.Blockers);

        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal(reserved.Id, blocker.InventoryItemId);
        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, free.Id));
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, reserved.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_receipt()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var (nextOrder, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 3m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        await new ChangePurchaseOrderStatusService(dbContext).ReceiveAsync(nextOrder.Id, CancellationToken.None);

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(13m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_part_usage()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var workOrder = await SeedWorkOrderAsync(dbContext);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        await new RecordInventoryItemUsageService(dbContext, Clock).RecordUsageAsync(
            workOrder.Id,
            item.Id,
            new RecordInventoryItemUsageRequest { QuantityUsed = 1m },
            CancellationToken.None);

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(9m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_reservation_change()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var workOrder = await SeedWorkOrderAsync(dbContext);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        await new ReplaceWorkOrderPartReservationsService(dbContext, Clock).ReplaceReservationsAsync(
            workOrder.Id,
            new ReplaceWorkOrderPartReservationsRequest
            {
                Reservations = [new WorkOrderPartReservationEntry { InventoryItemId = item.Id, Quantity = 9m }],
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_stocktake()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var stocktakeLine = StocktakeLine.Create(stocktake.Id, item, UtcNow);
        stocktakeLine.RecordCount(9m, "Missing", ActorUserId, UtcNow);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(stocktakeLine);
        await dbContext.SaveChangesAsync();
        var stocktakePreview = await new GetStocktakePostingPreviewQuery(dbContext, Clock)
            .GetPreviewAsync(stocktake.Id, CancellationToken.None);
        await new PostStocktakeService(dbContext, Clock).PostAsync(
            stocktake.Id,
            new PostStocktakeRequest
            {
                ExpectedConcurrencyToken = stocktakePreview.ConcurrencyToken,
                PreviewToken = stocktakePreview.PreviewToken,
            },
            ActorUserId,
            CancellationToken.None);

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(9m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task Posting_a_return_makes_an_open_stocktake_count_stale()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var stocktakeLine = StocktakeLine.Create(stocktake.Id, item, UtcNow);
        stocktakeLine.RecordCount(10m, null, ActorUserId, UtcNow);
        await dbContext.Set<Stocktake>().AddAsync(stocktake);
        await dbContext.Set<StocktakeLine>().AddAsync(stocktakeLine);
        await dbContext.SaveChangesAsync();
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 2m));

        var stocktakePreview = await new GetStocktakePostingPreviewQuery(dbContext, Clock)
            .GetPreviewAsync(stocktake.Id, CancellationToken.None);

        Assert.Contains(
            stocktakePreview.Blockers,
            blocker => blocker.Code == StocktakeBlockerCodes.StaleBaseline);
    }

    [Fact]
    public async Task PostAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);
        supplierReturn.RecordChange(ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<SupplierReturnConcurrencyException>(() => PostAsync(dbContext, preview));

        await AssertNothingPostedAsync(dbContext, supplierReturn.Id);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_cancelled_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);
        supplierReturn.Cancel("Supplier refused", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<SupplierReturnNotDraftException>(() => PostAsync(dbContext, preview));

        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task PostAsync_leaves_the_purchase_order_and_reservations_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        await SeedReservationAsync(dbContext, item, 4m);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 5m));

        await PostAsync(dbContext, await PreviewAsync(dbContext, supplierReturn.Id));

        dbContext.ChangeTracker.Clear();
        var storedOrder = await dbContext.Set<PurchaseOrder>().SingleAsync(candidate => candidate.Id == order.Id);
        var storedLine = await dbContext.Set<PurchaseOrderLine>().SingleAsync(candidate => candidate.Id == lines[0].Id);
        var reservation = await dbContext.Set<WorkOrderPartReservation>().SingleAsync();

        Assert.Equal(PurchaseOrderStatus.Received, storedOrder.Status);
        Assert.Equal(5m, storedLine.QuantityOrdered);
        Assert.Equal(4m, reservation.QuantityReserved);
        Assert.Equal(5m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task Posted_returns_feed_the_replenishment_query()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedItemAsync(dbContext, quantity: 6m, minimumQuantity: 5m, supplierId: supplier.Id);
        var (order, lines) = await SeedOrderAsync(dbContext, supplier, PurchaseOrderStatus.Received, (item, 6m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 3m));

        await PostAsync(dbContext, await PreviewAsync(dbContext, supplierReturn.Id));

        var replenishment = await new GetInventoryReplenishmentQuery(dbContext).GetReplenishmentAsync(
            new GetInventoryReplenishmentRequest(),
            CancellationToken.None);

        var row = Assert.Single(replenishment.Items);

        Assert.Equal(item.Id, row.InventoryItemId);
        Assert.Equal(3m, row.StockOnHand);
    }

    [Fact]
    public async Task PostAsync_rejects_an_unknown_return()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SupplierReturnNotFoundException>(() =>
            new PostSupplierReturnService(dbContext, Clock).PostAsync(
                Guid.NewGuid(),
                new PostSupplierReturnRequest { ExpectedConcurrencyToken = Guid.NewGuid(), PreviewToken = "ABC" },
                ActorUserId,
                CancellationToken.None));
    }

    [Fact]
    public async Task PostAsync_validates_the_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostSupplierReturnValidationException>(() =>
            new PostSupplierReturnService(dbContext, Clock).PostAsync(
                Guid.NewGuid(),
                new PostSupplierReturnRequest(),
                ActorUserId,
                CancellationToken.None));

        Assert.Contains(PostSupplierReturnValidator.PreviewTokenField, exception.Errors.Keys);
    }

    private static async Task AssertNothingPostedAsync(HarborOpsDbContext dbContext, Guid supplierReturnId)
    {
        Assert.Equal(SupplierReturnStatus.Draft, await StoredStatusAsync(dbContext, supplierReturnId));
        Assert.Equal(0, await MovementCountAsync(dbContext));
    }
}
