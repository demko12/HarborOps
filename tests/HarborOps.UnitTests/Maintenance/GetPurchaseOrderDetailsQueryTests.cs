using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrderDetailsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public async Task GetDetailsAsync_returns_the_supplier_lines_and_total()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var filter = await SeedInventoryItemAsync(dbContext, supplier.Id, "Fuel filter", "PN-1");
        var impeller = await SeedInventoryItemAsync(dbContext, supplier.Id, "Impeller", "PN-2");
        var order = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            PurchaseOrderStatus.Ordered,
            Today.AddDays(6),
            (filter.Id, 4m, 25.50m),
            (impeller.Id, 2m, 100m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        Assert.Equal(order.Id, response.PurchaseOrderId);
        Assert.Equal(supplier.Id, response.SupplierId);
        Assert.Equal("Baltic Marine Parts", response.SupplierName);
        Assert.Equal("Ana Ilic", response.SupplierContactPerson);
        Assert.Equal("sales@baltic.test", response.SupplierPhoneEmail);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), response.Status);
        Assert.Equal(302m, response.TotalCost);
        Assert.False(response.IsOverdue);
        Assert.Equal(6, response.DaysUntilDelivery);

        Assert.Equal(2, response.Lines.Count);
        Assert.Equal(["Fuel filter", "Impeller"], response.Lines.Select(line => line.InventoryItemName));

        var filterLine = response.Lines.Single(line => line.InventoryItemId == filter.Id);
        Assert.Equal("PN-1", filterLine.PartNumber);
        Assert.Equal(4m, filterLine.QuantityOrdered);
        Assert.Equal(25.50m, filterLine.UnitCost);
        Assert.Equal(102m, filterLine.LineTotal);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, new[] { "Ordered", "Cancelled" })]
    [InlineData(PurchaseOrderStatus.Ordered, new[] { "Received", "Cancelled" })]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, new[] { "Received" })]
    [InlineData(PurchaseOrderStatus.Received, new string[0])]
    [InlineData(PurchaseOrderStatus.Cancelled, new string[0])]
    public async Task GetDetailsAsync_derives_the_allowed_transitions(
        PurchaseOrderStatus status,
        string[] expectedTransitions)
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(dbContext, supplier.Id, status, null, (item.Id, 1m, 10m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        Assert.Equal(expectedTransitions, response.AllowedTransitions);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, new[] { "MarkOrdered", "Cancel" })]
    [InlineData(PurchaseOrderStatus.Ordered, new[] { "ReceivePartial", "ReceiveRemaining", "Cancel" })]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, new[] { "ReceivePartial", "ReceiveRemaining", "CreateReturn" })]
    [InlineData(PurchaseOrderStatus.Received, new[] { "CreateReturn" })]
    [InlineData(PurchaseOrderStatus.Cancelled, new string[0])]
    public async Task GetDetailsAsync_derives_the_allowed_actions(
        PurchaseOrderStatus status,
        string[] expectedActions)
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(dbContext, supplier.Id, status, null, (item.Id, 2m, 10m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        Assert.Equal(expectedActions, response.AllowedActions);
        Assert.Equal(order.ConcurrencyToken, response.ConcurrencyToken);
    }

    [Fact]
    public async Task GetDetailsAsync_reports_ordered_received_returned_and_outstanding_quantities()
    {
        await using var dbContext = SupplierReturnTestData.CreateDbContext();
        var filter = await SupplierReturnTestData.SeedItemAsync(dbContext, quantity: 0m, name: "Fuel filter");
        var impeller = await SupplierReturnTestData.SeedItemAsync(dbContext, quantity: 0m, name: "Impeller");
        var (order, lines) = await SupplierReturnTestData.SeedOrderAsync(
            dbContext,
            PurchaseOrderStatus.Ordered,
            (filter, 10m),
            (impeller, 2m));
        await PurchaseOrderReceiptTestData.ReceiveAsync(dbContext, order.Id, (lines[0], 4m));
        await SupplierReturnTestData.SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        var filterLine = response.Lines.Single(line => line.LineId == lines[0].Id);
        var impellerLine = response.Lines.Single(line => line.LineId == lines[1].Id);

        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), response.Status);
        Assert.Equal(120m, response.TotalCost);
        Assert.Equal(1, response.ReceiptCount);
        Assert.Equal(12m, response.TotalQuantityOrdered);
        Assert.Equal(4m, response.TotalQuantityReceived);
        Assert.Equal(1m, response.TotalQuantityReturned);
        Assert.Equal(8m, response.TotalQuantityOutstanding);
        Assert.Equal(4m, filterLine.ReceivedQuantity);
        Assert.Equal(0m, filterLine.LegacyReceivedQuantity);
        Assert.Equal(1m, filterLine.ReturnedQuantity);
        Assert.Equal(6m, filterLine.OutstandingQuantity);
        Assert.Equal(3m, filterLine.ReturnableQuantity);
        Assert.Equal(100m, filterLine.LineTotal);
        Assert.Equal(0m, impellerLine.ReceivedQuantity);
        Assert.Equal(2m, impellerLine.OutstandingQuantity);
        Assert.Equal(0m, impellerLine.ReturnableQuantity);
    }

    [Fact]
    public async Task GetDetailsAsync_flags_an_overdue_partially_received_delivery()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            PurchaseOrderStatus.PartiallyReceived,
            Today.AddDays(-1),
            (item.Id, 4m, 10m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        Assert.True(response.IsOverdue);
    }

    [Fact]
    public async Task GetDetailsAsync_flags_an_overdue_delivery()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext);
        var item = await SeedInventoryItemAsync(dbContext, supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            PurchaseOrderStatus.Ordered,
            Today.AddDays(-3),
            (item.Id, 1m, 10m));

        var response = await CreateQuery(dbContext).GetDetailsAsync(order.Id, CancellationToken.None);

        Assert.True(response.IsOverdue);
        Assert.Equal(-3, response.DaysUntilDelivery);
    }

    [Fact]
    public async Task GetDetailsAsync_rejects_an_unknown_order()
    {
        await using var dbContext = CreateDbContext();
        var unknownOrderId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PurchaseOrderNotFoundException>(
            () => CreateQuery(dbContext).GetDetailsAsync(unknownOrderId, CancellationToken.None));

        Assert.Equal(unknownOrderId, exception.PurchaseOrderId);
    }

    private static GetPurchaseOrderDetailsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetPurchaseOrderDetailsQuery(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task<Supplier> SeedSupplierAsync(HarborOpsDbContext dbContext)
    {
        var supplier = Supplier.Register(
            "Baltic Marine Parts",
            "Ana Ilic",
            "sales@baltic.test",
            "Pier 4",
            "Net 30");

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    private static async Task<InventoryItem> SeedInventoryItemAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        string name,
        string partNumber)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            name,
            partNumber,
            0m,
            1m,
            10m);

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync();

        return item;
    }

    private static async Task<PurchaseOrder> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        PurchaseOrderStatus status,
        DateOnly? expectedDelivery,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var order = PurchaseOrder.Create(supplierId, Today, expectedDelivery);
        var orderLines = lines
            .Select(line => PurchaseOrderLine.Create(order.Id, line.ItemId, line.Quantity, line.UnitCost))
            .ToList();

        PurchaseOrderTestData.ApplyStatus(order, status, orderLines);

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync();

        return order;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
