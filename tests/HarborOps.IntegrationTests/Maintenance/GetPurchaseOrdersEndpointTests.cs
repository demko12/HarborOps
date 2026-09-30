using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetPurchaseOrdersEndpointTests : IntegrationTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly HarborOpsApiFactory _application;

    public GetPurchaseOrdersEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_a_correctly_shaped_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Baltic Marine Parts");
        var order = await SeedOrderAsync(
            supplier.Id,
            Today,
            Today.AddDays(5),
            PurchaseOrderStatus.Ordered,
            (Guid.NewGuid(), 4m, 25.50m),
            (Guid.NewGuid(), 2m, 100m));

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetPurchaseOrdersValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(order.Id, item.PurchaseOrderId);
        Assert.Equal("Baltic Marine Parts", item.SupplierName);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), item.Status);
        Assert.Equal(2, item.LineCount);
        Assert.Equal(302m, item.TotalCost);
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public async Task Get_filters_by_supplier_and_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var mine = await SeedSupplierAsync("Mine");
        var other = await SeedSupplierAsync("Other");
        var expected = await SeedOrderAsync(mine.Id, Today, null, PurchaseOrderStatus.Ordered);
        await SeedOrderAsync(mine.Id, Today, null, PurchaseOrderStatus.Draft);
        await SeedOrderAsync(other.Id, Today, null, PurchaseOrderStatus.Ordered);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders?supplierId={mine.Id}&status=Ordered", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(expected.Id, Assert.Single(page.Items).PurchaseOrderId);
    }

    [Fact]
    public async Task Get_filters_overdue_deliveries()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Baltic Marine Parts");
        var late = await SeedOrderAsync(
            supplier.Id, Today.AddDays(-30), Today.AddDays(-2), PurchaseOrderStatus.Ordered);
        await SeedOrderAsync(supplier.Id, Today, Today.AddDays(10), PurchaseOrderStatus.Ordered);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?overdue=true", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal(late.Id, item.PurchaseOrderId);
        Assert.True(item.IsOverdue);
    }

    [Fact]
    public async Task Get_orders_by_newest_order_date()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Baltic Marine Parts");
        var oldest = await SeedOrderAsync(supplier.Id, Today.AddDays(-9), null, PurchaseOrderStatus.Draft);
        var newest = await SeedOrderAsync(supplier.Id, Today, null, PurchaseOrderStatus.Draft);
        var middle = await SeedOrderAsync(supplier.Id, Today.AddDays(-4), null, PurchaseOrderStatus.Draft);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrdersResponse>(CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(
            [newest.Id, middle.Id, oldest.Id],
            page.Items.Select(item => item.PurchaseOrderId));
    }

    [Fact]
    public async Task Get_returns_400_for_an_unknown_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders?status=Shipped", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_a_maintenance_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Supplier> SeedSupplierAsync(string name)
    {
        var supplier = Supplier.Register(name, null, null, null, null);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }

    private async Task<PurchaseOrder> SeedOrderAsync(
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

        PurchaseOrderSeedData.ApplyStatus(order, status, orderLines);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<PurchaseOrder>().AddAsync(order);
        await dbContext.Set<PurchaseOrderLine>().AddRangeAsync(orderLines);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return order;
    }
}
