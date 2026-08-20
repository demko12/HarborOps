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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class RecordVesselStatusChangeEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RecordVesselStatusChangeEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_status_change_and_persists_atomically()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "Active");

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "En Route", Notes = "Departed port" },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordVesselStatusChangeResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(vessel.Id, result.VesselId);
        Assert.Equal("En Route", result.Status);
        Assert.Equal("Departed port", result.Notes);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var updatedVessel = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);
        var historyRows = await dbContext.Set<VesselStatusHistory>()
            .Where(entry => entry.VesselId == vessel.Id)
            .ToListAsync();

        Assert.Equal("En Route", updatedVessel.CurrentStatus);
        var history = Assert.Single(historyRows);
        Assert.Equal("En Route", history.Status);
        Assert.Equal("Departed port", history.Notes);
        Assert.False(string.IsNullOrWhiteSpace(history.ChangedBy));
        Assert.EndsWith("@harborops.test", history.ChangedBy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(history.ChangedBy, result.ChangedBy);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "Active");

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "Sinking" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("status", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "Active" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_conflict_when_status_is_unchanged()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "Active");

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "Active" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.Equal(0, await dbContext.Set<VesselStatusHistory>().CountAsync());
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{Guid.NewGuid()}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "Active" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId, "Active");

        using var response = await client.PostAsJsonAsync(
            $"/api/fleet/vessels/{vessel.Id}/status-changes",
            new RecordVesselStatusChangeRequest { Status = "En Route", Notes = "Departed port" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var persisted = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);

        Assert.Equal("Active", persisted.CurrentStatus);
        Assert.Equal(0, await dbContext.Set<VesselStatusHistory>().CountAsync());
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

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId, string currentStatus)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            "North Star",
            "1234567",
            "Malta",
            currentStatus);

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }
}
