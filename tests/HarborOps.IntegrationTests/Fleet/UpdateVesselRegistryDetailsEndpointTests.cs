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

public sealed class UpdateVesselRegistryDetailsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateVesselRegistryDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_updates_mutable_registry_fields_and_keeps_imo_and_company()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync("Container Ship", "CON");
        var replacementTypeId = await SeedVesselTypeAsync("Chemical Tanker", "TANK");
        var vessel = await SeedVesselAsync(vesselTypeId, "9111111");

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = replacementTypeId,
                Name = "Northern Star",
                Mmsi = "256123000",
                CallSign = "9HA1234",
                FlagState = "Montenegro",
                PortOfRegistry = "Bar",
                ClassificationSociety = "Lloyd's Register",
                LifecycleStage = "In service",
                MsmdId = "MSMD-42",
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("Northern Star", result.Name);
        Assert.Equal(replacementTypeId, result.VesselTypeId);
        Assert.Equal("MSMD-42", result.MsmdId);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal("Northern Star", persisted.Name);
        Assert.Equal("256123000", persisted.Mmsi);
        Assert.Equal("9HA1234", persisted.CallSign);
        Assert.Equal("9111111", persisted.ImoNumber);
        Assert.Equal(vessel.CompanyId, persisted.CompanyId);
    }

    [Fact]
    public async Task Put_returns_validation_problem_for_invalid_identifiers()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = vesselTypeId,
                Name = "North Star",
                FlagState = "Malta",
                Mmsi = "25X",
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("mmsi", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_returns_not_found_for_unknown_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = vesselTypeId,
                Name = "North Star",
                FlagState = "Malta",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_not_found_for_unknown_vessel_type()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = Guid.NewGuid(),
                Name = "North Star",
                FlagState = "Malta",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_conflict_and_does_not_update_when_mmsi_belongs_to_another_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var edited = await SeedVesselAsync(vesselTypeId, "1000001");
        var other = await SeedVesselAsync(vesselTypeId, "1000002");

        using var seedOther = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{other.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = vesselTypeId,
                Name = other.Name,
                FlagState = other.FlagState,
                Mmsi = "256123000",
            },
            CancellationToken.None);
        seedOther.EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{edited.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = vesselTypeId,
                Name = "Renamed",
                FlagState = edited.FlagState,
                Mmsi = "256123000",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == edited.Id);

        Assert.Equal("North Star", persisted.Name);
        Assert.Null(persisted.Mmsi);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/registry-details",
            new UpdateVesselRegistryDetailsRequest { VesselTypeId = Guid.NewGuid(), Name = "North Star", FlagState = "Malta" },
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
            $"/api/fleet/vessels/{vessel.Id}/registry-details",
            new UpdateVesselRegistryDetailsRequest
            {
                VesselTypeId = vesselTypeId,
                Name = "Renamed",
                FlagState = "Malta",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal("North Star", persisted.Name);
    }

    private async Task<Guid> SeedVesselTypeAsync(string name = "Container Ship", string code = "CNTR")
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register(name, code, $"{name} vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId, string imoNumber = "1234567")
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, "North Star", imoNumber, "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
