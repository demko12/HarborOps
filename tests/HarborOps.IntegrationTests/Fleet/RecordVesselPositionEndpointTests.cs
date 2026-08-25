using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class RecordVesselPositionEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RecordVesselPositionEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_the_position_and_persists_it()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var beforeUtc = DateTime.UtcNow.AddSeconds(-1);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/position-reports",
            new RecordVesselPositionRequest { Latitude = 35.898765m, Longitude = -5.123456m },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordVesselPositionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(vessel.Id, result.VesselId);
        Assert.Equal(35.898765m, result.Latitude);
        Assert.Equal(-5.123456m, result.Longitude);
        Assert.InRange(result.ReportedAtUtc, beforeUtc, DateTime.UtcNow.AddSeconds(1));

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(35.898765m, persisted.CurrentLatitude);
        Assert.Equal(-5.123456m, persisted.CurrentLongitude);
        Assert.Equal(result.ReportedAtUtc, persisted.LastPositionReportedAtUtc);
    }

    [Fact]
    public async Task Recorded_position_is_visible_on_the_positions_endpoint_immediately()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Manage,
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var postResponse = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/position-reports",
            new RecordVesselPositionRequest { Latitude = 12.5m, Longitude = 42.25m },
            CancellationToken.None);
        postResponse.EnsureSuccessStatusCode();

        using var getResponse = await client.GetAsync(
            "/api/fleet/vessel-positions",
            CancellationToken.None);
        var result = await getResponse.Content.ReadFromJsonAsync<VesselPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(result);

        var item = Assert.Single(result.Items);

        Assert.Equal(12.5m, item.CurrentLatitude);
        Assert.Equal(42.25m, item.CurrentLongitude);
        Assert.NotNull(item.LastPositionReportedAtUtc);
        Assert.False(item.IsPositionStale);
    }

    [Theory]
    [InlineData(91, 0, "latitude")]
    [InlineData(-91, 0, "latitude")]
    [InlineData(0, 181, "longitude")]
    [InlineData(0, -181, "longitude")]
    public async Task Post_returns_validation_problem_for_invalid_coordinates(
        double latitude,
        double longitude,
        string expectedField)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/position-reports",
            new RecordVesselPositionRequest
            {
                Latitude = (decimal)latitude,
                Longitude = (decimal)longitude,
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.CurrentLatitude);
        Assert.Null(persisted.LastPositionReportedAtUtc);
    }

    [Fact]
    public async Task Post_accepts_boundary_coordinates()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/position-reports",
            new RecordVesselPositionRequest { Latitude = -90m, Longitude = 180m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/position-reports",
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/position-reports",
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/position-reports",
            new RecordVesselPositionRequest { Latitude = 10m, Longitude = 20m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.CurrentLatitude);
        Assert.Null(persisted.CurrentLongitude);
        Assert.Null(persisted.LastPositionReportedAtUtc);
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

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            "North Star",
            "1000001",
            "Malta",
            "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
