using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class RegisterVesselEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterVesselEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var vesselTypeId = await SeedVesselTypeAsync();
        var request = CreateValidRequest("1234567", vesselTypeId);

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterVesselResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal($"/api/fleet/vessels/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vessel = await dbContext
            .Set<Vessel>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(Vessel));
        var imoNumberIndex = entityType!
            .GetIndexes()
            .Single(index => index.Properties.Single().Name == nameof(Vessel.ImoNumber));

        Assert.Equal(request.Name, vessel.Name);
        Assert.Equal(request.ImoNumber, vessel.ImoNumber);
        Assert.Equal(request.FlagState, vessel.FlagState);
        Assert.Equal(request.VesselTypeId, vessel.VesselTypeId);
        Assert.Equal(request.CompanyId, vessel.CompanyId);
        Assert.Equal(request.CurrentStatus, vessel.CurrentStatus);
        Assert.Equal("Vessels", entityType.GetTableName());
        Assert.Equal(FleetModule.Schema, entityType.GetSchema());
        Assert.True(imoNumberIndex.IsUnique);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var request = new RegisterVesselRequest
        {
            CompanyId = null,
            VesselTypeId = null,
            Name = string.Empty,
            ImoNumber = "not-an-imo",
            FlagState = "Malta",
            CurrentStatus = "Active",
        };

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterVesselValidator.NameField, problem.Errors);
        Assert.Contains(RegisterVesselValidator.ImoNumberField, problem.Errors);
        Assert.Contains(RegisterVesselValidator.CompanyIdField, problem.Errors);
        Assert.Contains(RegisterVesselValidator.VesselTypeIdField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_problem_for_unknown_vessel_type()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);
        var request = CreateValidRequest("7654321", Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("1112223", Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_authentication_but_no_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest("2223334", Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/vessels",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_search_only_requires_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync("/api/fleet/vessels", CancellationToken.None);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_search_succeeds_for_manage_permission_too()
    {
        // manage implies read for the same capability
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Manage);

        using var response = await client.GetAsync("/api/fleet/vessels", CancellationToken.None);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegisterVesselRequest CreateValidRequest(string imoNumber, Guid vesselTypeId)
    {
        return new RegisterVesselRequest
        {
            CompanyId = Guid.NewGuid(),
            VesselTypeId = vesselTypeId,
            Name = "North Star",
            ImoNumber = imoNumber,
            FlagState = "Malta",
            CurrentStatus = "Active",
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
}
