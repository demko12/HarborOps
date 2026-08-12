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

public sealed class GetMaintenanceWorkOrdersEndpointTests : IntegrationTestBase
{
    private static readonly string[] ExpectedItemProperties =
    [
        "assignedEngineerId",
        "completedUtc",
        "createdUtc",
        "description",
        "dueUtc",
        "priority",
        "status",
        "title",
        "vesselId",
        "workOrderId",
    ];

    private readonly HarborOpsApiFactory _application;

    public GetMaintenanceWorkOrdersEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_correctly_shaped_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var workOrderId = await SeedWorkOrderAsync(vesselId, WorkOrderPriority.High, DateTime.UtcNow.AddDays(3));

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders?vesselId={vesselId}", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrdersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(GetMaintenanceWorkOrdersValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);
        Assert.Equal(workOrderId, item.WorkOrderId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("High", item.Priority);
        Assert.Equal("Open", item.Status);
    }

    [Fact]
    public async Task Get_returns_empty_items_when_nothing_matches()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders?vesselId={Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrdersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Get_filters_by_overdue()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var overdueId = await SeedWorkOrderAsync(vesselId, WorkOrderPriority.Medium, DateTime.UtcNow.AddDays(-1));
        await SeedWorkOrderAsync(vesselId, WorkOrderPriority.Medium, DateTime.UtcNow.AddDays(5));

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders?vesselId={vesselId}&overdue=true", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrdersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(overdueId, Assert.Single(page.Items).WorkOrderId);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/work-orders?pageSize=101", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetMaintenanceWorkOrdersValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_invalid_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/work-orders?status=Urgent", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetMaintenanceWorkOrdersValidator.StatusField, problem.Errors);
    }

    [Fact]
    public async Task Get_does_not_serialize_domain_entities()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        await SeedWorkOrderAsync(vesselId, WorkOrderPriority.Medium, DateTime.UtcNow.AddDays(2));

        using var response = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders?vesselId={vesselId}", UriKind.Relative),
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
        Assert.DoesNotContain("dryDockId", itemProperties, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/work-orders", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedWorkOrderAsync(Guid vesselId, WorkOrderPriority priority, DateTime dueUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
            "Replace filter",
            "Routine replacement",
            priority,
            dueUtc);

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }
}
