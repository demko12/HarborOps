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

public sealed class UpdateVesselSpecificationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateVesselSpecificationsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_stores_the_technical_profile()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/specifications",
            new UpdateVesselSpecificationsRequest
            {
                Dwt = 52000m,
                Gt = 29000m,
                Loa = 190m,
                Beam = 32m,
                Draft = 12m,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(52000m, result.Dwt);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal(29000m, persisted.Gt);
        Assert.Equal(12m, persisted.Draft);
    }

    [Fact]
    public async Task Put_returns_validation_problem_for_non_positive_measurements()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/specifications",
            new UpdateVesselSpecificationsRequest { Dwt = -1m },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("dwt", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.Dwt);
    }

    [Fact]
    public async Task Put_returns_not_found_for_unknown_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/specifications",
            new UpdateVesselSpecificationsRequest { Dwt = 52000m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/specifications",
            new UpdateVesselSpecificationsRequest { Dwt = 52000m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/specifications",
            new UpdateVesselSpecificationsRequest { Dwt = 52000m },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Null(persisted.Dwt);
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
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, "North Star", "1234567", "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
