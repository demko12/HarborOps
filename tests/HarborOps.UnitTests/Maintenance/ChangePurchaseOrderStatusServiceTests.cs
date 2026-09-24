using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

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

        var response = await new ChangePurchaseOrderStatusService(dbContext)
            .MarkOrderedAsync(order.Id, CancellationToken.None);

        Assert.Equal(nameof(PurchaseOrderStatus.Draft), response.PreviousStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), response.Status);
        Assert.Equal(
            [nameof(PurchaseOrderStatus.Received), nameof(PurchaseOrderStatus.Cancelled)],
            response.AllowedTransitions);
        Assert.Empty(response.StockAdjustments);

        await using var verification = CreateDbContext(databaseName);
        var stored = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Ordered, stored.Status);
        Assert.Equal(5m, storedItem.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_increases_every_inventory_quantity()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var filter = await SeedInventoryItemAsync(dbContext, 10m);
        var impeller = await SeedInventoryItemAsync(dbContext, 2.500m);
        var order = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Ordered,
            (filter.Id, 4m, 25m),
            (impeller.Id, 1.250m, 100m));

        var response = await new ChangePurchaseOrderStatusService(dbContext)
            .ReceiveAsync(order.Id, CancellationToken.None);

        Assert.Equal(nameof(PurchaseOrderStatus.Received), response.Status);
        Assert.Empty(response.AllowedTransitions);
        Assert.Equal(2, response.StockAdjustments.Count);

        await using var verification = CreateDbContext(databaseName);
        var storedOrder = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedFilter = await verification.Set<InventoryItem>()
            .SingleAsync(candidate => candidate.Id == filter.Id, CancellationToken.None);
        var storedImpeller = await verification.Set<InventoryItem>()
            .SingleAsync(candidate => candidate.Id == impeller.Id, CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Received, storedOrder.Status);
        Assert.Equal(14m, storedFilter.Quantity);
        Assert.Equal(3.750m, storedImpeller.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_keeps_the_lines_as_an_immutable_commercial_record()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 1m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item.Id, 6m, 12.75m));

        await new ChangePurchaseOrderStatusService(dbContext).ReceiveAsync(order.Id, CancellationToken.None);

        await using var verification = CreateDbContext(databaseName);
        var line = await verification.Set<PurchaseOrderLine>().SingleAsync(CancellationToken.None);

        Assert.Equal(item.Id, line.PartId);
        Assert.Equal(6m, line.QuantityOrdered);
        Assert.Equal(12.75m, line.UnitCost);
    }

    [Fact]
    public async Task ReceiveAsync_rejects_a_second_receipt_without_touching_stock()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item.Id, 4m, 25m));
        var service = new ChangePurchaseOrderStatusService(dbContext);

        await service.ReceiveAsync(order.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => service.ReceiveAsync(order.Id, CancellationToken.None));

        Assert.Contains("Received", exception.Message, StringComparison.Ordinal);

        await using var verification = CreateDbContext(databaseName);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(14m, storedItem.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_rejects_a_draft_order_without_touching_stock()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Draft, (item.Id, 4m, 25m));

        await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .ReceiveAsync(order.Id, CancellationToken.None));

        await using var verification = CreateDbContext(databaseName);
        var storedOrder = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Draft, storedOrder.Status);
        Assert.Equal(10m, storedItem.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_leaves_stock_and_status_untouched_when_an_item_is_missing()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var known = await SeedInventoryItemAsync(dbContext, 10m);
        var missingItemId = Guid.NewGuid();
        var order = await SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Ordered,
            (known.Id, 4m, 25m),
            (missingItemId, 1m, 5m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderItemsNotFoundException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .ReceiveAsync(order.Id, CancellationToken.None));

        Assert.Equal([missingItemId], exception.InventoryItemIds);

        await using var verification = CreateDbContext(databaseName);
        var storedOrder = await verification.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Ordered, storedOrder.Status);
        Assert.Equal(10m, storedItem.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_rejects_an_order_without_lines()
    {
        await using var dbContext = CreateDbContext(NewDatabaseName());
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered);

        var exception = await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .ReceiveAsync(order.Id, CancellationToken.None));

        Assert.Contains("no lines", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public async Task CancelAsync_rejects_a_received_order()
    {
        await using var dbContext = CreateDbContext(NewDatabaseName());
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item.Id, 4m, 25m));

        await Assert.ThrowsAsync<PurchaseOrderConflictException>(
            () => new ChangePurchaseOrderStatusService(dbContext)
                .CancelAsync(order.Id, CancellationToken.None));

        var stored = await dbContext.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Received, stored.Status);
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
    public async Task ReceiveAsync_replaces_the_stock_token_of_every_received_item()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var item = await SeedInventoryItemAsync(dbContext, 10m);
        var stockToken = item.StockConcurrencyToken;
        var reservationToken = item.ReservationConcurrencyToken;
        var order = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Ordered, (item.Id, 5m, 25m));

        await new ChangePurchaseOrderStatusService(dbContext).ReceiveAsync(order.Id, CancellationToken.None);

        await using var verification = CreateDbContext(databaseName);
        var storedItem = await verification.Set<InventoryItem>().SingleAsync(CancellationToken.None);

        Assert.Equal(15m, storedItem.Quantity);
        Assert.NotEqual(stockToken, storedItem.StockConcurrencyToken);
        Assert.Equal(reservationToken, storedItem.ReservationConcurrencyToken);
    }

    private static async Task<PurchaseOrder> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        PurchaseOrderStatus status,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);
        await dbContext.Set<Supplier>().AddAsync(supplier);

        var order = PurchaseOrder.Create(supplier.Id, OrderDate, OrderDate.AddDays(7));

        if (status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.Received)
        {
            order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
        }

        if (status == PurchaseOrderStatus.Received)
        {
            order.ChangeStatusTo(PurchaseOrderStatus.Received);
        }

        if (status == PurchaseOrderStatus.Cancelled)
        {
            order.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
        }

        await dbContext.Set<PurchaseOrder>().AddAsync(order);

        foreach (var line in lines)
        {
            await dbContext.Set<PurchaseOrderLine>().AddAsync(
                PurchaseOrderLine.Create(order.Id, line.ItemId, line.Quantity, line.UnitCost));
        }

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
