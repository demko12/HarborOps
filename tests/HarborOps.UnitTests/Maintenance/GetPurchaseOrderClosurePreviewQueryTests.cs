using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderClosureTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderClosurePreviewQueryTests
{
    [Fact]
    public async Task GetPreviewAsync_shows_received_and_undelivered_quantities_without_changing_anything()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        var stored = await StoredOrderAsync(dbContext, order.Id);

        var preview = await PreviewAsync(dbContext, order.Id);

        var line = Assert.Single(preview.Lines);

        Assert.True(preview.CanClose);
        Assert.Empty(preview.Blockers);
        Assert.Equal(order.Id, preview.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), preview.OrderStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), preview.ResultingOrderStatus);
        Assert.Equal(stored.ConcurrencyToken, preview.ConcurrencyToken);
        Assert.Equal(64, preview.PreviewToken.Length);
        Assert.Equal(UtcNow, preview.GeneratedUtc);
        Assert.Equal(4m, preview.TotalQuantityReceived);
        Assert.Equal(6m, preview.TotalQuantityToClose);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(item.Id, line.InventoryItemId);
        Assert.Equal("Main engine filter", line.Name);
        Assert.Equal(item.PartNumber, line.PartNumber);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(0m, line.ReturnedQuantity);
        Assert.Equal(6m, line.QuantityToClose);

        await using var verification = CreateDbContext(databaseName);
        var storedLine = Assert.Single((await StoredLinesAsync(verification, order.Id)).Values);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await StoredOrderAsync(verification, order.Id)).Status);
        Assert.Equal(0m, storedLine.ClosedQuantity);
        Assert.Equal(6m, storedLine.OutstandingQuantity);
        Assert.Equal(4m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(0, await ClosureCountAsync(verification));
    }

    [Fact]
    public async Task GetPreviewAsync_lists_every_line_including_fully_received_ones_and_shared_items()
    {
        await using var dbContext = CreateDbContext();
        var filter = await SeedItemAsync(dbContext, quantity: 0m, name: "Fuel filter");
        var impeller = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedReceivedAsync(
            dbContext,
            (filter, 10m, 4m),
            (filter, 5m, 5m),
            (impeller, 3m, 0m));

        var preview = await PreviewAsync(dbContext, order.Id);

        Assert.Equal(3, preview.Lines.Count);
        Assert.Equal(9m, preview.TotalQuantityReceived);
        Assert.Equal(9m, preview.TotalQuantityToClose);
        Assert.Equal(6m, preview.Lines.Single(line => line.PurchaseOrderLineId == lines[0].Id).QuantityToClose);
        Assert.Equal(0m, preview.Lines.Single(line => line.PurchaseOrderLineId == lines[1].Id).QuantityToClose);
        Assert.Equal(3m, preview.Lines.Single(line => line.PurchaseOrderLineId == lines[2].Id).QuantityToClose);
        Assert.Equal(
            ["Fuel filter", "Fuel filter", "Impeller"],
            preview.Lines.Select(line => line.Name));
    }

    [Fact]
    public async Task GetPreviewAsync_reports_posted_returns_without_reducing_what_was_received()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var preview = await PreviewAsync(dbContext, order.Id);

        var line = Assert.Single(preview.Lines);

        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.QuantityToClose);
    }

    [Fact]
    public async Task GetPreviewAsync_returns_a_stable_token_until_quantities_change()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));

        var first = await PreviewAsync(dbContext, order.Id);
        var repeated = await PreviewAsync(dbContext, order.Id);
        await PurchaseOrderReceiptTestData.ReceiveAsync(dbContext, order.Id, (lines[0], 1m));
        var afterReceipt = await PreviewAsync(dbContext, order.Id);
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));
        var afterReturn = await PreviewAsync(dbContext, order.Id);

        Assert.Equal(first.PreviewToken, repeated.PreviewToken);
        Assert.NotEqual(first.PreviewToken, afterReceipt.PreviewToken);
        Assert.Equal(5m, afterReceipt.TotalQuantityToClose);
        Assert.NotEqual(afterReceipt.PreviewToken, afterReturn.PreviewToken);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    [InlineData(PurchaseOrderStatus.ClosedShort)]
    public async Task GetPreviewAsync_blocks_orders_that_cannot_be_closed(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, status, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanClose);
        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, blocker.Code);
        Assert.Equal(status.ToString(), preview.ResultingOrderStatus);
    }

    [Fact]
    public async Task GetPreviewAsync_points_an_order_with_nothing_received_to_cancellation()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id);

        Assert.Contains("cancel it", Assert.Single(preview.Blockers).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_a_partially_received_order_without_a_recorded_receipt()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);

        // Test data reaches PartiallyReceived through the domain only, so no receipt row exists.
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.PartiallyReceived, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id);

        Assert.False(preview.CanClose);
        Assert.Equal(PurchaseOrderClosureBlockerCodes.NoReceipts, Assert.Single(preview.Blockers).Code);
    }

    [Fact]
    public async Task GetPreviewAsync_blocks_a_legacy_received_order_and_leaves_its_balance_alone()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await PurchaseOrderTestData.SeedLegacyReceivedAsync(dbContext, order, lines);

        var preview = await PreviewAsync(dbContext, order.Id);

        var line = Assert.Single(preview.Lines);

        Assert.Equal(PurchaseOrderClosureBlockerCodes.OrderNotClosable, Assert.Single(preview.Blockers).Code);
        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(0m, line.QuantityToClose);
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();
        var purchaseOrderId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => PreviewAsync(dbContext, purchaseOrderId));

        Assert.Equal(purchaseOrderId, exception.PurchaseOrderId);
    }
}
