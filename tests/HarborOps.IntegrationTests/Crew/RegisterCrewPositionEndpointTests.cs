using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterCrewPositionEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterCrewPositionEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_creates_and_persists_a_position()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync("/api/crew/positions", request);
        var result = await response.Content.ReadFromJsonAsync<RegisterCrewPositionResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == result.Id);

        Assert.Equal("Bosun", saved.RankName);
        Assert.Equal(CrewPositionDepartments.Deck, saved.Department);
        Assert.Equal(CrewPositionSeniorityLevels.SeniorRating, saved.SeniorityLevel);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_an_unsupported_department()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = CreateValidRequest() with { Department = "Bridge" };

        using var response = await client.PostAsJsonAsync("/api/crew/positions", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CrewPositionFieldValidation.DepartmentField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_a_duplicate_rank_and_department()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        await SeedPositionAsync("Bosun", CrewPositionDepartments.Deck);
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync("/api/crew/positions", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/crew/positions", CreateValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_read_only_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PostAsJsonAsync("/api/crew/positions", CreateValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static RegisterCrewPositionRequest CreateValidRequest()
    {
        return new RegisterCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }

    private async Task SeedPositionAsync(string rankName, string department)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();
    }
}
