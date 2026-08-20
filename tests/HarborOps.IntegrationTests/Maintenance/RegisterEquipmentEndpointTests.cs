using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class RegisterEquipmentEndpointTests : IntegrationTestBase
{
    private static readonly Uri EquipmentUri = new("/api/maintenance/equipment", UriKind.Relative);

    private readonly HarborOpsApiFactory _application;

    public RegisterEquipmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_201_and_persists_the_equipment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var vesselId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            EquipmentUri,
            new
            {
                vesselId,
                name = "Main engine",
                category = "Propulsion",
                manufacturer = "Wartsila",
                serialNumber = "SN-1001",
            },
            CancellationToken.None);

        var created = await response.Content.ReadFromJsonAsync<RegisterEquipmentResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var stored = await dbContext.Set<Equipment>().SingleAsync(CancellationToken.None);

        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(vesselId, stored.VesselId);
        Assert.Equal("Main engine", stored.Name);
    }

    [Fact]
    public async Task Post_returns_400_for_a_missing_vessel_and_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsJsonAsync(
            EquipmentUri,
            new { category = "Propulsion" },
            CancellationToken.None);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterEquipmentValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterEquipmentValidator.NameField, problem.Errors);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            EquipmentUri,
            new { vesselId = Guid.NewGuid(), name = "Main engine", category = "Propulsion" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_only_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.PostAsJsonAsync(
            EquipmentUri,
            new { vesselId = Guid.NewGuid(), name = "Main engine", category = "Propulsion" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
