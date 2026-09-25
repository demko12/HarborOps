using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetSupplierReturnPostingPreviewQueryTests
{
    [Fact]
    public async Task GetPreviewAsync_aggregates_lines_for_the_same_part_and_allows_posting()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 12m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m), (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m), (lines[1], 3.5m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var previewItem = Assert.Single(preview.Items);

        Assert.True(preview.CanPost);
        Assert.Empty(preview.Blockers);
        Assert.Equal(supplierReturn.ConcurrencyToken, preview.ConcurrencyToken);
        Assert.False(string.IsNullOrWhiteSpace(preview.PreviewToken));
        Assert.Equal(UtcNow, preview.GeneratedUtc);
        Assert.Equal(2, preview.Lines.Count);
        Assert.Equal(item.Id, previewItem.InventoryItemId);
        Assert.Equal(12m, previewItem.OnHandQuantity);
        Assert.Equal(0m, previewItem.ReservedQuantity);
        Assert.Equal(12m, previewItem.AvailableQuantity);
        Assert.Equal(5.5m, previewItem.ReturnQuantity);
        Assert.Equal(6.5m, previewItem.QuantityAfter);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_returns_that_would_consume_reserved_stock()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var workOrder = await SeedReservationAsync(dbContext, item, 7m);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 4m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var blocker = Assert.Single(preview.Blockers);
        var previewItem = Assert.Single(preview.Items);

        Assert.False(preview.CanPost);
        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal(item.Id, blocker.InventoryItemId);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
        Assert.Equal(7m, previewItem.ReservedQuantity);
        Assert.Equal(3m, previewItem.AvailableQuantity);
        Assert.Equal(7m, Assert.Single(preview.Lines).ReservedQuantity);
    }

    [Fact]
    public async Task GetPreviewAsync_allows_returning_exactly_the_unreserved_stock()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        await SeedReservationAsync(dbContext, item, 7m);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 3m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        Assert.True(preview.CanPost);
    }

    [Fact]
    public async Task GetPreviewAsync_ignores_reservations_on_closed_work_orders_and_consumed_quantities()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var completed = await SeedReservationAsync(dbContext, item, 6m);
        completed.Complete(UtcNow);
        var cancelled = await SeedReservationAsync(dbContext, item, 6m);
        cancelled.Cancel();
        var partlyConsumed = await SeedReservationAsync(dbContext, item, 4m);
        var reservation = await dbContext.Set<WorkOrderPartReservation>()
            .SingleAsync(candidate => candidate.WorkOrderId == partlyConsumed.Id);
        reservation.Consume(3m, UtcNow);
        await dbContext.SaveChangesAsync();
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 9m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        Assert.True(preview.CanPost);
        Assert.Equal(1m, Assert.Single(preview.Items).ReservedQuantity);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_returns_above_the_on_hand_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 2m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 3m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        Assert.Equal(SupplierReturnBlockerCodes.InsufficientStock, Assert.Single(preview.Blockers).Code);
    }

    [Fact]
    public async Task GetPreviewAsync_checks_stock_against_the_total_for_a_part_across_lines()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 6m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m), (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 4m), (lines[1], 4m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var blocker = Assert.Single(preview.Blockers);

        Assert.Equal(SupplierReturnBlockerCodes.InsufficientStock, blocker.Code);
        Assert.Equal(item.Id, blocker.InventoryItemId);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_a_draft_overtaken_by_another_posted_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var draft = await SeedReturnAsync(dbContext, order, (lines[0], 3m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 3m));

        var preview = await PreviewAsync(dbContext, draft.Id);

        var blocker = Assert.Single(preview.Blockers);
        var line = Assert.Single(preview.Lines);

        Assert.Equal(SupplierReturnBlockerCodes.ExceedsReturnable, blocker.Code);
        Assert.Equal(lines[0].Id, blocker.PurchaseOrderLineId);
        Assert.Equal(5m, line.ReceivedQuantity);
        Assert.Equal(3m, line.PreviouslyReturnedQuantity);
        Assert.Equal(2m, line.RemainingReturnableQuantity);
    }

    [Fact]
    public async Task GetPreviewAsync_changes_its_token_when_stock_moves()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));
        var before = await PreviewAsync(dbContext, supplierReturn.Id);

        item.IncreaseStock(1m);
        item.ReduceStock(1m);
        await dbContext.SaveChangesAsync();

        var after = await PreviewAsync(dbContext, supplierReturn.Id);

        Assert.NotEqual(before.PreviewToken, after.PreviewToken);
    }

    [Fact]
    public async Task GetPreviewAsync_does_not_change_anything()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1m));
        var token = supplierReturn.ConcurrencyToken;

        await PreviewAsync(dbContext, supplierReturn.Id);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.Set<SupplierReturn>().SingleAsync();

        Assert.Equal(token, stored.ConcurrencyToken);
        Assert.Equal(SupplierReturnStatus.Draft, stored.Status);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_a_posted_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var posted = await SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        await Assert.ThrowsAsync<SupplierReturnNotDraftException>(() => PreviewAsync(dbContext, posted.Id));
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_an_unknown_return()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SupplierReturnNotFoundException>(() => PreviewAsync(dbContext, Guid.NewGuid()));
    }
}
