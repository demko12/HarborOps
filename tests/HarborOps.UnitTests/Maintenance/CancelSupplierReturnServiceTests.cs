using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CancelSupplierReturnServiceTests
{
    [Fact]
    public async Task CancelAsync_records_the_reason_and_leaves_stock_untouched()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));

        var details = await CancelAsync(dbContext, supplierReturn.Id, supplierReturn.ConcurrencyToken, " Supplier refused ");

        Assert.Equal("Cancelled", details.Status);
        Assert.Equal("Supplier refused", details.CancellationReason);
        Assert.Equal(ActorUserId, details.CancelledByUserId);
        Assert.Equal(UtcNow, details.CancelledUtc);
        Assert.Empty(details.Blockers);
        Assert.Empty(details.Movements);
        Assert.Equal(10m, await StoredQuantityAsync(dbContext, item.Id));
        Assert.Equal(0, await MovementCountAsync(dbContext));
    }

    [Fact]
    public async Task CancelAsync_rejects_a_stale_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 2m));

        await Assert.ThrowsAsync<SupplierReturnConcurrencyException>(() =>
            CancelAsync(dbContext, supplierReturn.Id, Guid.NewGuid(), "Duplicate"));

        Assert.Equal(SupplierReturnStatus.Draft, await StoredStatusAsync(dbContext, supplierReturn.Id));
    }

    [Fact]
    public async Task CancelAsync_cannot_reverse_a_posted_return()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var posted = await SeedPostedReturnAsync(dbContext, order, (lines[0], 2m));

        await Assert.ThrowsAsync<SupplierReturnNotDraftException>(() =>
            CancelAsync(dbContext, posted.Id, posted.ConcurrencyToken, "Changed my mind"));

        Assert.Equal(SupplierReturnStatus.Posted, await StoredStatusAsync(dbContext, posted.Id));
        Assert.Equal(8m, await StoredQuantityAsync(dbContext, item.Id));
    }

    [Fact]
    public async Task CancelAsync_rejects_an_unknown_return()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<SupplierReturnNotFoundException>(() =>
            CancelAsync(dbContext, Guid.NewGuid(), Guid.NewGuid(), "Duplicate"));
    }

    [Fact]
    public async Task CancelAsync_validates_the_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<CancelSupplierReturnValidationException>(() =>
            CancelAsync(dbContext, Guid.NewGuid(), Guid.NewGuid(), " "));

        Assert.Contains(CancelSupplierReturnValidator.ReasonField, exception.Errors.Keys);
    }

    private static Task<SupplierReturnDetails> CancelAsync(
        HarborOpsDbContext dbContext,
        Guid supplierReturnId,
        Guid expectedConcurrencyToken,
        string reason)
    {
        return new CancelSupplierReturnService(dbContext, Clock).CancelAsync(
            supplierReturnId,
            new CancelSupplierReturnRequest
            {
                ExpectedConcurrencyToken = expectedConcurrencyToken,
                Reason = reason,
            },
            ActorUserId,
            CancellationToken.None);
    }
}
