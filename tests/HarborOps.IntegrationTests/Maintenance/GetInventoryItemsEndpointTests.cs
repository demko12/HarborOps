using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetInventoryItemsEndpointTests : IntegrationTestBase
{
    private static readonly string[] ExpectedItemProperties =
    [
        "categoryId",
        "inventoryItemId",
        "minimumQuantity",
        "name",
        "partNumber",
        "quantity",
        "supplierId",
        "unitCost",
        "unitId",
    ];

    private readonly HarborOpsApiFactory _application;

    public GetInventoryItemsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_correctly_shaped_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var inventoryItem = await SeedInventoryItemAsync("Main engine filter", "FILTER-001", 10m, 2m);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetInventoryItemsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetInventoryItemsValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(inventoryItem.Id, item.InventoryItemId);
        Assert.Equal("Main engine filter", item.Name);
        Assert.Equal("FILTER-001", item.PartNumber);
        Assert.Equal(10m, item.Quantity);
        Assert.Equal(2m, item.MinimumQuantity);
    }

    [Fact]
    public async Task Get_filters_by_search()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var match = await SeedInventoryItemAsync("Fuel Filter", "AB-1000", 10m, 2m);
        await SeedInventoryItemAsync("Impeller", "IMP-1", 10m, 2m);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items?search=Fuel", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetInventoryItemsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(match.Id, Assert.Single(page.Items).InventoryItemId);
    }

    [Fact]
    public async Task Get_filters_by_low_stock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var lowStock = await SeedInventoryItemAsync("Low stock part", "PN-LOW", 1m, 5m);
        await SeedInventoryItemAsync("Healthy part", "PN-HEALTHY", 20m, 5m);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items?lowStock=true", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetInventoryItemsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(lowStock.Id, Assert.Single(page.Items).InventoryItemId);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items?pageSize=101", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetInventoryItemsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_does_not_serialize_domain_entities()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        await SeedInventoryItemAsync("Main engine filter", "FILTER-001", 10m, 2m);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items", UriKind.Relative),
            CancellationToken.None);
        var payload = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(payload);
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        var itemProperties = item.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedItemProperties, itemProperties);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_authentication_but_no_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_succeeds_for_manage_permission_too()
    {
        // manage implies read for the same capability
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/inventory-items", UriKind.Relative),
            CancellationToken.None);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(
        string name,
        string partNumber,
        decimal quantity,
        decimal minimumQuantity)
    {
        var inventoryItem = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity,
            150m);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return inventoryItem;
    }
}
