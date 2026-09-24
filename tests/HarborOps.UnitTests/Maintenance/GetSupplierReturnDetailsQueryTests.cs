using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetSupplierReturnDetailsQueryTests
{
    [Fact]
    public async Task GetDetailsAsync_shows_a_posted_return_with_its_immutable_history()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 6m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));
        var posted = await SeedPostedReturnAsync(dbContext, order, (lines[0], 2m));

        var details = await new GetSupplierReturnDetailsQuery(dbContext)
            .GetDetailsAsync(posted.Id, CancellationToken.None);

        var line = Assert.Single(details.Lines);
        var movement = Assert.Single(details.Movements);

        Assert.Equal("Posted", details.Status);
        Assert.Equal(ActorUserId, details.PostedByUserId);
        Assert.Equal(UtcNow, details.PostedUtc);
        Assert.Empty(details.Blockers);
        Assert.Equal(6m, line.ReceivedQuantity);
        Assert.Equal(1m, line.PreviouslyReturnedQuantity);
        Assert.Equal(3m, line.RemainingReturnableQuantity);
        Assert.Equal(7m, line.OnHandQuantity);
        Assert.Equal(9m, movement.QuantityBefore);
        Assert.Equal(7m, movement.QuantityAfter);
    }

    [Fact]
    public async Task GetDetailsAsync_shows_blockers_on_a_draft()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var workOrder = await SeedReservationAsync(dbContext, item, 9m);
        var draft = await SeedReturnAsync(dbContext, order, (lines[0], 2m));

        var details = await new GetSupplierReturnDetailsQuery(dbContext)
            .GetDetailsAsync(draft.Id, CancellationToken.None);

        var blocker = Assert.Single(details.Blockers);

        Assert.Equal("Draft", details.Status);
        Assert.Equal(SupplierReturnBlockerCodes.ReservedStock, blocker.Code);
        Assert.Equal([workOrder.Id], blocker.ReservingWorkOrderIds);
        Assert.Equal(9m, Assert.Single(details.Lines).ReservedQuantity);
        Assert.Empty(details.Movements);
    }

    [Fact]
    public async Task GetDetailsAsync_does_not_report_blockers_for_a_cancelled_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 1m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 5m));
        supplierReturn.Cancel("Duplicate", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        var details = await new GetSupplierReturnDetailsQuery(dbContext)
            .GetDetailsAsync(supplierReturn.Id, CancellationToken.None);

        Assert.Equal("Cancelled", details.Status);
        Assert.Empty(details.Blockers);
    }

    [Fact]
    public async Task GetDetailsAsync_rejects_an_unknown_return()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SupplierReturnNotFoundException>(() =>
            new GetSupplierReturnDetailsQuery(dbContext).GetDetailsAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
