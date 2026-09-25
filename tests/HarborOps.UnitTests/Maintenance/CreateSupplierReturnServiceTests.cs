using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class CreateSupplierReturnServiceTests
{
    [Fact]
    public async Task CreateAsync_creates_a_draft_for_the_order_supplier_with_line_allowances()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Nordic Pumps");
        var item = await SeedItemAsync(dbContext, quantity: 12m, name: "Impeller");
        var (order, lines) = await SeedOrderAsync(dbContext, supplier, PurchaseOrderStatus.Received, (item, 10m));
        var stockToken = item.StockConcurrencyToken;

        var details = await CreateService(dbContext).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = order.Id,
                Notes = " RMA pending ",
                Lines = [Entry(lines[0], 4.125m, "  Wrong impeller size  ")],
            },
            ActorUserId,
            CancellationToken.None);

        var line = Assert.Single(details.Lines);

        Assert.Equal("Draft", details.Status);
        Assert.Equal(order.Id, details.PurchaseOrderId);
        Assert.Equal("Received", details.PurchaseOrderStatus);
        Assert.Equal(supplier.Id, details.SupplierId);
        Assert.Equal("Nordic Pumps", details.SupplierName);
        Assert.Equal("RMA pending", details.Notes);
        Assert.Equal(ActorUserId, details.CreatedByUserId);
        Assert.Equal(UtcNow, details.CreatedUtc);
        Assert.Empty(details.Blockers);
        Assert.Empty(details.Movements);
        Assert.Equal(lines[0].Id, line.PurchaseOrderLineId);
        Assert.Equal(item.Id, line.InventoryItemId);
        Assert.Equal("Impeller", line.Name);
        Assert.Equal("pcs", line.UnitAbbreviation);
        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(0m, line.PreviouslyReturnedQuantity);
        Assert.Equal(10m, line.RemainingReturnableQuantity);
        Assert.Equal(12m, line.OnHandQuantity);
        Assert.Equal(4.125m, line.Quantity);
        Assert.Equal("Wrong impeller size", line.Reason);

        // A draft never touches stock.
        var storedItem = await dbContext.Set<InventoryItem>().AsNoTracking().SingleAsync(candidate => candidate.Id == item.Id);
        Assert.Equal(12m, storedItem.Quantity);
        Assert.Equal(stockToken, storedItem.StockConcurrencyToken);
    }

    [Fact]
    public async Task CreateAsync_accepts_multiple_order_lines_for_the_same_part()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m), (item, 7m));

        var details = await CreateService(dbContext).CreateAsync(
            new CreateSupplierReturnRequest
            {
                PurchaseOrderId = order.Id,
                Lines = [Entry(lines[0], 5m), Entry(lines[1], 2m)],
            },
            ActorUserId,
            CancellationToken.None);

        Assert.Equal(2, details.Lines.Count);
        Assert.All(details.Lines, line => Assert.Equal(item.Id, line.InventoryItemId));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task CreateAsync_rejects_an_order_that_was_not_received(PurchaseOrderStatus status)
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, lines) = await SeedOrderAsync(dbContext, status, (item, 5m));

        await Assert.ThrowsAsync<SupplierReturnPurchaseOrderNotReceivedException>(() =>
            CreateAsync(dbContext, order.Id, Entry(lines[0], 1m)));

        Assert.Empty(await dbContext.Set<SupplierReturn>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(() =>
            CreateAsync(dbContext, Guid.NewGuid(), new SupplierReturnLineEntry
            {
                PurchaseOrderLineId = Guid.NewGuid(),
                Quantity = 1m,
                Reason = Reason,
            }));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_line_from_another_purchase_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 10m);
        var (order, _) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        var (_, foreignLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(() =>
            CreateAsync(dbContext, order.Id, Entry(foreignLines[0], 1m)));

        Assert.Contains("lines[0].purchaseOrderLineId", exception.Errors.Keys);
        Assert.Empty(await dbContext.Set<SupplierReturn>().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_more_than_the_received_quantity()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 50m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(() =>
            CreateAsync(dbContext, order.Id, Entry(lines[0], 5.001m)));

        Assert.Contains("lines[0].quantity", exception.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_counts_posted_returns_against_the_allowance()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 50m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        await SeedPostedReturnAsync(dbContext, order, (lines[0], 3m));

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(() =>
            CreateAsync(dbContext, order.Id, Entry(lines[0], 2.5m)));

        var accepted = await CreateAsync(dbContext, order.Id, Entry(lines[0], 2m));

        Assert.Contains("lines[0].quantity", exception.Errors.Keys);
        Assert.Equal(3m, Assert.Single(accepted.Lines).PreviouslyReturnedQuantity);
        Assert.Equal(2m, Assert.Single(accepted.Lines).RemainingReturnableQuantity);
    }

    [Fact]
    public async Task CreateAsync_ignores_draft_and_cancelled_returns_in_the_allowance()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 50m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 5m));
        await SeedReturnAsync(dbContext, order, (lines[0], 5m));
        var cancelled = await SeedReturnAsync(dbContext, order, (lines[0], 5m));
        cancelled.Cancel("Duplicate", ActorUserId, UtcNow);
        await dbContext.SaveChangesAsync();

        var details = await CreateAsync(dbContext, order.Id, Entry(lines[0], 5m));

        Assert.Equal(0m, Assert.Single(details.Lines).PreviouslyReturnedQuantity);
    }

    [Fact]
    public async Task CreateAsync_validates_the_request_before_touching_the_database()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<CreateSupplierReturnValidationException>(() =>
            CreateService(dbContext).CreateAsync(new CreateSupplierReturnRequest(), ActorUserId, CancellationToken.None));

        Assert.Contains(CreateSupplierReturnValidator.PurchaseOrderIdField, exception.Errors.Keys);
        Assert.Contains(SupplierReturnLinesValidator.LinesField, exception.Errors.Keys);
    }

    private static Task<SupplierReturnDetails> CreateAsync(
        HarborOpsDbContext dbContext,
        Guid purchaseOrderId,
        params SupplierReturnLineEntry[] lines)
    {
        return CreateService(dbContext).CreateAsync(
            new CreateSupplierReturnRequest { PurchaseOrderId = purchaseOrderId, Lines = lines },
            ActorUserId,
            CancellationToken.None);
    }

    private static CreateSupplierReturnService CreateService(HarborOpsDbContext dbContext)
    {
        return new CreateSupplierReturnService(dbContext, Clock);
    }
}
