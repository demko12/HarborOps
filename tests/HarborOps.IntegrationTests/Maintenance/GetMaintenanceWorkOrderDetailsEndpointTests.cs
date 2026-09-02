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

public sealed class GetMaintenanceWorkOrderDetailsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetMaintenanceWorkOrderDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_the_work_order_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var engineerId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId, assignedEngineerId: engineerId);

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(workOrder.Id, details.WorkOrderId);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Equal(engineerId, details.AssignedEngineerId);
        Assert.Equal("Replace main engine filter", details.Title);
        Assert.Equal("High", details.Priority);
        Assert.Equal("Open", details.Status);
        Assert.Null(details.CompletedUtc);
    }

    [Fact]
    public async Task Get_returns_empty_collections_for_a_work_order_without_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Empty(details.Equipment);
        Assert.Empty(details.PartUsages);
        Assert.Null(details.Completion);
    }

    [Fact]
    public async Task Get_returns_the_linked_equipment_of_the_work_order_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var mainEngine = await SeedEquipmentAsync(vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(vesselId, "Boiler");
        await SeedLinksAsync(workOrder.Id, mainEngine.Id, boiler.Id);

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal([boiler.Id, mainEngine.Id], details.Equipment.Select(item => item.EquipmentId));
        Assert.Equal("Boiler", details.Equipment[0].Name);
        Assert.Equal(vesselId, details.Equipment[0].VesselId);
    }

    [Fact]
    public async Task Get_returns_the_recorded_part_usage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());
        var part = await SeedInventoryItemAsync("Main engine filter", "FILTER-001", quantity: 7.5m);
        await SeedUsageAsync(workOrder.Id, part.Id, quantityUsed: 2.5m);

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);

        var usage = Assert.Single(details.PartUsages);

        Assert.Equal(part.Id, usage.PartId);
        Assert.Equal("FILTER-001", usage.PartNumber);
        Assert.Equal(2.5m, usage.QuantityUsed);
        Assert.Equal(7.5m, usage.RemainingQuantity);
    }

    [Fact]
    public async Task Get_returns_the_completion_record_of_a_completed_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var technicianId = Guid.NewGuid();
        var completedUtc = DateTime.UtcNow.AddDays(-1);
        var workOrder = await SeedWorkOrderAsync(
            Guid.NewGuid(),
            status: WorkOrderStatus.Completed,
            completedUtc: completedUtc);
        await SeedCompletionRecordAsync(workOrder.Id, technicianId, completedUtc);

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("Completed", details.Status);
        Assert.NotNull(details.Completion);
        Assert.Equal(technicianId, details.Completion!.TechnicianId);
        Assert.Equal(DateOnly.FromDateTime(completedUtc), details.Completion.CompletionDate);
        Assert.Equal(1_250.50m, details.Completion.Cost);
        Assert.Equal(6.5m, details.Completion.LaborHours);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Open, new[] { "Start", "Cancel", "ReplaceEquipment" })]
    [InlineData(WorkOrderStatus.InProgress, new[] { "Cancel", "ReplaceEquipment" })]
    [InlineData(WorkOrderStatus.Completed, new string[0])]
    [InlineData(WorkOrderStatus.Canceled, new string[0])]
    public async Task Get_reports_the_actions_allowed_for_the_current_status(
        WorkOrderStatus status,
        string[] expected)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid(), status: status);

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetMaintenanceWorkOrderDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(status.ToString(), details.Status);
        Assert.Equal(expected, details.AllowedActions);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(DetailsUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(DetailsUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_allowed_with_only_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage,
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());

        using var response = await client.GetAsync(DetailsUri(workOrder.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Uri DetailsUri(Guid workOrderId)
    {
        return new Uri($"/api/maintenance/work-orders/{workOrderId}", UriKind.Relative);
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        Guid vesselId,
        WorkOrderStatus status = WorkOrderStatus.Open,
        Guid? assignedEngineerId = null,
        DateTime? completedUtc = null)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
            "Replace main engine filter",
            "Filter is due for replacement",
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7),
            dryDockId: null,
            assignedEngineerId: assignedEngineerId);

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(completedUtc ?? DateTime.UtcNow);
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

    private async Task SeedLinksAsync(Guid workOrderId, params Guid[] equipmentIds)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<WorkOrderEquipment>().AddRangeAsync(
            equipmentIds.Select(equipmentId => WorkOrderEquipment.Create(workOrderId, equipmentId)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
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
            2m,
            150m);

        await dbContext.Set<InventoryItem>().AddAsync(item, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task SeedUsageAsync(Guid workOrderId, Guid partId, decimal quantityUsed)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<InventoryItemUsage>().AddAsync(
            InventoryItemUsage.Create(workOrderId, partId, quantityUsed),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task SeedCompletionRecordAsync(
        Guid workOrderId,
        Guid technicianId,
        DateTime completedUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<MaintenanceRecord>().AddAsync(
            MaintenanceRecord.Create(
                workOrderId,
                technicianId,
                DateOnly.FromDateTime(completedUtc),
                "Filter replaced and tested",
                1_250.50m,
                6.5m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
