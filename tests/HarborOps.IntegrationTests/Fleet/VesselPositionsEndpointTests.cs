using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class VesselPositionsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VesselPositionsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_positions_with_the_stale_threshold()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(
            vesselTypeId,
            "North Star",
            "1000001",
            position: (35.898765m, -5.123456m),
            reportedAtUtc: DateTime.UtcNow.AddHours(-1));

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(VesselPositionsQuery.StaleAfterHours, result.StaleAfterHours);

        var item = Assert.Single(result.Items);

        Assert.Equal(vessel.Id, item.VesselId);
        Assert.Equal("North Star", item.Name);
        Assert.Equal("1000001", item.ImoNumber);
        Assert.Equal("Active", item.CurrentStatus);
        Assert.Equal(35.898765m, item.CurrentLatitude);
        Assert.Equal(-5.123456m, item.CurrentLongitude);
        Assert.NotNull(item.LastPositionReportedAtUtc);
        Assert.False(item.IsPositionStale);
    }

    [Fact]
    public async Task Get_marks_missing_and_stale_positions()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        await SeedVesselAsync(vesselTypeId, "Missing", "1000001");
        await SeedVesselAsync(
            vesselTypeId,
            "Stale",
            "1000002",
            position: (10m, 20m),
            reportedAtUtc: DateTime.UtcNow.AddDays(-5));
        await SeedVesselAsync(
            vesselTypeId,
            "Fresh",
            "1000003",
            position: (11m, 21m),
            reportedAtUtc: DateTime.UtcNow.AddMinutes(-5));

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);

        // Stale and missing rows come first, ordered by name.
        Assert.Equal(["Missing", "Stale", "Fresh"], result.Items.Select(item => item.Name));

        var missing = result.Items[0];
        Assert.Null(missing.CurrentLatitude);
        Assert.Null(missing.LastPositionReportedAtUtc);
        Assert.True(missing.IsPositionStale);
        Assert.True(result.Items[1].IsPositionStale);
        Assert.False(result.Items[2].IsPositionStale);
    }

    [Fact]
    public async Task Get_filters_by_fleet_and_status_and_staleness()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var fleetId = await SeedFleetAsync("Atlantic Fleet");

        var staleInFleet = await SeedVesselAsync(vesselTypeId, "Stale At Port", "1000001", "At Port");
        var freshInFleet = await SeedVesselAsync(
            vesselTypeId,
            "Fresh At Port",
            "1000002",
            "At Port",
            position: (10m, 20m),
            reportedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        var otherStatusInFleet = await SeedVesselAsync(vesselTypeId, "En Route Ship", "1000003", "En Route");
        await SeedVesselAsync(vesselTypeId, "Unassigned", "1000004", "At Port");

        await SeedActiveAssignmentAsync(staleInFleet.Id, fleetId);
        await SeedActiveAssignmentAsync(freshInFleet.Id, fleetId);
        await SeedActiveAssignmentAsync(otherStatusInFleet.Id, fleetId);

        using var response = await client.GetAsync(
            $"/api/fleet/vessel-positions?fleetId={fleetId}&status=At%20Port&staleOnly=true",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);

        var item = Assert.Single(result.Items);

        Assert.Equal(staleInFleet.Id, item.VesselId);
        Assert.Equal(fleetId, item.FleetId);
        Assert.Equal("Atlantic Fleet", item.FleetName);
        Assert.True(item.IsPositionStale);
    }

    [Fact]
    public async Task Get_honours_paging()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        await SeedVesselAsync(vesselTypeId, "Vessel 01", "1000001");
        await SeedVesselAsync(vesselTypeId, "Vessel 02", "1000002");
        await SeedVesselAsync(vesselTypeId, "Vessel 03", "1000003");

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions?pageNumber=2&pageSize=1",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal("Vessel 02", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task Get_returns_an_empty_page_when_no_vessels_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_an_invalid_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            $"/api/fleet/vessel-positions?pageSize={VesselPositionsValidator.MaxPageSize + 1}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("pageSize", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_an_unknown_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions?status=Sinking",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("status", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Crew.Read);

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_allowed_for_a_read_only_operator()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SeedVesselTypeAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private async Task<Vessel> SeedVesselAsync(
        Guid vesselTypeId,
        string name,
        string imoNumber,
        string currentStatus = "Active",
        (decimal Latitude, decimal Longitude)? position = null,
        DateTime? reportedAtUtc = null)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            name,
            imoNumber,
            "Malta",
            currentStatus);

        if (position is { } coordinates)
        {
            vessel.ReportPosition(
                coordinates.Latitude,
                coordinates.Longitude,
                reportedAtUtc ?? DateTime.UtcNow);
        }

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private async Task<Guid> SeedFleetAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var fleet = FleetEntity.Register(name, "Fleet used by vessel position endpoint tests.");

        await dbContext.Set<FleetEntity>().AddAsync(fleet);
        await dbContext.SaveChangesAsync();

        return fleet.Id;
    }

    private async Task SeedActiveAssignmentAsync(Guid vesselId, Guid fleetId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<FleetAssignment>().AddAsync(FleetAssignment.Register(vesselId, fleetId));
        await dbContext.SaveChangesAsync();
    }
}
