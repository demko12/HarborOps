using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class DryDockEndpointTests : IntegrationTestBase
{
    private static readonly Uri DryDocksUri = new("/api/maintenance/dry-docks", UriKind.Relative);
    private static readonly DateOnly StartDate = new(2026, 9, 1);

    private readonly HarborOpsApiFactory _application;

    public DryDockEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_and_persists_a_planned_dry_dock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();
        var portId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            DryDocksUri,
            new
            {
                vesselId,
                portId,
                startDate = StartDate,
                plannedTasks = "Hull blasting and coating",
            },
            CancellationToken.None);

        var created = await response.Content.ReadFromJsonAsync<ScheduleDryDockResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        var stored = await SingleStoredAsync();

        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(vesselId, stored.VesselId);
        Assert.Equal(portId, stored.PortId);
        Assert.Equal(DryDockStatus.Planned, stored.Status);
    }

    [Fact]
    public async Task Post_returns_400_for_a_request_without_a_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            DryDocksUri,
            new { portId = Guid.NewGuid(), startDate = StartDate, plannedTasks = "Work" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vesselId", await response.Content.ReadAsStringAsync(CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_returns_200_with_the_paged_board_shape()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        await SeedAsync(DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Rudder survey"));

        using var response = await client.GetAsync(DryDocksUri, CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetDryDocksResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.TotalCount);

        var item = Assert.Single(payload.Items);

        Assert.Equal("Rudder survey", item.PlannedTasks);
        Assert.Equal("Planned", item.Status);
    }

    [Fact]
    public async Task Get_returns_400_for_an_unknown_status_filter()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            new Uri("/api/maintenance/dry-docks?status=Sailing", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_details_returns_200_with_the_allowed_next_statuses()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Propeller work");
        await SeedAsync(dryDock);

        using var response = await client.GetAsync(DetailsUri(dryDock.Id), CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetDryDockDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("Planned", details.Status);
        Assert.Equal(["InProgress", "Canceled"], details.AllowedNextStatuses);
    }

    [Fact]
    public async Task Get_details_returns_404_for_an_unknown_dry_dock()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(DetailsUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_returns_200_and_moves_the_dry_dock_in_progress()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        await SeedAsync(dryDock);

        using var response = await client.PostAsync(
            ActionUri(dryDock.Id, "start"),
            content: null,
            CancellationToken.None);

        var transition = await response.Content.ReadFromJsonAsync<DryDockTransitionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(transition);
        Assert.Equal("InProgress", transition.Status);
        Assert.Equal(["Completed"], transition.AllowedNextStatuses);
        Assert.Equal(DryDockStatus.InProgress, (await SingleStoredAsync()).Status);
    }

    [Fact]
    public async Task Start_returns_409_for_a_dry_dock_that_already_started()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        dryDock.Start();
        await SeedAsync(dryDock);

        using var response = await client.PostAsync(
            ActionUri(dryDock.Id, "start"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Complete_returns_200_and_persists_the_end_date_and_cost()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        dryDock.Start();
        await SeedAsync(dryDock);

        using var response = await client.PostAsJsonAsync(
            ActionUri(dryDock.Id, "complete"),
            new { endDate = StartDate.AddDays(12), actualCost = 91_250.25m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await SingleStoredAsync();

        Assert.Equal(DryDockStatus.Completed, stored.Status);
        Assert.Equal(StartDate.AddDays(12), stored.EndDate);
        Assert.Equal(91_250.25m, stored.ActualCost);
    }

    [Fact]
    public async Task Complete_returns_400_for_a_negative_actual_cost()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        dryDock.Start();
        await SeedAsync(dryDock);

        using var response = await client.PostAsJsonAsync(
            ActionUri(dryDock.Id, "complete"),
            new { endDate = StartDate.AddDays(2), actualCost = -1m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stored = await SingleStoredAsync();

        Assert.Equal(DryDockStatus.InProgress, stored.Status);
        Assert.Null(stored.EndDate);
        Assert.Null(stored.ActualCost);
    }

    [Fact]
    public async Task Complete_returns_409_for_a_dry_dock_that_never_started()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        await SeedAsync(dryDock);

        using var response = await client.PostAsJsonAsync(
            ActionUri(dryDock.Id, "complete"),
            new { endDate = StartDate.AddDays(2), actualCost = 100m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var stored = await SingleStoredAsync();

        Assert.Equal(DryDockStatus.Planned, stored.Status);
        Assert.Null(stored.EndDate);
        Assert.Null(stored.ActualCost);
    }

    [Fact]
    public async Task Cancel_returns_200_and_keeps_the_planned_record()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Abandoned work");
        await SeedAsync(dryDock);

        using var response = await client.PostAsync(
            ActionUri(dryDock.Id, "cancel"),
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await SingleStoredAsync();

        Assert.Equal(DryDockStatus.Canceled, stored.Status);
        Assert.Equal(StartDate, stored.StartDate);
        Assert.Equal("Abandoned work", stored.PlannedTasks);
    }

    [Fact]
    public async Task Cancel_returns_404_for_an_unknown_dry_dock()
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
    public async Task Every_route_rejects_an_anonymous_client()
    {
        using var client = _application.CreateClient();
        var dryDockId = Guid.NewGuid();

        using var list = await client.GetAsync(DryDocksUri, CancellationToken.None);
        using var details = await client.GetAsync(DetailsUri(dryDockId), CancellationToken.None);
        using var start = await client.PostAsync(ActionUri(dryDockId, "start"), null, CancellationToken.None);
        using var cancel = await client.PostAsync(ActionUri(dryDockId, "cancel"), null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, details.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, start.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancel.StatusCode);
    }

    [Fact]
    public async Task Reading_is_forbidden_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var dryDockId = Guid.NewGuid();

        using var list = await client.GetAsync(DryDocksUri, CancellationToken.None);
        using var details = await client.GetAsync(DetailsUri(dryDockId), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, details.StatusCode);
    }

    [Fact]
    public async Task Writing_is_forbidden_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var dryDock = DryDock.Schedule(Guid.NewGuid(), Guid.NewGuid(), StartDate, "Work");
        await SeedAsync(dryDock);

        using var schedule = await client.PostAsJsonAsync(
            DryDocksUri,
            new { vesselId = Guid.NewGuid(), portId = Guid.NewGuid(), startDate = StartDate, plannedTasks = "Work" },
            CancellationToken.None);
        using var start = await client.PostAsync(ActionUri(dryDock.Id, "start"), null, CancellationToken.None);
        using var cancel = await client.PostAsync(ActionUri(dryDock.Id, "cancel"), null, CancellationToken.None);
        using var complete = await client.PostAsJsonAsync(
            ActionUri(dryDock.Id, "complete"),
            new { endDate = StartDate, actualCost = 1m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, schedule.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);
        Assert.Equal(DryDockStatus.Planned, (await SingleStoredAsync()).Status);
    }

    [Fact]
    public async Task Reading_is_allowed_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(DryDocksUri, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Uri DetailsUri(Guid dryDockId)
    {
        return new Uri($"/api/maintenance/dry-docks/{dryDockId}", UriKind.Relative);
    }

    private static Uri ActionUri(Guid dryDockId, string action)
    {
        return new Uri($"/api/maintenance/dry-docks/{dryDockId}/{action}", UriKind.Relative);
    }

    private async Task SeedAsync(params DryDock[] dryDocks)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<DryDock>().AddRangeAsync(dryDocks, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<DryDock> SingleStoredAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<DryDock>().AsNoTracking().SingleAsync(CancellationToken.None);
    }
}
