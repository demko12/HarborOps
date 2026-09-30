using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

// Supplier returns are capped by what was actually received (receipts plus any legacy balance) net of
// posted returns, never by the ordered quantity.
public sealed class SupplierReturnReceivedQuantityTests
{
    [Fact]
    public async Task CreateAsync_accepts_a_partially_received_order_up_to_the_received_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 4m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));

        var details = await CreateReturnAsync(dbContext, order, (lines[0], 4m));

        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), details.PurchaseOrderStatus);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(4m, line.RemainingReturnableQuantity);
        Assert.Empty(details.Blockers);
    }

    [Fact]
    public async Task CreateAsync_rejects_returning_more_than_was_received()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 4.001m)));

        Assert.Equal(["lines[0].quantity"], exception.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_line_with_nothing_received_yet()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var other = await SeedItemAsync(dbContext, quantity: 10m, name: "Impeller");
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m), (other, 2m, 0m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 1m), (lines[1], 1m)));

        Assert.Equal(["lines[1].quantity"], exception.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_ordered_order_before_its_first_receipt()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        await Assert.ThrowsAsync<SupplierReturnPurchaseOrderNotReceivedException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 1m)));
    }

    [Fact]
    public async Task PostAsync_nets_earlier_posted_returns_against_the_received_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));
        var first = await SeedReturnAsync(dbContext, order, (lines[0], 3m));
        var second = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        await PostAsync(dbContext, await PreviewAsync(dbContext, first.Id));

        var preview = await PreviewAsync(dbContext, second.Id);

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(SupplierReturnBlockerCodes.ExceedsReturnable, blocker.Code);
        Assert.Contains("received 4.000, already returned 3.000", blocker.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<SupplierReturnPostingBlockedException>(() => PostAsync(dbContext, preview));
        Assert.Equal(7m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_does_not_reduce_gross_received_or_reopen_outstanding_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 4m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var details = await new GetPurchaseOrderDetailsQuery(dbContext, Clock)
            .GetDetailsAsync(order.Id, CancellationToken.None);
        var remaining = await new PostPurchaseOrderReceiptService(dbContext, Clock)
            .ReceiveRemainingAsync(order.Id, null, ActorUserId, CancellationToken.None);

        var line = Assert.Single(details.Lines);

        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), details.Status);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal(6m, line.OutstandingQuantity);
        Assert.Equal(3m, line.ReturnableQuantity);
        Assert.Equal(6m, remaining.Receipt.TotalQuantity);
        Assert.Equal(9m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_still_protects_reserved_stock_on_a_partially_received_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 4m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));
        var workOrder = await SeedReservationAsync(dbContext, item, 3m);
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));

        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        var blocker = Assert.Single(preview.Blockers);

        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
        await Assert.ThrowsAsync<SupplierReturnPostingBlockedException>(() => PostAsync(dbContext, preview));
        Assert.Equal(4m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task PostAsync_rejects_a_preview_made_stale_by_a_receipt_on_the_same_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 4m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 10m, 4m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));
        var preview = await PreviewAsync(dbContext, supplierReturn.Id);

        await PurchaseOrderReceiptTestData.ReceiveAsync(dbContext, order.Id, (lines[0], 2m));

        await Assert.ThrowsAsync<SupplierReturnStalePreviewException>(() => PostAsync(dbContext, preview));

        var fresh = await PreviewAsync(dbContext, supplierReturn.Id);

        Assert.Equal(6m, Assert.Single(fresh.Lines).ReceivedQuantity);
        Assert.Equal(6m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task Returns_are_capped_per_order_line_when_a_part_repeats()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedPartiallyReceivedOrderAsync(dbContext, (item, 5m, 5m), (item, 5m, 2m));

        var withinCeilings = await CreateReturnAsync(dbContext, order, (lines[0], 5m), (lines[1], 2m));
        var aboveSecondCeiling = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 1m), (lines[1], 2.001m)));

        Assert.Empty(withinCeilings.Blockers);
        Assert.Equal(["lines[1].quantity"], aboveSecondCeiling.Errors.Keys);
    }

    [Fact]
    public async Task Legacy_received_orders_remain_returnable_with_existing_returns_counted()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await PurchaseOrderTestData.SeedLegacyReceivedAsync(dbContext, order, lines);
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 4m));

        var withinBalance = await CreateReturnAsync(dbContext, order, (lines[0], 6m));
        var aboveBalance = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(
            () => CreateReturnAsync(dbContext, order, (lines[0], 6.001m)));
        var details = await new GetPurchaseOrderDetailsQuery(dbContext, Clock)
            .GetDetailsAsync(order.Id, CancellationToken.None);

        var line = Assert.Single(details.Lines);

        Assert.Equal(6m, Assert.Single(withinBalance.Lines).RemainingReturnableQuantity);
        Assert.Equal(["lines[0].quantity"], aboveBalance.Errors.Keys);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), details.Status);
        Assert.Equal(0, details.ReceiptCount);
        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(10m, line.LegacyReceivedQuantity);
        Assert.Equal(4m, line.ReturnedQuantity);
        Assert.Equal(0m, line.OutstandingQuantity);
        Assert.Equal(6m, line.ReturnableQuantity);
        Assert.Equal(6m, await StoredQuantityAsync(dbContext, item.Id));
    }

    private static Task<SupplierReturnDetails> CreateReturnAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        params (PurchaseOrderLine Line, decimal Quantity)[] lines)
    {
        return new CreateSupplierReturnService(dbContext, Clock).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = order.Id,
                Lines = [.. lines.Select(line => Entry(line.Line, line.Quantity))],
            },
            ActorUserId,
            CancellationToken.None);
    }
}
