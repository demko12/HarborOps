using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class VesselLookupEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VesselLookupEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_names_for_requested_vessels()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var vessel = await SeedVesselAsync("Aurora Borealis", "5200001");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels/lookup",
            new VesselLookupRequest { VesselIds = [vessel.Id] },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselLookupResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Vessels);
        Assert.Equal(vessel.Id, item.Id);
        Assert.Equal("Aurora Borealis", item.Name);
    }

    [Fact]
    public async Task Post_returns_empty_for_unknown_ids()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels/lookup",
            new VesselLookupRequest { VesselIds = [Guid.NewGuid()] },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselLookupResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Vessels);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_missing_vessel_ids()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels/lookup",
            new VesselLookupRequest(),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(VesselLookupValidator.VesselIdsField, problem.Errors);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels/lookup",
            new VesselLookupRequest { VesselIds = [Guid.NewGuid()] },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Vessel> SeedVesselAsync(string name, string imoNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        var vessel = Vessel.Register(Guid.NewGuid(), vesselType.Id, name, imoNumber, "Malta", "Active");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
