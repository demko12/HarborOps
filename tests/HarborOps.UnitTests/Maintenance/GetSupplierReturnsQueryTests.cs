using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using static HarborOps.UnitTests.Maintenance.SupplierReturnTestData;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetSupplierReturnsQueryTests
{
    [Fact]
    public async Task GetSupplierReturnsAsync_returns_summary_rows()
    {
        await using var dbContext = CreateDbContext();
        var supplier = await SeedSupplierAsync(dbContext, "Nordic Pumps");
        var item = await SeedItemAsync(dbContext, quantity: 20m);
        var (order, lines) = await SeedOrderAsync(dbContext, supplier, PurchaseOrderStatus.Received, (item, 5m), (item, 5m));
        var supplierReturn = await SeedReturnAsync(dbContext, order, (lines[0], 1.5m), (lines[1], 2m));

        var response = await new GetSupplierReturnsQuery(dbContext).GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest(),
            CancellationToken.None);

        var row = Assert.Single(response.Items);

        Assert.Equal(supplierReturn.Id, row.SupplierReturnId);
        Assert.Equal(order.Id, row.PurchaseOrderId);
        Assert.Equal(supplier.Id, row.SupplierId);
        Assert.Equal("Nordic Pumps", row.SupplierName);
        Assert.Equal("Draft", row.Status);
        Assert.Equal(2, row.LineCount);
        Assert.Equal(3.5m, row.TotalQuantity);
        Assert.Equal(supplierReturn.ConcurrencyToken, row.ConcurrencyToken);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(GetSupplierReturnsValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(GetSupplierReturnsValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task GetSupplierReturnsAsync_filters_by_order_supplier_and_status()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 50m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var (otherOrder, otherLines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var draft = await SeedReturnAsync(dbContext, order, (lines[0], 1m));
        var posted = await SeedPostedReturnAsync(dbContext, order, (lines[0], 1m));
        var other = await SeedReturnAsync(dbContext, otherOrder, (otherLines[0], 1m));
        var query = new GetSupplierReturnsQuery(dbContext);

        var byOrder = await query.GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest { PurchaseOrderId = order.Id },
            CancellationToken.None);
        var bySupplier = await query.GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest { SupplierId = otherOrder.SupplierId },
            CancellationToken.None);
        var postedOnly = await query.GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest { PurchaseOrderId = order.Id, Status = "posted" },
            CancellationToken.None);

        Assert.Equal(
            new[] { draft.Id, posted.Id }.Order(),
            byOrder.Items.Select(row => row.SupplierReturnId).Order());
        Assert.Equal([other.Id], bySupplier.Items.Select(row => row.SupplierReturnId));
        Assert.Equal([posted.Id], postedOnly.Items.Select(row => row.SupplierReturnId));
    }

    [Fact]
    public async Task GetSupplierReturnsAsync_pages_newest_first_with_a_stable_order()
    {
        await using var dbContext = CreateDbContext();
        var item = await SeedItemAsync(dbContext, quantity: 50m);
        var (order, lines) = await SeedOrderAsync(dbContext, PurchaseOrderStatus.Received, (item, 10m));
        var oldest = await SeedReturnAsync(dbContext, order, UtcNow.AddDays(-2), (lines[0], 1m));
        var middle = await SeedReturnAsync(dbContext, order, UtcNow.AddDays(-1), (lines[0], 1m));
        var newest = await SeedReturnAsync(dbContext, order, UtcNow, (lines[0], 1m));
        var query = new GetSupplierReturnsQuery(dbContext);

        var firstPage = await query.GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await query.GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal([newest.Id, middle.Id], firstPage.Items.Select(row => row.SupplierReturnId));
        Assert.Equal([oldest.Id], secondPage.Items.Select(row => row.SupplierReturnId));
    }

    [Fact]
    public async Task GetSupplierReturnsAsync_returns_an_empty_page()
    {
        await using var dbContext = CreateDbContext();

        var response = await new GetSupplierReturnsQuery(dbContext).GetSupplierReturnsAsync(
            new GetSupplierReturnsRequest(),
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetSupplierReturnsAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetSupplierReturnsValidationException>(() =>
            new GetSupplierReturnsQuery(dbContext).GetSupplierReturnsAsync(
                new GetSupplierReturnsRequest { Status = "Archived" },
                CancellationToken.None));

        Assert.Contains(GetSupplierReturnsValidator.StatusField, exception.Errors.Keys);
    }
}
