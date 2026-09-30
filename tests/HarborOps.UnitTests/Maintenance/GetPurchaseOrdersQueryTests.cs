using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetPurchaseOrdersQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public async Task GetPurchaseOrdersAsync_projects_supplier_totals_and_line_count()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        var order = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            Today,
            Today.AddDays(5),
            PurchaseOrderStatus.Ordered,
            (Guid.NewGuid(), 4m, 25.50m),
            (Guid.NewGuid(), 2m, 100m));

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(order.Id, item.PurchaseOrderId);
        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Baltic Marine Parts", item.SupplierName);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), item.Status);
        Assert.Equal(2, item.LineCount);
        Assert.Equal(302m, item.TotalCost);
        Assert.False(item.IsOverdue);
        Assert.Equal(5, item.DaysUntilDelivery);
        Assert.Equal(1, response.PageNumber);
        Assert.Equal(GetPurchaseOrdersValidator.DefaultPageSize, response.PageSize);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_filters_by_supplier()
    {
        await using var dbContext = CreateDbContext();
        var mine = await SeedSupplierAsync(dbContext, "Mine");
        var other = await SeedSupplierAsync(dbContext, "Other");
        var expected = await SeedOrderAsync(dbContext, mine.Id, Today, null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, other.Id, Today, null, PurchaseOrderStatus.Draft);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { SupplierId = mine.Id },
            CancellationToken.None);

        Assert.Equal(expected.Id, Assert.Single(response.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        var ordered = await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Ordered);
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Cancelled);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Status = "ordered" },
            CancellationToken.None);

        Assert.Equal(ordered.Id, Assert.Single(response.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_filters_partially_received_orders_and_reports_their_quantities()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Ordered, (Guid.NewGuid(), 4m, 1m));
        var partial = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            Today,
            null,
            PurchaseOrderStatus.PartiallyReceived,
            (Guid.NewGuid(), 10m, 2m),
            (Guid.NewGuid(), 3m, 1m));

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Status = "partiallyreceived" },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(partial.Id, item.PurchaseOrderId);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), item.Status);
        Assert.Equal(23m, item.TotalCost);
        Assert.Equal(13m, item.QuantityOrdered);
        Assert.Equal(5m, item.QuantityReceived);
        Assert.Equal(8m, item.QuantityOutstanding);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_counts_partially_received_orders_as_overdue()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        var late = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            Today.AddDays(-20),
            Today.AddDays(-2),
            PurchaseOrderStatus.PartiallyReceived,
            (Guid.NewGuid(), 4m, 1m));
        var onTime = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            Today.AddDays(-20),
            Today.AddDays(2),
            PurchaseOrderStatus.PartiallyReceived,
            (Guid.NewGuid(), 4m, 1m));

        var overdueOnly = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Overdue = true },
            CancellationToken.None);
        var notOverdue = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Overdue = false },
            CancellationToken.None);

        Assert.Equal(late.Id, Assert.Single(overdueOnly.Items).PurchaseOrderId);
        Assert.True(overdueOnly.Items[0].IsOverdue);
        Assert.Equal(onTime.Id, Assert.Single(notOverdue.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_filters_by_order_date_range()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-10), null, PurchaseOrderStatus.Draft);
        var inRange = await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-3), null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(3), null, PurchaseOrderStatus.Draft);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest
            {
                OrderDateFrom = Today.AddDays(-5),
                OrderDateTo = Today,
            },
            CancellationToken.None);

        Assert.Equal(inRange.Id, Assert.Single(response.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_filters_by_expected_delivery_range()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        var inRange = await SeedOrderAsync(
            dbContext, supplier.Id, Today, Today.AddDays(4), PurchaseOrderStatus.Ordered);
        await SeedOrderAsync(dbContext, supplier.Id, Today, Today.AddDays(30), PurchaseOrderStatus.Ordered);
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Ordered);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest
            {
                ExpectedDeliveryFrom = Today,
                ExpectedDeliveryTo = Today.AddDays(7),
            },
            CancellationToken.None);

        Assert.Equal(inRange.Id, Assert.Single(response.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_classifies_and_filters_overdue_deliveries()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        var late = await SeedOrderAsync(
            dbContext, supplier.Id, Today.AddDays(-20), Today.AddDays(-2), PurchaseOrderStatus.Ordered);
        var dueToday = await SeedOrderAsync(
            dbContext, supplier.Id, Today.AddDays(-10), Today, PurchaseOrderStatus.Ordered);
        var lateButReceived = await SeedOrderAsync(
            dbContext,
            supplier.Id,
            Today.AddDays(-20),
            Today.AddDays(-2),
            PurchaseOrderStatus.Received,
            (Guid.NewGuid(), 1m, 1m));

        var all = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest(),
            CancellationToken.None);
        var overdueOnly = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Overdue = true },
            CancellationToken.None);
        var notOverdue = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { Overdue = false },
            CancellationToken.None);

        Assert.True(all.Items.Single(item => item.PurchaseOrderId == late.Id).IsOverdue);
        Assert.False(all.Items.Single(item => item.PurchaseOrderId == dueToday.Id).IsOverdue);
        Assert.False(all.Items.Single(item => item.PurchaseOrderId == lateButReceived.Id).IsOverdue);
        Assert.Equal(-2, all.Items.Single(item => item.PurchaseOrderId == late.Id).DaysUntilDelivery);

        Assert.Equal(late.Id, Assert.Single(overdueOnly.Items).PurchaseOrderId);
        Assert.Equal(2, notOverdue.TotalCount);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_orders_by_newest_order_date()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        var oldest = await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-9), null, PurchaseOrderStatus.Draft);
        var newest = await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        var middle = await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-4), null, PurchaseOrderStatus.Draft);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest(),
            CancellationToken.None);

        Assert.Equal(
            [newest.Id, middle.Id, oldest.Id],
            response.Items.Select(item => item.PurchaseOrderId));
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_breaks_same_day_ties_by_identifier()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);

        var response = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest(),
            CancellationToken.None);

        Assert.Equal(
            response.Items.Select(item => item.PurchaseOrderId).OrderBy(id => id),
            response.Items.Select(item => item.PurchaseOrderId));
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_pages_without_overlap()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Baltic Marine Parts");
        await SeedOrderAsync(dbContext, supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-1), null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(dbContext, supplier.Id, Today.AddDays(-2), null, PurchaseOrderStatus.Draft);

        var firstPage = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await CreateQuery(dbContext).GetPurchaseOrdersAsync(
            new GetPurchaseOrdersRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Empty(firstPage.Items.Select(item => item.PurchaseOrderId)
            .Intersect(secondPage.Items.Select(item => item.PurchaseOrderId)));
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPurchaseOrdersValidationException>(
            () => CreateQuery(dbContext).GetPurchaseOrdersAsync(
                new GetPurchaseOrdersRequest { Status = "Shipped" },
                CancellationToken.None));

        Assert.Contains(GetPurchaseOrdersValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_rejects_an_inverted_order_date_range()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPurchaseOrdersValidationException>(
            () => CreateQuery(dbContext).GetPurchaseOrdersAsync(
                new GetPurchaseOrdersRequest
                {
                    OrderDateFrom = Today,
                    OrderDateTo = Today.AddDays(-1),
                },
                CancellationToken.None));

        Assert.Contains(GetPurchaseOrdersValidator.OrderDateToField, exception.Errors);
    }

    [Fact]
    public async Task GetPurchaseOrdersAsync_rejects_an_invalid_page_size()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetPurchaseOrdersValidationException>(
            () => CreateQuery(dbContext).GetPurchaseOrdersAsync(
                new GetPurchaseOrdersRequest { PageSize = GetPurchaseOrdersValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetPurchaseOrdersValidator.PageSizeField, exception.Errors);
    }

    private static GetPurchaseOrdersQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetPurchaseOrdersQuery(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task<Supplier> SeedSupplierAsync(HarborOpsDbContext dbContext, string name)
    {
        var supplier = Supplier.Register(name, null, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync();

        return supplier;
    }

    private static async Task<PurchaseOrder> SeedOrderAsync(
        HarborOpsDbContext dbContext,
        Guid supplierId,
        DateOnly orderDate,
        DateOnly? expectedDelivery,
        PurchaseOrderStatus status,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var order = PurchaseOrder.Create(supplierId, orderDate, expectedDelivery);
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
