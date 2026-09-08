using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class InventoryReplenishmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public InventoryReplenishmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_only_the_items_below_the_reorder_level()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        await SeedItemAsync(supplier.Id, "Low part", "PN-1", quantity: 1m, minimum: 5m);
        await SeedItemAsync(supplier.Id, "Stocked part", "PN-2", quantity: 40m, minimum: 5m);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.TotalCount);

        var row = Assert.Single(payload.Items);

        Assert.Equal("Low part", row.Name);
        Assert.Equal("Marine Parts Ltd", row.SupplierName);
        Assert.Equal(supplier.Id, row.SupplierId);
    }

    [Fact]
    public async Task Get_projects_stock_reservations_availability_and_shortage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        var item = await SeedItemAsync(
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 8m,
            minimum: 10m);
        var workOrder = await SeedWorkOrderAsync();
        await SeedReservationAsync(workOrder.Id, item.Id, requested: 3m, available: 8m);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);

        var row = Assert.Single(payload.Items);

        Assert.Equal(8m, row.StockOnHand);
        Assert.Equal(3m, row.ReservedQuantity);
        Assert.Equal(5m, row.AvailableQuantity);
        Assert.Equal(10m, row.ReorderLevel);
        Assert.Equal(5m, row.ShortageQuantity);
        Assert.Equal(0m, row.OpenPurchaseOrderQuantity);
    }

    [Fact]
    public async Task Get_counts_open_purchase_order_lines_as_incoming_stock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        var item = await SeedItemAsync(
            supplier.Id,
            "Main engine filter",
            "FILTER-001",
            quantity: 1m,
            minimum: 20m);

        await SeedPurchaseOrderAsync(supplier.Id, item.Id, 5m, PurchaseOrderStatus.Draft);
        await SeedPurchaseOrderAsync(supplier.Id, item.Id, 7m, PurchaseOrderStatus.Ordered);
        await SeedPurchaseOrderAsync(supplier.Id, item.Id, 9m, PurchaseOrderStatus.Received);
        await SeedPurchaseOrderAsync(supplier.Id, item.Id, 4m, PurchaseOrderStatus.Cancelled);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(12m, payload.Items[0].OpenPurchaseOrderQuantity);
    }

    [Fact]
    public async Task Get_classifies_rows_by_availability_and_incoming_stock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        await SeedItemAsync(supplier.Id, "Empty part", "PN-1", quantity: 0m, minimum: 5m);
        await SeedItemAsync(supplier.Id, "Low part", "PN-2", quantity: 4m, minimum: 5m);
        var covered = await SeedItemAsync(
            supplier.Id,
            "Ordered part",
            "PN-3",
            quantity: 4m,
            minimum: 5m);
        await SeedPurchaseOrderAsync(supplier.Id, covered.Id, 10m, PurchaseOrderStatus.Ordered);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);

        var byName = payload.Items.ToDictionary(row => row.Name, row => row.Classification);

        Assert.Equal(InventoryReplenishmentClassifications.Critical, byName["Empty part"]);
        Assert.Equal(InventoryReplenishmentClassifications.Low, byName["Low part"]);
        Assert.Equal(InventoryReplenishmentClassifications.Covered, byName["Ordered part"]);
    }

    [Fact]
    public async Task Get_orders_the_worst_shortage_first()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        await SeedItemAsync(supplier.Id, "Small gap", "PN-3", quantity: 4m, minimum: 5m);
        await SeedItemAsync(supplier.Id, "Huge gap", "PN-1", quantity: 0m, minimum: 20m);
        await SeedItemAsync(supplier.Id, "Tied gap", "PN-2", quantity: 4m, minimum: 5m);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(
            ["Huge gap", "Small gap", "Tied gap"],
            payload.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Get_applies_the_requested_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");

        for (var index = 1; index <= 5; index++)
        {
            await SeedItemAsync(
                supplier.Id,
                $"Part {index:D2}",
                $"PN-{index:D2}",
                quantity: 0m,
                minimum: 1m);
        }

        using var response = await client.GetAsync(
            ReplenishmentUri(pageNumber: 2, pageSize: 2),
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2, payload.PageNumber);
        Assert.Equal(2, payload.PageSize);
        Assert.Equal(5, payload.TotalCount);
        Assert.Equal(["Part 03", "Part 04"], payload.Items.Select(row => row.Name));
    }

    [Fact]
    public async Task Get_uses_the_default_page_when_none_is_requested()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var supplier = await SeedSupplierAsync("Marine Parts Ltd");
        await SeedItemAsync(supplier.Id, "Low part", "PN-1", quantity: 0m, minimum: 5m);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetInventoryReplenishmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.PageNumber);
        Assert.Equal(25, payload.PageSize);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Get_returns_400_for_invalid_paging(int pageNumber, int pageSize)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            ReplenishmentUri(pageNumber, pageSize),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(ReplenishmentUri(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Uri ReplenishmentUri(int? pageNumber = null, int? pageSize = null)
    {
        var query = pageNumber is null && pageSize is null
            ? string.Empty
            : $"?pageNumber={pageNumber ?? 1}&pageSize={pageSize ?? 25}";

        return new Uri($"/api/maintenance/inventory/replenishment{query}", UriKind.Relative);
    }

    private async Task<Supplier> SeedSupplierAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var supplier = Supplier.Register(name, null, null, null, null);

        await dbContext.Set<Supplier>().AddAsync(supplier, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }

    private async Task<InventoryItem> SeedItemAsync(
        Guid supplierId,
        string name,
        string partNumber,
        decimal quantity,
        decimal minimum)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            name,
            partNumber,
            quantity,
            minimum,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task SeedReservationAsync(
        Guid workOrderId,
        Guid inventoryItemId,
        decimal requested,
        decimal available)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            inventoryItemId,
            requested,
            available,
            DateTime.UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task SeedPurchaseOrderAsync(
        Guid supplierId,
        Guid inventoryItemId,
        decimal quantity,
        PurchaseOrderStatus status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var purchaseOrder = PurchaseOrder.Create(supplierId, today, today.AddDays(14));

        switch (status)
        {
            case PurchaseOrderStatus.Ordered:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                break;

            case PurchaseOrderStatus.Received:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Received);
                break;

            case PurchaseOrderStatus.Cancelled:
                purchaseOrder.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
                break;

            default:
                break;
        }

        var line = PurchaseOrderLine.Create(purchaseOrder.Id, inventoryItemId, quantity, 100m);

        await dbContext.Set<PurchaseOrder>().AddAsync(purchaseOrder, CancellationToken.None);
        await dbContext.Set<PurchaseOrderLine>().AddAsync(line, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
