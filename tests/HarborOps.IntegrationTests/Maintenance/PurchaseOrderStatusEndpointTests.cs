using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class PurchaseOrderStatusEndpointTests : IntegrationTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly HarborOpsApiFactory _application;

    public PurchaseOrderStatusEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Order_moves_a_draft_order_to_ordered()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 5m);
        var order = await SeedOrderAsync(supplier.Id, PurchaseOrderStatus.Draft, (item.Id, 3m, 10m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/order", UriKind.Relative),
            content: null,
            CancellationToken.None);
        var result = await response.Content
            .ReadFromJsonAsync<ChangePurchaseOrderStatusResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(PurchaseOrderStatus.Draft), result.PreviousStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), result.Status);
        Assert.Empty(result.StockAdjustments);

        Assert.Equal(PurchaseOrderStatus.Ordered, await GetStatusAsync(order.Id));
        Assert.Equal(5m, await GetQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Receive_increases_stock_and_closes_the_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var filter = await SeedInventoryItemAsync(supplier.Id, 10m);
        var impeller = await SeedInventoryItemAsync(supplier.Id, 2.500m);
        var order = await SeedOrderAsync(
            supplier.Id,
            PurchaseOrderStatus.Ordered,
            (filter.Id, 4m, 25m),
            (impeller.Id, 1.250m, 100m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);
        var result = await response.Content
            .ReadFromJsonAsync<ChangePurchaseOrderStatusResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), result.Status);
        Assert.Empty(result.AllowedTransitions);
        Assert.Equal(2, result.StockAdjustments.Count);

        Assert.Equal(PurchaseOrderStatus.Received, await GetStatusAsync(order.Id));
        Assert.Equal(14m, await GetQuantityAsync(filter.Id));
        Assert.Equal(3.750m, await GetQuantityAsync(impeller.Id));
    }

    [Fact]
    public async Task Receive_twice_returns_409_and_leaves_stock_alone()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, PurchaseOrderStatus.Ordered, (item.Id, 4m, 25m));
        var uri = new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative);

        using var first = await client.PostAsync(uri, content: null, CancellationToken.None);
        using var second = await client.PostAsync(uri, content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(14m, await GetQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Receive_a_draft_order_returns_409_without_changing_stock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, PurchaseOrderStatus.Draft, (item.Id, 4m, 25m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PurchaseOrderStatus.Draft, await GetStatusAsync(order.Id));
        Assert.Equal(10m, await GetQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Receive_rolls_back_when_a_line_item_is_missing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var known = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(
            supplier.Id,
            PurchaseOrderStatus.Ordered,
            (known.Id, 4m, 25m),
            (Guid.NewGuid(), 1m, 5m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PurchaseOrderStatus.Ordered, await GetStatusAsync(order.Id));
        Assert.Equal(10m, await GetQuantityAsync(known.Id));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Ordered)]
    public async Task Cancel_closes_an_open_order(PurchaseOrderStatus initialStatus)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, initialStatus, (item.Id, 4m, 25m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/cancel", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PurchaseOrderStatus.Cancelled, await GetStatusAsync(order.Id));
        Assert.Equal(10m, await GetQuantityAsync(item.Id));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.PartiallyReceived)]
    [InlineData(PurchaseOrderStatus.Received)]
    public async Task Cancel_an_order_with_received_goods_returns_409(PurchaseOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, status, (item.Id, 4m, 25m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/cancel", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, await GetStatusAsync(order.Id));
    }

    [Fact]
    public async Task Receive_posts_only_the_outstanding_quantity_of_a_partially_received_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, PurchaseOrderStatus.PartiallyReceived, (item.Id, 4m, 25m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);
        var result = await response.Content
            .ReadFromJsonAsync<ChangePurchaseOrderStatusResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(PurchaseOrderStatus.PartiallyReceived), result.PreviousStatus);
        Assert.Equal(nameof(PurchaseOrderStatus.Received), result.Status);
        Assert.Equal(2m, Assert.Single(result.StockAdjustments).QuantityReceived);
        Assert.NotNull(result.Receipt);
        Assert.False(result.AlreadyPosted);
        Assert.Equal(12m, await GetQuantityAsync(item.Id));
    }

    [Fact]
    public async Task Order_returns_404_for_an_unknown_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{Guid.NewGuid()}/order", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Receive_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{Guid.NewGuid()}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Receive_with_only_the_read_permission_returns_403_without_changing_stock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, 10m);
        var order = await SeedOrderAsync(supplier.Id, PurchaseOrderStatus.Ordered, (item.Id, 4m, 25m));

        using var response = await client.PostAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}/receive", UriKind.Relative),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PurchaseOrderStatus.Ordered, await GetStatusAsync(order.Id));
        Assert.Equal(10m, await GetQuantityAsync(item.Id));
    }

    private async Task<PurchaseOrderStatus> GetStatusAsync(Guid purchaseOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var order = await dbContext.Set<PurchaseOrder>()
            .SingleAsync(candidate => candidate.Id == purchaseOrderId, CancellationToken.None);

        return order.Status;
    }

    private async Task<decimal> GetQuantityAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var item = await dbContext.Set<InventoryItem>()
            .SingleAsync(candidate => candidate.Id == inventoryItemId, CancellationToken.None);

        return item.Quantity;
    }

    private async Task<Supplier> SeedSupplierAsync()
    {
        var supplier = Supplier.Register("Baltic Marine Parts", null, null, null, null);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(Guid supplierId, decimal quantity)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            $"Part {Guid.NewGuid():N}",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            1m,
            10m);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task<PurchaseOrder> SeedOrderAsync(
        Guid supplierId,
        PurchaseOrderStatus status,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var order = PurchaseOrder.Create(supplierId, Today, Today.AddDays(7));
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
