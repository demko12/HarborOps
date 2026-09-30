using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderReceiptTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderReceiptPostingPreviewQueryTests
{
    [Fact]
    public async Task GetPreviewAsync_predicts_the_receipt_without_changing_anything()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedItemAsync(dbContext, quantity: 3m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));

        var line = Assert.Single(preview.Lines);

        Assert.True(preview.CanPost);
        Assert.Empty(preview.Blockers);
        Assert.Equal(order.Id, preview.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), preview.OrderStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), preview.ResultingOrderStatus);
        Assert.Equal(order.ConcurrencyToken, preview.ConcurrencyToken);
        Assert.Equal(64, preview.PreviewToken.Length);
        Assert.Equal(UtcNow, preview.GeneratedUtc);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.Equal(0m, line.ReceivedQuantity);
        Assert.Equal(10m, line.OutstandingQuantity);
        Assert.Equal(4m, line.QuantityToReceive);
        Assert.Equal(6m, line.OutstandingQuantityAfter);
        Assert.Equal(3m, line.OnHandQuantity);
        Assert.Equal(7m, line.OnHandQuantityAfter);

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(3m, await StoredQuantityAsync(verification, item.Id));
        Assert.Equal(0m, (await verification.Set<PurchaseOrderLine>().SingleAsync()).ReceivedQuantity);
        Assert.Equal(PurchaseOrderStatus.Ordered, (await verification.Set<PurchaseOrder>().SingleAsync()).Status);
        Assert.Empty(await verification.Set<PurchaseOrderReceipt>().ToListAsync());
        Assert.Empty(await verification.Set<InventoryAdjustment>().ToListAsync());
    }

    [Fact]
    public async Task GetPreviewAsync_returns_a_stable_token_until_the_order_or_stock_changes()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 3m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var first = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));
        var repeated = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));
        var otherQuantity = await PreviewAsync(dbContext, order.Id, (lines[0], 5m));
        item.ReduceStock(1m);
        await dbContext.SaveChangesAsync();
        var afterUsage = await PreviewAsync(dbContext, order.Id, (lines[0], 4m));

        Assert.Equal(first.PreviewToken, repeated.PreviewToken);
        Assert.NotEqual(first.PreviewToken, otherQuantity.PreviewToken);
        Assert.NotEqual(first.PreviewToken, afterUsage.PreviewToken);
    }

    [Fact]
    public async Task GetPreviewAsync_predicts_closing_the_order_when_everything_arrives()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));

        var partial = await PreviewAsync(dbContext, order.Id, (lines[0], 10m));
        var complete = await PreviewAsync(dbContext, order.Id, (lines[0], 10m), (lines[1], 2m));

        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), partial.ResultingOrderStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), complete.ResultingOrderStatus);
    }

    [Fact]
    public async Task GetPreviewAsync_reports_over_receipt_as_a_blocker()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 10.5m));

        var blocker = Assert.Single(preview.Blockers);

        Assert.False(preview.CanPost);
        Assert.Equal(PurchaseOrderReceiptBlockerCodes.ExceedsOutstanding, blocker.Code);
        Assert.Equal(lines[0].Id, blocker.PurchaseOrderLineId);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), preview.ResultingOrderStatus);
        Assert.Equal(-0.5m, Assert.Single(preview.Lines).OutstandingQuantityAfter);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task GetPreviewAsync_reports_an_order_that_cannot_be_received(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, status, (item, 10m));

        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 1m));

        Assert.False(preview.CanPost);
        Assert.Equal(PurchaseOrderReceiptBlockerCodes.OrderNotReceivable, Assert.Single(preview.Blockers).Code);
    }

    [Fact]
    public async Task GetPreviewAsync_shows_the_combined_on_hand_effect_of_a_repeated_part()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 1m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 3m), (item, 5m));

        var preview = await PreviewAsync(dbContext, order.Id, (lines[0], 3m), (lines[1], 2m));

        Assert.Equal(2, preview.Lines.Count);
        Assert.All(preview.Lines, line => Assert.Equal(6m, line.OnHandQuantityAfter));
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_invalid_requests_and_foreign_lines()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var (_, foreignLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));

        var invalid = await Assert.ThrowsAsync<PreviewPurchaseOrderReceiptValidationException>(
            () => CreateQuery(dbContext).GetPreviewAsync(
                order.Id,
                new PreviewPurchaseOrderReceiptRequest { ReceivedUtc = UtcNow.AddDays(1), Lines = [] },
                CancellationToken.None));
        var foreign = await Assert.ThrowsAsync<PreviewPurchaseOrderReceiptValidationException>(
            () => PreviewAsync(dbContext, order.Id, (foreignLines[0], 1m)));

        Assert.Equal(["lines", "receivedUtc"], invalid.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["lines[0].purchaseOrderLineId"], foreign.Errors.Keys);
    }

    [Fact]
    public async Task GetPreviewAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => CreateQuery(dbContext).GetPreviewAsync(
                Guid.NewGuid(),
                new PreviewPurchaseOrderReceiptRequest
                {
                    ReceivedUtc = ReceivedUtc,
                    Lines = [new PurchaseOrderReceiptLineEntry { PurchaseOrderLineId = Guid.NewGuid(), Quantity = 1m }],
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task GetRemainingPreviewAsync_lists_only_outstanding_quantities()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var other = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m), (other, 2m));
        await ReceiveAsync(dbContext, order.Id, (lines[0], 4m), (lines[1], 2m));

        var preview = await CreateQuery(dbContext).GetRemainingPreviewAsync(order.Id, CancellationToken.None);

        var line = Assert.Single(preview.Lines);

        Assert.True(preview.CanPost);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), preview.OrderStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), preview.ResultingOrderStatus);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(6m, line.QuantityToReceive);
        Assert.Equal(0m, line.OutstandingQuantityAfter);
    }

    [Fact]
    public async Task GetRemainingPreviewAsync_reports_a_fully_received_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));

        var preview = await CreateQuery(dbContext).GetRemainingPreviewAsync(order.Id, CancellationToken.None);

        Assert.False(preview.CanPost);
        Assert.Empty(preview.Lines);
        Assert.Equal(PurchaseOrderReceiptBlockerCodes.OrderNotReceivable, Assert.Single(preview.Blockers).Code);
    }

    private static GetPurchaseOrderReceiptPostingPreviewQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetPurchaseOrderReceiptPostingPreviewQuery(dbContext, Clock);
    }
}
