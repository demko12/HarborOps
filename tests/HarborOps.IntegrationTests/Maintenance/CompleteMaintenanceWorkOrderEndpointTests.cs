using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using HarborOps.Modules.Maintenance.Presentation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class CompleteMaintenanceWorkOrderEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CompleteMaintenanceWorkOrderEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_completes_work_order_and_persists_maintenance_record()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var workOrderId = await SeedWorkOrderAsync();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CompleteMaintenanceWorkOrderResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(workOrderId, result.WorkOrderId);
        Assert.NotEqual(Guid.Empty, result.MaintenanceRecordId);
        Assert.Equal(request.CompletedUtc, result.CompletedUtc);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleAsync(
                candidate => candidate.Id == workOrderId,
                CancellationToken.None);
        var maintenanceRecord = await dbContext
            .Set<MaintenanceRecord>()
            .SingleAsync(
                candidate => candidate.Id == result.MaintenanceRecordId,
                CancellationToken.None);

        Assert.Equal(WorkOrderStatus.Completed, workOrder.Status);
        Assert.Equal(request.CompletedUtc, workOrder.CompletedUtc);
        Assert.Equal(workOrderId, maintenanceRecord.WorkOrderId);
        Assert.Equal(request.TechnicianId, maintenanceRecord.TechnicianId);
        Assert.Equal(request.Cost, maintenanceRecord.Cost);
        Assert.Equal(request.LaborHours, maintenanceRecord.LaborHours);
        Assert.Equal(request.Notes, maintenanceRecord.Notes);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{Guid.NewGuid()}/completion",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var workOrderId = await SeedWorkOrderAsync();
        var request = new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.Empty,
            CompletedUtc = DateTime.UtcNow.AddDays(1),
            Cost = -10m,
            LaborHours = 0m,
        };

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.TechnicianIdField, problem.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CompletedUtcField, problem.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.CostField, problem.Errors);
        Assert.Contains(CompleteMaintenanceWorkOrderValidator.LaborHoursField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_already_completed_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var workOrderId = await SeedWorkOrderAsync();
        var request = CreateValidRequest();

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            request,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var recordCount = await dbContext
            .Set<MaintenanceRecord>()
            .CountAsync(
                record => record.WorkOrderId == workOrderId,
                CancellationToken.None);

        Assert.Equal(1, recordCount);
    }

    [Fact]
    public async Task Post_returns_conflict_for_canceled_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        var workOrderId = await SeedCanceledWorkOrderAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_completes_a_generated_work_order_and_advances_its_schedule_once()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var (scheduleId, workOrderId) = await SeedGeneratedWorkOrderAsync(dueDate);

        using var first = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);
        using var second = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var occurrence = await dbContext.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.ScheduleId == scheduleId, CancellationToken.None);
        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scheduleId, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ScheduleOccurrenceState.Completed, occurrence.State);
        Assert.Equal(dueDate.AddMonths(1), schedule.NextDueDate);
    }

    [Fact]
    public async Task Post_returns_409_for_a_generated_work_order_without_its_occurrence()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var (_, workOrderId) = await SeedGeneratedWorkOrderAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            seedOccurrence: false);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == workOrderId, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(WorkOrderStatus.Open, workOrder.Status);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_409_when_the_generated_work_orders_schedule_is_hour_based()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var (scheduleId, workOrderId) = await SeedGeneratedWorkOrderAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            frequency: MaintenanceFrequency.RunningHours);

        using var response = await client.PostAsJsonAsync(
            $"/api/maintenance/work-orders/{workOrderId}/completion",
            CreateValidRequest(),
            CancellationToken.None);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var occurrence = await dbContext.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.ScheduleId == scheduleId, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.False(await dbContext.Set<MaintenanceRecord>().AnyAsync(CancellationToken.None));
    }
    private async Task<Guid> SeedWorkOrderAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace filter",
            "Routine replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(3));

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);
        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }

    private async Task<Guid> SeedCanceledWorkOrderAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace filter",
            "Routine replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(3));

        workOrder.Cancel();

        await dbContext.Set<MaintenanceWorkOrder>()
            .AddAsync(workOrder);

        await dbContext.SaveChangesAsync();

        return workOrder.Id;
    }

    private static CompleteMaintenanceWorkOrderRequest CreateValidRequest()
    {
        return new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.NewGuid(),
            CompletedUtc = DateTime.UtcNow,
            Notes = "Filter replaced successfully.",
            Cost = 150.50m,
            LaborHours = 2.5m,
        };
    }

    private async Task<(Guid ScheduleId, Guid WorkOrderId)> SeedGeneratedWorkOrderAsync(
    DateOnly dueDate,
    bool seedOccurrence = true,
    MaintenanceFrequency frequency = MaintenanceFrequency.Monthly)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var schedule = MaintenanceSchedule.Create(
            "Main engine overhaul",
            frequency,
            dueDate,
            frequency is MaintenanceFrequency.RunningHours ? 500 : null);
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            Guid.NewGuid(),
            "Main engine overhaul - due",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate);

        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule);
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder);

        if (seedOccurrence)
        {
            await dbContext.Set<ScheduleOccurrence>().AddAsync(
                ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, DateTime.UtcNow));
        }

        await dbContext.SaveChangesAsync();

        return (schedule.Id, workOrder.Id);
    }
}
