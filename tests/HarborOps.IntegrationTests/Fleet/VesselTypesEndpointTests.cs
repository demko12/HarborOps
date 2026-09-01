using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class VesselTypesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VesselTypesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_registered_vessel_types_for_a_reader()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        await SeedVesselTypeAsync("Container Ship", "CON");
        await SeedVesselTypeAsync("Bulk Carrier", "BULK");

        using var response = await client.GetAsync("/api/fleet/vessel-types", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselTypesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Contains(result.VesselTypes, type => type.Name == "Container Ship" && type.Code == "CON");
        Assert.Contains(result.VesselTypes, type => type.Name == "Bulk Carrier");
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/fleet/vessel-types", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_a_fleet_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync("/api/fleet/vessel-types", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedVesselTypeAsync(string name, string code)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register(name, code, $"{name} vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();
    }
}
