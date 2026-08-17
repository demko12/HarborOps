using System.Net;
using System.Net.Http.Json;
using HarborOps.Api.Composition.Operations.FleetVessel;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.IntegrationTests.Operations;

public sealed class FleetVesselOverviewEndpointTests : IntegrationTestBase
{
    private static readonly DateTime OverdueUtc = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UpcomingUtc = new(2099, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public FleetVesselOverviewEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_composed_vessel_rows()
    {
        var fleetId = await SeedFleetAsync("Atlantic Fleet");
        var aurora = await SeedVesselAsync(fleetId, "Aurora", "2000001");
        await SeedVesselAsync(fleetId, "Borealis", "2000002");

        await SeedWorkOrderAsync(aurora, WorkOrderStatus.Open, WorkOrderPriority.Critical, OverdueUtc);
        await SeedWorkOrderAsync(aurora, WorkOrderStatus.InProgress, WorkOrderPriority.Low, UpcomingUtc);
        await SeedWorkOrderAsync(aurora, WorkOrderStatus.Completed, WorkOrderPriority.High, OverdueUtc);

        using var client = await CreateOverviewClientAsync();
        using var response = await client.GetAsync(
            $"/api/operations/fleets/{fleetId}/vessels",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<FleetVesselOverviewPage>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["Aurora", "Borealis"], page.Items.Select(item => item.Name));

        var auroraRow = page.Items[0];
        Assert.Equal("Bulk Carrier", auroraRow.VesselTypeName);
        Assert.Equal("Atlantic Fleet", auroraRow.FleetName);
        Assert.Equal(2, auroraRow.OpenWorkOrderCount);
        Assert.Equal(1, auroraRow.OverdueWorkOrderCount);
        Assert.Equal(1, auroraRow.CriticalOpenWorkOrderCount);
        Assert.Equal(UpcomingUtc, auroraRow.NextWorkOrderDueUtc);

        var borealisRow = page.Items[1];
        Assert.Equal(0, borealisRow.OpenWorkOrderCount);
        Assert.Equal(0, borealisRow.OverdueWorkOrderCount);
        Assert.Equal(0, borealisRow.CriticalOpenWorkOrderCount);
        Assert.Null(borealisRow.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task Get_applies_paging_search_and_fleet_filtering()
    {
        var fleetId = await SeedFleetAsync("Atlantic Fleet");
        var otherFleetId = await SeedFleetAsync("Pacific Fleet");
        await SeedVesselAsync(fleetId, "Coral Queen", "2100001");
        await SeedVesselAsync(fleetId, "Coral Star", "2100002");
        await SeedVesselAsync(fleetId, "Nordic Wind", "2100003");
        await SeedVesselAsync(otherFleetId, "Coral Ghost", "2100004");

        using var client = await CreateOverviewClientAsync();
        using var response = await client.GetAsync(
            $"/api/operations/fleets/{fleetId}/vessels?pageNumber=2&pageSize=1&search=Coral",
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<FleetVesselOverviewPage>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(1, page.PageSize);

        var vessel = Assert.Single(page.Items);
        Assert.Equal("Coral Star", vessel.Name);
    }

    [Fact]
    public async Task Get_returns_unauthorized_for_an_anonymous_request()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/operations/fleets/{Guid.NewGuid()}/vessels", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_maintenance_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            $"/api/operations/fleets/{Guid.NewGuid()}/vessels",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_fleet_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(
            $"/api/operations/fleets/{Guid.NewGuid()}/vessels",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_a_malformed_fleet_id()
    {
        using var client = await CreateOverviewClientAsync();

        using var response = await client.GetAsync(
            "/api/operations/fleets/not-a-guid/vessels",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(FleetVesselOverviewValidator.FleetIdField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_invalid_paging()
    {
        using var client = await CreateOverviewClientAsync();

        using var response = await client.GetAsync(
            $"/api/operations/fleets/{Guid.NewGuid()}/vessels?pageNumber=0&pageSize=0",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(FleetVesselOverviewValidator.PageNumberField, problem.Errors);
        Assert.Contains(FleetVesselOverviewValidator.PageSizeField, problem.Errors);
    }

    private Task<HttpClient> CreateOverviewClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read,
            Permissions.Maintenance.Read);
    }

    private async Task<Guid> SeedFleetAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var fleet = FleetEntity.Register(name, $"{name} description");

        dbContext.Add(fleet);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return fleet.Id;
    }

    private async Task<Guid> SeedVesselAsync(Guid fleetId, string name, string imoNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var vesselType = VesselType.Register("Bulk Carrier", "BC", "Bulk carrier");
        dbContext.Add(vesselType);

        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselType.Id,
            name,
            imoNumber,
            "Malta",
            "Active");

        dbContext.Add(vessel);
        dbContext.Add(FleetAssignment.Register(vessel.Id, fleetId));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return vessel.Id;
    }

    private async Task SeedWorkOrderAsync(
        Guid vesselId,
        WorkOrderStatus status,
        WorkOrderPriority priority,
        DateTime dueUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            vesselId,
            $"Work order {dueUtc:O}",
            null,
            priority,
            dueUtc);

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;
            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(dueUtc);
                break;
            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;
            default:
                break;
        }

        dbContext.Add(workOrder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
