using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderReceiptTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderReceiptsQueryTests
{
    [Fact]
    public async Task GetReceiptsAsync_lists_deliveries_newest_first_with_their_lines()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var first = await PostReceiptAsync(dbContext, order, lines[0], 4m, UtcNow.AddDays(-2), "DN-1");
        var second = await PostReceiptAsync(dbContext, order, lines[0], 6m, UtcNow.AddDays(-1), "DN-2");

        var response = await CreateQuery(dbContext).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest(),
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal([second.ReceiptId, first.ReceiptId], response.Items.Select(receipt => receipt.ReceiptId));
        Assert.Equal("DN-2", response.Items[0].DeliveryReference);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.Items[0].OrderStatusBefore);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), response.Items[0].OrderStatusAfter);
        Assert.Equal(10m, Assert.Single(response.Items[0].Lines).LineReceivedQuantityAfter);
        Assert.Equal(6m, Assert.Single(response.Items[1].Lines).OutstandingQuantityAfter);
    }

    [Fact]
    public async Task GetReceiptsAsync_pages_the_history()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var oldest = await PostReceiptAsync(dbContext, order, lines[0], 1m, UtcNow.AddDays(-3));
        await PostReceiptAsync(dbContext, order, lines[0], 1m, UtcNow.AddDays(-2));
        await PostReceiptAsync(dbContext, order, lines[0], 1m, UtcNow.AddDays(-1));

        var response = await CreateQuery(dbContext).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(oldest.ReceiptId, Assert.Single(response.Items).ReceiptId);
    }

    [Fact]
    public async Task GetReceiptsAsync_only_lists_receipts_of_the_requested_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var (other, otherLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await PostReceiptAsync(dbContext, other, otherLines[0], 1m, UtcNow.AddDays(-1));

        var response = await CreateQuery(dbContext).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest(),
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetReceiptsAsync_has_no_history_for_a_legacy_received_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        await PurchaseOrderTestData.SeedLegacyReceivedAsync(dbContext, order, lines);

        var response = await CreateQuery(dbContext).GetReceiptsAsync(
            order.Id,
            new GetPurchaseOrderReceiptsRequest(),
            CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Theory]
    [InlineData(0, null, "pageNumber")]
    [InlineData(null, 0, "pageSize")]
    [InlineData(null, 101, "pageSize")]
    public async Task GetReceiptsAsync_validates_paging(int? pageNumber, int? pageSize, string field)
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPurchaseOrderReceiptsValidationException>(
            () => CreateQuery(dbContext).GetReceiptsAsync(
                Guid.NewGuid(),
                new GetPurchaseOrderReceiptsRequest { PageNumber = pageNumber, PageSize = pageSize },
                CancellationToken.None));

        Assert.Equal([field], exception.Errors.Keys);
    }

    [Fact]
    public async Task GetReceiptsAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => CreateQuery(dbContext).GetReceiptsAsync(
                Guid.NewGuid(),
                new GetPurchaseOrderReceiptsRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task GetReceiptAsync_returns_one_receipt_of_the_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var posted = await PostReceiptAsync(dbContext, order, lines[0], 4m, UtcNow.AddDays(-1), "DN-7");

        var receipt = await CreateQuery(dbContext).GetReceiptAsync(order.Id, posted.ReceiptId, CancellationToken.None);

        Assert.Equal(posted, receipt with { Lines = posted.Lines, Movements = posted.Movements });
        Assert.Equal(posted.Movements, receipt.Movements);
        Assert.Equal(posted.Lines, receipt.Lines);
    }

    [Fact]
    public async Task GetReceiptAsync_rejects_a_receipt_of_another_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var (other, otherLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item, 10m));
        var posted = await PostReceiptAsync(dbContext, other, otherLines[0], 1m, UtcNow.AddDays(-1));

        var exception = await Assert.ThrowsAsync<PurchaseOrderReceiptNotFoundException>(
            () => CreateQuery(dbContext).GetReceiptAsync(order.Id, posted.ReceiptId, CancellationToken.None));

        Assert.Equal(posted.ReceiptId, exception.ReceiptId);
    }

    private static GetPurchaseOrderReceiptsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetPurchaseOrderReceiptsQuery(dbContext);
    }

    private static async Task<PurchaseOrderReceiptDetails> PostReceiptAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrder order,
        PurchaseOrderLine line,
        decimal quantity,
        DateTime receivedUtc,
        string? deliveryReference = null)
    {
        var preview = await PreviewAsync(dbContext, order.Id, (line, quantity));
        var response = await PostAsync(
            dbContext,
            order.Id,
            Request(preview, (line, quantity)) with { ReceivedUtc = receivedUtc, DeliveryReference = deliveryReference });

        return response.Receipt;
    }
}
