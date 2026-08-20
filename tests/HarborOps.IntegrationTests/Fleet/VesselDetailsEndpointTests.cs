using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class VesselDetailsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VesselDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_vessel_details_with_history_ordered_descending()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "North Star", "1234567");
        var fleet = await SeedFleetAsync("Atlantic Fleet");
        await SeedActiveAssignmentAsync(vessel.Id, fleet.Id);

        using var first = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "En Route" },
            CancellationToken.None);
        first.EnsureSuccessStatusCode();

        using var second = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "At Port", Notes = "Berthed" },
            CancellationToken.None);
        second.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            $"/api/fleet/vessels/{vessel.Id}",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VesselDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(vessel.Id, result.VesselId);
        Assert.Equal("North Star", result.Name);
        Assert.Equal("At Port", result.CurrentStatus);
        Assert.Equal(fleet.Id, result.FleetId);
        Assert.Equal("Atlantic Fleet", result.FleetName);
        Assert.Equal(2, result.StatusHistory.Count);
        Assert.Equal("At Port", result.StatusHistory[0].Status);
        Assert.Equal("Berthed", result.StatusHistory[0].Notes);
        Assert.Equal("En Route", result.StatusHistory[1].Status);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_succeeds_for_read_only_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "North Star", "1234567");

        using var response = await client.GetAsync(
            $"/api/fleet/vessels/{vessel.Id}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "North Star", "1234567");

        using var response = await client.GetAsync(
            $"/api/fleet/vessels/{vessel.Id}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId, string name, string imoNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, name, imoNumber, "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private async Task<FleetEntity> SeedFleetAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var fleet = FleetEntity.Register(name, "Fleet used by details endpoint tests.");

        await dbContext.Set<FleetEntity>().AddAsync(fleet);
        await dbContext.SaveChangesAsync();

        return fleet;
    }

    private async Task SeedActiveAssignmentAsync(Guid vesselId, Guid fleetId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var assignment = FleetAssignment.Register(vesselId, fleetId);

        await dbContext.Set<FleetAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
    }
}
