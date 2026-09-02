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

public sealed class ReplaceWorkOrderEquipmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ReplaceWorkOrderEquipmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_returns_200_and_persists_the_new_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var mainEngine = await SeedEquipmentAsync(vesselId, "Main engine");
        var boiler = await SeedEquipmentAsync(vesselId, "Boiler");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { mainEngine.Id, boiler.Id } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderEquipmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(workOrder.Id, payload.WorkOrderId);
        Assert.Equal("Open", payload.Status);
        Assert.Equal(["Start", "Cancel", "ReplaceEquipment"], payload.AllowedActions);
        Assert.Equal([boiler.Id, mainEngine.Id], payload.Equipment.Select(item => item.EquipmentId));
        Assert.Equal([boiler.Id, mainEngine.Id], await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_replaces_the_previous_scope_without_duplicating_links()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var kept = await SeedEquipmentAsync(vesselId, "Main engine");
        var removed = await SeedEquipmentAsync(vesselId, "Bow thruster");
        var added = await SeedEquipmentAsync(vesselId, "Boiler");
        await SeedLinksAsync(workOrder.Id, kept.Id, removed.Id);

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { kept.Id, added.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([added.Id, kept.Id], await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_accepts_an_empty_selection_and_clears_the_scope()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");
        await SeedLinksAsync(workOrder.Id, equipment.Id);

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = Array.Empty<Guid>() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_scopes_a_work_order_that_already_started()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId, WorkOrderStatus.InProgress);
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { equipment.Id } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderEquipmentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("InProgress", payload.Status);
        Assert.Equal(["Cancel", "ReplaceEquipment"], payload.AllowedActions);
    }

    [Fact]
    public async Task Put_returns_400_for_a_duplicated_selection()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { equipment.Id, equipment.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "equipmentIds",
            await response.Content.ReadAsStringAsync(CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Empty(await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_400_for_an_empty_equipment_identifier()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { Guid.Empty } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_400_when_the_selection_is_missing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(Guid.NewGuid());

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var equipment = await SeedEquipmentAsync(Guid.NewGuid(), "Main engine");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(Guid.NewGuid()),
            new { equipmentIds = new[] { equipment.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_equipment_row()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var known = await SeedEquipmentAsync(vesselId, "Main engine");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { known.Id, Guid.NewGuid() } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_409_for_equipment_of_another_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var foreign = await SeedEquipmentAsync(Guid.NewGuid(), "Foreign boiler");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { foreign.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_replacement_leaves_the_previous_scope_unchanged()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var first = await SeedEquipmentAsync(vesselId, "Main engine");
        var second = await SeedEquipmentAsync(vesselId, "Boiler");
        var foreign = await SeedEquipmentAsync(Guid.NewGuid(), "Foreign pump");
        await SeedLinksAsync(workOrder.Id, first.Id, second.Id);

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { foreign.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal([second.Id, first.Id], await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task Put_returns_409_for_a_closed_work_order(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId, status);
        var existing = await SeedEquipmentAsync(vesselId, "Main engine");
        var wanted = await SeedEquipmentAsync(vesselId, "Boiler");
        await SeedLinksAsync(workOrder.Id, existing.Id);

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { wanted.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal([existing.Id], await StoredEquipmentIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(Guid.NewGuid()),
            new { equipmentIds = Array.Empty<Guid>() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_403_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var vesselId = Guid.NewGuid();
        var workOrder = await SeedWorkOrderAsync(vesselId);
        var equipment = await SeedEquipmentAsync(vesselId, "Main engine");

        using var response = await client.PutAsJsonAsync(
            EquipmentUri(workOrder.Id),
            new { equipmentIds = new[] { equipment.Id } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await StoredEquipmentIdsAsync(workOrder.Id));
    }

    private static Uri EquipmentUri(Guid workOrderId)
    {
        return new Uri($"/api/maintenance/work-orders/{workOrderId}/equipment", UriKind.Relative);
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        Guid vesselId,
        WorkOrderStatus status = WorkOrderStatus.Open)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
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

    private async Task SeedLinksAsync(Guid workOrderId, params Guid[] equipmentIds)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<WorkOrderEquipment>().AddRangeAsync(
            equipmentIds.Select(equipmentId => WorkOrderEquipment.Create(workOrderId, equipmentId)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<Guid>> StoredEquipmentIdsAsync(Guid workOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await (
            from link in dbContext.Set<WorkOrderEquipment>().AsNoTracking()
            join equipment in dbContext.Set<Equipment>().AsNoTracking()
                on link.EquipmentId equals equipment.Id
            where link.WorkOrderId == workOrderId
            orderby equipment.Name, equipment.Id
            select link.EquipmentId)
            .ToListAsync(CancellationToken.None);
    }
}
