using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

// Receiving is covered by PostPurchaseOrderReceiptServiceTests; this service only orders and cancels.
public sealed class ChangePurchaseOrderStatusServiceTests
{
    private static readonly DateOnly OrderDate = new(2026, 8, 20);

    [Fact]
    public async Task MarkOrderedAsync_moves_a_draft_order_to_ordered()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 5m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Draft, (item.Id, 3m, 10m));
        var token = order.ConcurrencyToken;

        var response = await new ChangePurchaseOrderStatusService(dbContext)
            .MarkOrderedAsync(order.Id, CancellationToken.None);

        Assert.Equal(nameof(PurchaseOrderStatus.Draft), response.PreviousStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), response.Status);
        Assert.Equal(
            [nameof(PurchaseOrderStatus.Received), nameof(PurchaseOrderStatus.Cancelled)],
            response.AllowedTransitions);
        Assert.Empty(response.StockAdjustments);
        Assert.Null(response.Receipt);
        Assert.False(response.AlreadyPosted);

        await using var verification = CreateDbContext(databaseName);
        var stored = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Ordered, stored.Status);
        Assert.NotEqual(token, stored.ConcurrencyToken);
        Assert.Equal(5m, storedItem.Quantity);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    public async Task CancelAsync_cancels_open_orders(PurchaseOrderStatus initialStatus)
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, initialStatus, (item.Id, 4m, 25m));

        var response = await new ChangePurchaseOrderStatusService(dbContext)
            .CancelAsync(order.Id, CancellationToken.None);

        Assert.Equal(nameof(PurchaseOrderStatus.Cancelled), response.Status);
        Assert.Empty(response.AllowedTransitions);
        Assert.Empty(response.StockAdjustments);

        await using var verification = CreateDbContext(databaseName);
        var storedOrder = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Cancelled, storedOrder.Status);
        Assert.Equal(10m, storedItem.Quantity);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.PartiallyReceived)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.ClosedShort)]
    public async Task CancelAsync_rejects_an_order_with_received_goods(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext(NewDatabaseName());
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, status, (item.Id, 4m, 25m));

        await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .CancelAsync(order.Id, CancellationToken.None));

        var stored = await dbContext.Set<PurchaseOrder>().AsNoTracking().SingleAsync(CancellationToken.None);

        Assert.Equal(status, stored.Status);
    }

    [Fact]
    public async Task MarkOrderedAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext(NewDatabaseName());
        var unknownOrderId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .MarkOrderedAsync(unknownOrderId, CancellationToken.None));

        Assert.Equal(unknownOrderId, exception.PurchaseOrderId);
    }

    [Fact]
    public async Task MarkOrderedAsync_rejects_an_order_that_is_already_ordered()
    {
        await using var dbContext = CreateDbContext(NewDatabaseName());
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item.Id, 4m, 25m));

        await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .MarkOrderedAsync(order.Id, CancellationToken.None));
    }

    private static async Task<PurchaseOrder> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrderStatus status,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        await dbContext.Set<Supplier>().AddAsync(supplier);

        var order = PurchaseOrder.Create(supplier.Id, OrderDate, OrderDate.AddDays(7));
        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.ItemId, line.Quantity, line.UnitCost))
            .ToList();

        PurchaseOrderTestData.ApplyStatus(order, status, orderLines);

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return order;
    }

    private static async Task<InventoryItem> SeedInventoryItemAsync(
        HarborOpsDbContext dbContext,
        decimal quantity)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"Part {Guid.NewGuid():N}",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            1m,
            10m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static string NewDatabaseName()
    {
        return $"HarborOpsUnitTests-{Guid.NewGuid()}";
    }

    private static HarborOpsDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new HarborOpsDbContext(options);
    }
}
