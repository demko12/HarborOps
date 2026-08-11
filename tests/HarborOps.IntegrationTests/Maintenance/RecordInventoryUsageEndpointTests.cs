using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class RecordInventoryItemUsageEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RecordInventoryItemUsageEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_usage_and_reduces_inventory()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var inventoryItem = await SeedInventoryItemAsync(10m);
        var request = CreateValidRequest(2.500m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{inventoryItem.Id}/usage",
            request,
            CancellationToken.None);

        var result = await response.Content.ReadFromJsonAsync<RecordInventoryItemUsageResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(workOrder.Id, result!.WorkOrderId);
        Assert.Equal(inventoryItem.Id, result.PartId);
        Assert.Equal(2.500m, result.QuantityUsed);
        Assert.Equal(7.500m, result.RemainingQuantity);
        Assert.Equal(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{inventoryItem.Id}/usage",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(
                candidate => candidate.Id == inventoryItem.Id,
                CancellationToken.None);

        var usage = await dbContext
            .Set<InventoryItemUsage>()
            .SingleAsync(
                candidate =>
                    candidate.WorkOrderId == workOrder.Id &&
                    candidate.PartId == inventoryItem.Id,
                CancellationToken.None);

        Assert.Equal(7.500m, savedInventoryItem.Quantity);
        Assert.Equal(workOrder.Id, usage.WorkOrderId);
        Assert.Equal(inventoryItem.Id, usage.PartId);
        Assert.Equal(2.500m, usage.QuantityUsed);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_quantity()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest(0m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{Guid.NewGuid()}/parts/{Guid.NewGuid()}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(
            RecordInventoryItemUsageValidator.QuantityUsedField,
            problem.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var inventoryItem = await SeedInventoryItemAsync(10m);
        var workOrderId = Guid.NewGuid();
        var request = CreateValidRequest(2m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/parts/{inventoryItem.Id}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Maintenance work order not found.", problem!.Title);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_inventory_item()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var partId = Guid.NewGuid();
        var request = CreateValidRequest(2m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{partId}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Inventory item not found.", problem!.Title);
    }

    [Fact]
    public async Task Post_returns_conflict_for_invalid_work_order_state()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Canceled);
        var inventoryItem = await SeedInventoryItemAsync(10m);
        var request = CreateValidRequest(2m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{inventoryItem.Id}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            "Inventory item usage could not be recorded.",
            problem!.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(
                candidate => candidate.Id == inventoryItem.Id,
                CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .ToListAsync(CancellationToken.None);

        Assert.Equal(10m, savedInventoryItem.Quantity);
        Assert.Empty(usages);
    }

    [Fact]
    public async Task Post_returns_conflict_for_insufficient_stock_and_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var inventoryItem = await SeedInventoryItemAsync(2m);
        var request = CreateValidRequest(3m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{inventoryItem.Id}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            "Inventory item usage could not be recorded.",
            problem!.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(
                candidate => candidate.Id == inventoryItem.Id,
                CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2m, savedInventoryItem.Quantity);
        Assert.Empty(usages);
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_usage()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var inventoryItem = await SeedInventoryItemAsync(10m);

        await SeedUsageAsync(
            workOrder.Id,
            inventoryItem.Id,
            2m);

        var request = CreateValidRequest(3m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrder.Id}/parts/{inventoryItem.Id}/usage",
            request,
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            "Inventory item usage could not be recorded.",
            problem!.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var savedInventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleAsync(
                candidate => candidate.Id == inventoryItem.Id,
                CancellationToken.None);

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .Where(
                usage =>
                    usage.WorkOrderId == workOrder.Id &&
                    usage.PartId == inventoryItem.Id)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(10m, savedInventoryItem.Quantity);
        Assert.Single(usages);
        Assert.Equal(2m, usages[0].QuantityUsed);
    }

    [Fact]
    public async Task Post_preserves_inventory_item_usage_composite_key()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var firstWorkOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var secondWorkOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var inventoryItem = await SeedInventoryItemAsync(10m);

        var firstRequest = CreateValidRequest(2m);
        var secondRequest = CreateValidRequest(3m);

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{firstWorkOrder.Id}/parts/{inventoryItem.Id}/usage",
            firstRequest,
            CancellationToken.None);

        using var secondResponse = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{secondWorkOrder.Id}/parts/{inventoryItem.Id}/usage",
            secondRequest,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var usages = await dbContext
            .Set<InventoryItemUsage>()
            .Where(
                usage =>
                    usage.PartId == inventoryItem.Id &&
                    (usage.WorkOrderId == firstWorkOrder.Id ||
                     usage.WorkOrderId == secondWorkOrder.Id))
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, usages.Count);

        Assert.Contains(
            usages,
            usage =>
                usage.WorkOrderId == firstWorkOrder.Id &&
                usage.PartId == inventoryItem.Id &&
                usage.QuantityUsed == 2m);

        Assert.Contains(
            usages,
            usage =>
                usage.WorkOrderId == secondWorkOrder.Id &&
                usage.PartId == inventoryItem.Id &&
                usage.QuantityUsed == 3m);

        var entityType = dbContext.Model.FindEntityType(typeof(InventoryItemUsage));
        var primaryKey = entityType!.FindPrimaryKey();

        Assert.NotNull(primaryKey);
        Assert.Equal(
            ["WorkOrderId", "PartId"],
            primaryKey!.Properties.Select(property => property.Name).ToArray());

        Assert.Equal(
            "InventoryItemUsages",
            entityType.GetTableName());

        Assert.Equal(
            MaintenanceModule.Schema,
            entityType.GetSchema());
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        var request = CreateValidRequest(2m);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{Guid.NewGuid()}/parts/{Guid.NewGuid()}/usage",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RecordInventoryItemUsageRequest CreateValidRequest(
        decimal quantityUsed)
    {
        return new RecordInventoryItemUsageRequest
        {
            QuantityUsed = quantityUsed,
        };
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        WorkOrderStatus status)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        switch (status)
        {
            case WorkOrderStatus.Open:
                break;

            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(DateTime.UtcNow);
                break;

            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(
        decimal quantity)
    {
        var inventoryItem = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Main engine filter",
            "FILTER-001",
            quantity,
            2m,
            150m);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItem>().AddAsync(inventoryItem);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return inventoryItem;
    }

    private async Task SeedUsageAsync(
        Guid workOrderId,
        Guid partId,
        decimal quantityUsed)
    {
        var usage = InventoryItemUsage.Create(
            workOrderId,
            partId,
            quantityUsed);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItemUsage>().AddAsync(usage);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
