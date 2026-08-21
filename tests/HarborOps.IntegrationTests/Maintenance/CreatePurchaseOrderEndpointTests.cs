using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class CreatePurchaseOrderEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CreatePurchaseOrderEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_and_persists_the_draft_order_with_lines()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var filter = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1", 0m);
        var impeller = await SeedInventoryItemAsync(supplier.Id, "Impeller", "PN-2", 0m);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = supplier.Id,
                orderDate = "2026-08-20",
                expectedDelivery = "2026-08-30",
                lines = new[]
                {
                    new { inventoryItemId = filter.Id, quantityOrdered = 4m, unitCost = 25.50m },
                    new { inventoryItemId = impeller.Id, quantityOrdered = 2m, unitCost = 100m },
                },
            },
            CancellationToken.None);

        var result = await response.Content
            .ReadFromJsonAsync<CreatePurchaseOrderResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(PurchaseOrderStatus.Draft), result.Status);
        Assert.Equal(2, result.LineCount);
        Assert.Equal(302m, result.TotalCost);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var storedOrder = await dbContext.Set<PurchaseOrder>().SingleAsync(CancellationToken.None);
        var storedLines = await dbContext.Set<PurchaseOrderLine>().ToListAsync(CancellationToken.None);

        Assert.Equal(result.PurchaseOrderId, storedOrder.Id);
        Assert.Equal(supplier.Id, storedOrder.SupplierId);
        Assert.Equal(new DateOnly(2026, 8, 20), storedOrder.OrderDate);
        Assert.Equal(new DateOnly(2026, 8, 30), storedOrder.ExpectedDelivery);
        Assert.Equal(PurchaseOrderStatus.Draft, storedOrder.Status);
        Assert.Equal(2, storedLines.Count);
    }

    [Fact]
    public async Task Post_returns_400_without_lines()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = supplier.Id,
                orderDate = "2026-08-20",
                lines = Array.Empty<object>(),
            },
            CancellationToken.None);

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CreatePurchaseOrderValidator.LinesField, problem.Errors);
        await AssertNoOrderPersistedAsync();
    }

    [Fact]
    public async Task Post_returns_400_for_duplicate_item_lines()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1", 0m);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = supplier.Id,
                orderDate = "2026-08-20",
                lines = new[]
                {
                    new { inventoryItemId = item.Id, quantityOrdered = 1m, unitCost = 10m },
                    new { inventoryItemId = item.Id, quantityOrdered = 2m, unitCost = 10m },
                },
            },
            CancellationToken.None);

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains($"{CreatePurchaseOrderValidator.LinesField}[1].inventoryItemId", problem.Errors);
        await AssertNoOrderPersistedAsync();
    }

    [Fact]
    public async Task Post_returns_400_for_a_non_positive_quantity()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1", 0m);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = supplier.Id,
                orderDate = "2026-08-20",
                lines = new[] { new { inventoryItemId = item.Id, quantityOrdered = 0m, unitCost = 10m } },
            },
            CancellationToken.None);

        var problem = await response.Content
            .ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains($"{CreatePurchaseOrderValidator.LinesField}[0].quantityOrdered", problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_supplier()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();
        var item = await SeedInventoryItemAsync(supplier.Id, "Fuel filter", "PN-1", 0m);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = Guid.NewGuid(),
                orderDate = "2026-08-20",
                lines = new[] { new { inventoryItemId = item.Id, quantityOrdered = 1m, unitCost = 10m } },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoOrderPersistedAsync();
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_inventory_item()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var supplier = await SeedSupplierAsync();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new
            {
                supplierId = supplier.Id,
                orderDate = "2026-08-20",
                lines = new[] { new { inventoryItemId = Guid.NewGuid(), quantityOrdered = 1m, unitCost = 10m } },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoOrderPersistedAsync();
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new { supplierId = Guid.NewGuid(), orderDate = "2026-08-20" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/maintenance/purchase-orders", UriKind.Relative),
            new { supplierId = Guid.NewGuid(), orderDate = "2026-08-20" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task AssertNoOrderPersistedAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Empty(await dbContext.Set<PurchaseOrder>().ToListAsync(CancellationToken.None));
        Assert.Empty(await dbContext.Set<PurchaseOrderLine>().ToListAsync(CancellationToken.None));
    }

    private async Task<Supplier> SeedSupplierAsync()
    {
        var supplier = Supplier.Register("Baltic Marine Parts", "Ana Ilic", null, null, null);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<Supplier>().AddAsync(supplier);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return supplier;
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(
        Guid supplierId,
        string name,
        string partNumber,
        decimal quantity)
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            supplierId,
            name,
            partNumber,
            quantity,
            1m,
            10m);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItem>().AddAsync(item);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }
}
