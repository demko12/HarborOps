using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.PurchaseOrderClosureTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderClosureQueryTests
{
    [Fact]
    public async Task GetClosureAsync_returns_the_attributable_closure_and_its_evidence()
    {
        var databaseName = NewDatabaseName();
        await using var dbContext = CreateDbContext(databaseName);
        var filter = await SeedItemAsync(dbContext, quantity: 0m, name: "Fuel filter");
        var impeller = await SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SeedReceivedAsync(dbContext, (impeller, 3m, 1m), (filter, 10m, 4m));
        var closed = await CloseRemainingAsync(dbContext, order.Id);

        await using var verification = CreateDbContext(databaseName);
        var closure = await GetClosureAsync(verification, order.Id);

        Assert.Equal(closed.Closure.ClosureId, closure.ClosureId);
        Assert.Equal(closed.Closure.ClientRequestId, closure.ClientRequestId);
        Assert.Equal(Reason, closure.Reason);
        Assert.Equal(ActorUserId, closure.ClosedByUserId);
        Assert.Equal(UtcNow, closure.ClosedUtc);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), closure.OrderStatusBefore);
        Assert.Equal(nameof(PurchaseOrderStatus.ClosedShort), closure.OrderStatusAfter);
        Assert.Equal(5m, closure.TotalQuantityReceived);
        Assert.Equal(8m, closure.TotalQuantityClosed);
        Assert.Equal(["Fuel filter", "Impeller"], closure.Lines.Select(line => line.Name));
        Assert.Equal([lines[1].Id, lines[0].Id], closure.Lines.Select(line => line.PurchaseOrderLineId));
        Assert.Equal([6m, 2m], closure.Lines.Select(line => line.ClosedQuantity));
    }

    [Fact]
    public async Task GetClosureAsync_keeps_the_evidence_when_goods_are_returned_later()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, lines) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));
        await CloseRemainingAsync(dbContext, order.Id);
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 2m));

        var line = Assert.Single((await GetClosureAsync(dbContext, order.Id)).Lines);

        Assert.Equal(4m, line.ReceivedQuantity);
        Assert.Equal(0m, line.ReturnedQuantity);
        Assert.Equal(6m, line.ClosedQuantity);
    }

    [Fact]
    public async Task GetClosureAsync_rejects_an_order_that_was_not_closed()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 0m);
        var (order, _) = await SeedReceivedAsync(dbContext, (item, 10m, 4m));

        var exception = await Assert.ThrowsAsync<PurchaseOrderClosureNotFoundException>(
            () => GetClosureAsync(dbContext, order.Id));

        Assert.Equal(order.Id, exception.PurchaseOrderId);
    }

    [Fact]
    public async Task GetClosureAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(() => GetClosureAsync(dbContext, Guid.NewGuid()));
    }
}
