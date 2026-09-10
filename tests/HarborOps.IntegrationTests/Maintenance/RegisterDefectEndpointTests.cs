using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class RegisterDefectEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterDefectEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_for_a_valid_vessel_with_no_equipment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var request = CreateValidRequest(vessel.Id);

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/maintenance/defects/{result!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Reported", result.Status);
        Assert.Null(result.EquipmentId);

        var stored = await StoredDefectAsync(result.Id);
        Assert.Equal(vessel.Id, stored.VesselId);
        Assert.Equal(DefectStatus.Reported, stored.Status);
    }

    [Fact]
    public async Task Post_returns_201_for_valid_vessel_and_equipment_on_the_same_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var equipment = await SeedEquipmentAsync(vessel.Id);
        var request = CreateValidRequest(vessel.Id) with { EquipmentId = equipment.Id };

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DefectDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(equipment.Id, result!.EquipmentId);

        var stored = await StoredDefectAsync(result.Id);
        Assert.Equal(equipment.Id, stored.EquipmentId);
    }

    [Fact]
    public async Task Post_returns_400_for_a_missing_title()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var request = CreateValidRequest(vessel.Id) with { Title = null };

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDefectValidator.TitleField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_400_when_equipment_belongs_to_a_different_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var otherVessel = await SeedVesselAsync(vesselTypeId, "Other Vessel", "2000002");
        var foreignEquipment = await SeedEquipmentAsync(otherVessel.Id);
        var request = CreateValidRequest(vessel.Id) with { EquipmentId = foreignEquipment.Id };

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDefectValidator.EquipmentIdField, problem.Errors);
        Assert.Empty(await AllDefectsAsync());
    }

    [Fact]
    public async Task Post_returns_403_without_the_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Read);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var request = CreateValidRequest(vessel.Id);

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await AllDefectsAsync());
    }

    [Fact]
    public async Task Post_returns_404_when_the_vessel_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var unknownVesselId = Guid.NewGuid();
        var request = CreateValidRequest(unknownVesselId);

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AllDefectsAsync());
    }

    [Fact]
    public async Task Post_returns_404_when_the_equipment_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Maintenance.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var vessel = await SeedVesselAsync(vesselTypeId);
        var request = CreateValidRequest(vessel.Id) with { EquipmentId = Guid.NewGuid() };

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AllDefectsAsync());
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest(Guid.NewGuid());

        using var response = await client.PostAsJsonAsync("/api/maintenance/defects", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegisterDefectRequest CreateValidRequest(Guid vesselId)
    {
        return new RegisterDefectRequest
        {
            VesselId = vesselId,
            EquipmentId = null,
            Title = "Hydraulic leak on crane boom",
            Description = "Visible oil leak near the boom cylinder",
            ReportedSeverity = "Medium",
            ObservedAtUtc = DateTime.UtcNow.AddHours(-1),
        };
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

    private async Task<Vessel> SeedVesselAsync(Guid vesselTypeId, string name = "North Star", string imoNumber = "1000001")
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = Vessel.Register(Guid.NewGuid(), vesselTypeId, name, imoNumber, "Malta", "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private async Task<Equipment> SeedEquipmentAsync(Guid vesselId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var equipment = Equipment.Register(vesselId, "Deck crane", "Cargo handling", "Liebherr", null);

        await dbContext.Set<Equipment>().AddAsync(equipment);
        await dbContext.SaveChangesAsync();

        return equipment;
    }

    private async Task<VesselDefect> StoredDefectAsync(Guid defectId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<VesselDefect>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == defectId, CancellationToken.None);
    }

    private async Task<List<VesselDefect>> AllDefectsAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<VesselDefect>().AsNoTracking().ToListAsync(CancellationToken.None);
    }
}
