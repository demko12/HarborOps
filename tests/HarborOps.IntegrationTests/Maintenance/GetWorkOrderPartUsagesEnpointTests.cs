using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class GetWorkOrderPartUsagesEndpointTests : IntegrationTestBase
{
    private static readonly string[] ExpectedItemProperties =
    [
        "name",
        "partId",
        "partNumber",
        "quantityUsed",
        "remainingQuantity",
    ];

    private readonly HarborOpsApiFactory _application;

    public GetWorkOrderPartUsagesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_usage_rows_for_the_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var (workOrder, inventoryItem) = await SeedWorkOrderWithUsageAsync(7.5m, 2.5m);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/part-usages", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetWorkOrderPartUsagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);

        var item = Assert.Single(page.Items);
        Assert.Equal(inventoryItem.Id, item.PartId);
        Assert.Equal(2.5m, item.QuantityUsed);
        Assert.Equal(7.5m, item.RemainingQuantity);
    }

    [Fact]
    public async Task Get_returns_empty_list_when_no_usage_recorded()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/part-usages", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetWorkOrderPartUsagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{Guid.NewGuid()}/part-usages", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Get_does_not_serialize_domain_entities()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var (workOrder, _) = await SeedWorkOrderWithUsageAsync(7.5m, 2.5m);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}/part-usages", UriKind.Relative),
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
            new Uri($"/api/maintenance/work-orders/{Guid.NewGuid()}/part-usages", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    //[Fact]
    //public async Task Get_with_authentication_but_no_read_permission_returns_403()
    //{
    //    using var client = await _application.CreateAuthenticatedClientAsync();

    //    using var response = await client.GetAsync(
    //        new Uri(
    //            $"/api/maintenance/work-orders/{Guid.NewGuid()}/part-usages",
    //            UriKind.Relative),
    //        CancellationToken.None);

    //    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    //}

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync()
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task<(MaintenanceWorkOrder WorkOrder, InventoryItem InventoryItem)> SeedWorkOrderWithUsageAsync(
        decimal remainingQuantity,
        decimal quantityUsed)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        var inventoryItem = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            remainingQuantity,
            2m,
            150m);

        var usage = InventoryItemUsage.Create(workOrder.Id, inventoryItem.Id, quantityUsed);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.Set<InventoryItemUsage>().AddAsync(usage);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (workOrder, inventoryItem);
    }
}
