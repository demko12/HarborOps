using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class CreateMaintenanceWorkOrderEndpointTests: IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public CreateMaintenanceWorkOrderEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_creates_and_persists_work_order()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/maintenance/work-orders",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CreateMaintenanceWorkOrderResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.Id);
        Assert.Equal(
            $"/api/maintenance/work-orders/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(MaintenanceWorkOrder));

        Assert.Equal(request.VesselId, workOrder.VesselId);
        Assert.Equal(request.Title, workOrder.Title);
        Assert.Equal(request.Description, workOrder.Description);
        Assert.Equal(WorkOrderPriority.High, workOrder.Priority);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.Null(workOrder.DryDockId);
        Assert.Null(workOrder.AssignedEngineerId);
        Assert.Null(workOrder.CompletedUtc);
        Assert.Equal("MaintenanceWorkOrders", entityType!.GetTableName());
        Assert.Equal(MaintenanceModule.Schema, entityType!.GetSchema());

    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new CreateMaintenanceWorkOrderRequest
        {
            VesselId = Guid.Empty,
            Title = string.Empty,
            Description = null,
            Priority = "Urgent",
            DueUtc = DateTime.UtcNow.AddDays(-1),
        };

        using var response = await client.PostAsJsonAsync(
            "/api/maintenance/work-orders",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CreateMaintenanceWorkOrderValidator.VesselIdField, problem.Errors);
        Assert.Contains(CreateMaintenanceWorkOrderValidator.TitleField, problem.Errors);
        Assert.Contains(CreateMaintenanceWorkOrderValidator.PriorityField, problem.Errors);
        Assert.Contains(CreateMaintenanceWorkOrderValidator.DueUtcField, problem.Errors);
    }

    private static CreateMaintenanceWorkOrderRequest CreateValidRequest()
    {
        return new CreateMaintenanceWorkOrderRequest
        {
            VesselId = Guid.NewGuid(),
            Title = "Replace main engine filter",
            Description = "Filter is due for replacement",
            Priority = "High",
            DueUtc = DateTime.UtcNow.AddDays(7)
        };
    }
}
