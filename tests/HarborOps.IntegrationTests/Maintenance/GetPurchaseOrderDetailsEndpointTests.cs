using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetPurchaseOrderDetailsEndpointTests : IntegrationTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly HarborOpsApiFactory _application;

    public GetPurchaseOrderDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_complete_details_response()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync();
        var filter = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(
            supplier.Id,
            PurchaseOrderStatus.Ordered,
            Today.AddDays(6),
            (filter.Id, 4m, 25.50m));

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}", UriKind.Relative),
            CancellationToken.None);
        var details = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrderDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(order.Id, details.PurchaseOrderId);
        Assert.Equal("Baltic Marine Parts", details.SupplierName);
        Assert.Equal("Ana Ilic", details.SupplierContactPerson);
        Assert.Equal(nameof(PurchaseOrderStatus.Ordered), details.Status);
        Assert.Equal(102m, details.TotalCost);
        Assert.False(details.IsOverdue);
        Assert.Equal(
            [nameof(PurchaseOrderStatus.Received), nameof(PurchaseOrderStatus.Cancelled)],
            details.AllowedTransitions);

        var line = Assert.Single(details.Lines);
        Assert.Equal(filter.Id, line.InventoryItemId);
        Assert.Equal("Fuel filter", line.InventoryItemName);
        Assert.Equal("PN-1", line.PartNumber);
        Assert.Equal(4m, line.QuantityOrdered);
        Assert.Equal(25.50m, line.UnitCost);
        Assert.Equal(102m, line.LineTotal);
    }

    [Fact]
    public async Task Get_reports_no_transitions_for_a_received_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync();
        var filter = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1");
        var order = await SeedOrderAsync(
            supplier.Id,
            PurchaseOrderStatus.Received,
            Today.AddDays(-1),
            (filter.Id, 1m, 10m));

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{order.Id}", UriKind.Relative),
            CancellationToken.None);
        var details = await response.Content
            .ReadFromJsonAsync<GetPurchaseOrderDetailsResponse>(CancellationToken.None);

        Assert.NotNull(details);
        Assert.Empty(details.AllowedTransitions);
        Assert.False(details.IsOverdue);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/purchase-orders/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Supplier> SeedSupplierAsync()
    {
        var supplier = Supplier.Register(
            "Baltic Marine Parts",
            "Ana Ilic",
            "sales@baltic.test",
            "Pier 4",
            "Net 30");

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(
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

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task<PurchaseOrder> SeedOrderAsync(
        Guid supplierId,
        PurchaseOrderStatus status,
        DateOnly? expectedDelivery,
        params (Guid ItemId, decimal Quantity, decimal UnitCost)[] lines)
    {
        var order = PurchaseOrder.Create(supplierId, Today, expectedDelivery);
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
