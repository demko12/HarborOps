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

public sealed class MaintenanceWorkOrderTransitionEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public MaintenanceWorkOrderTransitionEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Start_returns_200_and_moves_the_work_order_in_progress()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);
        var transition = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrderTransitionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(transition);
        Assert.Equal(workOrder.Id, transition.WorkOrderId);
        Assert.Equal("InProgress", transition.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], transition.AllowedActions);
        Assert.Null(transition.CompletedUtc);
        Assert.Equal(WorkOrderStatus.InProgress, (await StoredAsync(workOrder.Id)).Status);
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task Start_returns_409_for_a_work_order_that_is_not_open(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(status);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await StoredAsync(workOrder.Id)).Status);
    }

    [Fact]
    public async Task Start_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsync(
            ActionUri(Guid.NewGuid(), "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Open)]
    [InlineData(WorkOrderStatus.InProgress)]
    public async Task Cancel_returns_200_for_an_open_or_running_work_order(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(status);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "cancel"),
            content: null,
            CancellationToken.None);
        var transition = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrderTransitionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(transition);
        Assert.Equal("Canceled", transition.Status);
        Assert.Empty(transition.AllowedActions);

        var stored = await StoredAsync(workOrder.Id);

        Assert.Equal(WorkOrderStatus.Canceled, stored.Status);
        Assert.Null(stored.CompletedUtc);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task Cancel_returns_409_for_a_closed_work_order(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(status);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await StoredAsync(workOrder.Id)).Status);
    }

    [Fact]
    public async Task Cancel_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsync(
            ActionUri(Guid.NewGuid(), "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancelling_keeps_the_equipment_scope_and_the_completion_history()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.InProgress, vesselId);
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");
        await SeedLinkAsync(workOrder.Id, equipment.Id);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var links = await dbContext.Set<WorkOrderEquipment>()
            .AsNoTracking()
            .Where(link => link.WorkOrderId == workOrder.Id)
            .ToListAsync(CancellationToken.None);

        var link = Assert.Single(links);
        Assert.Equal(equipment.Id, link.EquipmentId);
    }

    [Fact]
    public async Task A_started_work_order_is_reported_by_the_details_endpoint()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage,
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);

        using var start = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);
        using var details = await client.GetAsync(
            new Uri($"/api/maintenance/work-orders/{workOrder.Id}", UriKind.Relative),
            CancellationToken.None);
        var payload = await details.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("InProgress", payload.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], payload.AllowedActions);
    }

    [Fact]
    public async Task Every_transition_route_rejects_an_anonymous_client()
    {
        using var client = _application.CreateClient();
        var workOrderId = Guid.NewGuid();

        using var start = await client.PostAsync(
            ActionUri(workOrderId, "start"),
            content: null,
            CancellationToken.None);
        using var cancel = await client.PostAsync(
            ActionUri(workOrderId, "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, start.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancel.StatusCode);
    }

    [Fact]
    public async Task Transitions_are_forbidden_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);

        using var start = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);
        using var cancel = await client.PostAsync(
            ActionUri(workOrder.Id, "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.Equal(WorkOrderStatus.Open, (await StoredAsync(workOrder.Id)).Status);
    }

    [Fact]
    public async Task Start_returns_409_when_a_reservation_is_not_fully_covered()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var filter = await SeedInventoryItemAsync("Main engine filter", "FILTER-001", quantity: 1m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 5m, available: 1m);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(WorkOrderStatus.Open, (await StoredAsync(workOrder.Id)).Status);
    }

    [Fact]
    public async Task Start_returns_200_when_every_reservation_is_covered()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var filter = await SeedInventoryItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WorkOrderStatus.InProgress, (await StoredAsync(workOrder.Id)).Status);
    }

    [Fact]
    public async Task A_shortage_on_another_work_order_does_not_block_the_start()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var other = await SeedWorkOrderAsync(WorkOrderStatus.Open);
        var filter = await SeedInventoryItemAsync("Main engine filter", "FILTER-001", quantity: 1m);
        await SeedReservationAsync(other.Id, filter.Id, requested: 5m, available: 1m);

        using var response = await client.PostAsync(
            ActionUri(workOrder.Id, "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Start_returns_200_for_a_generated_work_order_and_keeps_its_occurrence_generated()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var (scheduleId, workOrderId) = await SeedGeneratedWorkOrderAsync(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        using var response = await client.PostAsync(
            ActionUri(workOrderId, "start"),
            content: null,
            CancellationToken.None);

        var occurrence = await StoredOccurrenceAsync(scheduleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WorkOrderStatus.InProgress, (await StoredAsync(workOrderId)).Status);
        Assert.Equal(ScheduleOccurrenceState.Generated, occurrence.State);
        Assert.Null(occurrence.ResolvedUtc);
    }

    [Fact]
    public async Task Cancel_returns_200_for_a_generated_work_order_and_does_not_advance_its_schedule()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var (scheduleId, workOrderId) = await SeedGeneratedWorkOrderAsync(dueDate);

        using var response = await client.PostAsync(
            ActionUri(workOrderId, "cancel"),
            content: null,
            CancellationToken.None);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var schedule = await dbContext.Set<MaintenanceSchedule>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scheduleId, CancellationToken.None);
        var occurrence = await StoredOccurrenceAsync(scheduleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WorkOrderStatus.Canceled, (await StoredAsync(workOrderId)).Status);
        Assert.Equal(ScheduleOccurrenceState.Cancelled, occurrence.State);
        Assert.Equal(workOrderId, occurrence.WorkOrderId);
        Assert.Equal(dueDate, schedule.NextDueDate);
    }

    private async Task<InventoryItem> SeedInventoryItemAsync(
        string name,
        string partNumber,
        decimal quantity)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity: 2m,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
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

    private static Uri ActionUri(Guid workOrderId, string action)
    {
        return new Uri($"/api/maintenance/work-orders/{workOrderId}/{action}", UriKind.Relative);
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        WorkOrderStatus status,
        Guid? vesselId = null)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            vesselId ?? Guid.NewGuid(),
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        switch (status)
        {
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
                break;
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task<Equipment> SeedEquipmentAsync(Guid vesselId, string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var equipment = Equipment.Register(vesselId, name, "Propulsion", "MAN", null);

        await dbContext.Set<Equipment>().AddAsync(equipment, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return equipment;
    }

    private async Task SeedLinkAsync(Guid workOrderId, Guid equipmentId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<WorkOrderEquipment>().AddAsync(
            WorkOrderEquipment.Create(workOrderId, equipmentId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<MaintenanceWorkOrder> StoredAsync(Guid workOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == workOrderId, CancellationToken.None);
    }

    private async Task<(Guid ScheduleId, Guid WorkOrderId)> SeedGeneratedWorkOrderAsync(DateOnly dueDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var equipment = Equipment.Register(Guid.NewGuid(), "Main engine", "Propulsion", "MAN", null);
        var schedule = MaintenanceSchedule.Create("Main engine overhaul", MaintenanceFrequency.Monthly, dueDate, null);
        var workOrder = MaintenanceWorkOrder.CreateFromSchedule(
            equipment.VesselId,
            "Main engine overhaul - due",
            null,
            WorkOrderPriority.Medium,
            schedule.Id,
            dueDate);

        await dbContext.Set<Equipment>().AddAsync(equipment, CancellationToken.None);
        await dbContext.Set<MaintenanceSchedule>().AddAsync(schedule, CancellationToken.None);
        await dbContext.Set<ScheduleEquipment>().AddAsync(
            ScheduleEquipment.Create(schedule.Id, equipment.Id),
            CancellationToken.None);
        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, CancellationToken.None);
        await dbContext.Set<WorkOrderEquipment>().AddAsync(
            WorkOrderEquipment.Create(workOrder.Id, equipment.Id),
            CancellationToken.None);
        await dbContext.Set<ScheduleOccurrence>().AddAsync(
            ScheduleOccurrence.Generate(schedule.Id, dueDate, workOrder.Id, DateTime.UtcNow),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (schedule.Id, workOrder.Id);
    }

    private async Task<ScheduleOccurrence> StoredOccurrenceAsync(Guid scheduleId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.ScheduleId == scheduleId, CancellationToken.None);
    }
}
