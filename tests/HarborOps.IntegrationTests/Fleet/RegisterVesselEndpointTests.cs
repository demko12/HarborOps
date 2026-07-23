using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Fleet;

public sealed class RegisterVesselEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterVesselEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_vessel()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

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
        Assert.Equal(request.VesselType, vessel.VesselType);
        Assert.Equal("Vessels", entityType.GetTableName());
        Assert.Equal(FleetModule.Schema, entityType.GetSchema());
        Assert.True(imoNumberIndex.IsUnique);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new RegisterVesselRequest
        {
            Name = string.Empty,
            ImoNumber = "not-an-imo",
            FlagState = "Malta",
            VesselType = "Container Ship",
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
    }

    private static RegisterVesselRequest CreateValidRequest()
    {
        return new RegisterVesselRequest
        {
            Name = "North Star",
            ImoNumber = "1234567",
            FlagState = "Malta",
            VesselType = "Container Ship",
        };
    }
}
