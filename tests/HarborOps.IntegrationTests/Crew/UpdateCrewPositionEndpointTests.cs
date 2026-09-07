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

public sealed class UpdateCrewPositionEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateCrewPositionEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_updates_and_persists_the_position()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var positionId = await SeedPositionAsync("Bosun", CrewPositionDepartments.Deck);
        var request = CreateValidRequest() with { RankName = "Chief Bosun", Department = CrewPositionDepartments.Engine };

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{positionId}", request);
        var result = await response.Content.ReadFromJsonAsync<UpdateCrewPositionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(positionId, result.Id);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var updated = await dbContext.Set<CrewPosition>().SingleAsync(p => p.Id == positionId);

        Assert.Equal("Chief Bosun", updated.RankName);
        Assert.Equal(CrewPositionDepartments.Engine, updated.Department);
    }

    [Fact]
    public async Task Put_returns_validation_problem_for_an_unsupported_seniority_level()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var positionId = await SeedPositionAsync("Bosun", CrewPositionDepartments.Deck);
        var request = CreateValidRequest() with { SeniorityLevel = "Master" };

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{positionId}", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CrewPositionFieldValidation.SeniorityLevelField, problem.Errors);
    }

    [Fact]
    public async Task Put_returns_not_found_for_an_unknown_position()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = CreateValidRequest();

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{Guid.NewGuid()}", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Put_returns_conflict_for_a_duplicate_rank_and_department_from_another_position()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        await SeedPositionAsync("Chief Engineer", CrewPositionDepartments.Engine);
        var positionId = await SeedPositionAsync("Bosun", CrewPositionDepartments.Deck);
        var request = CreateValidRequest() with { RankName = "Chief Engineer", Department = CrewPositionDepartments.Engine };

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{positionId}", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{Guid.NewGuid()}", CreateValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_with_read_only_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var positionId = await SeedPositionAsync("Bosun", CrewPositionDepartments.Deck);

        using var response = await client.PutAsJsonAsync($"/api/crew/positions/{positionId}", CreateValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static UpdateCrewPositionRequest CreateValidRequest()
    {
        return new UpdateCrewPositionRequest
        {
            RankName = "Bosun",
            Department = CrewPositionDepartments.Deck,
            SeniorityLevel = CrewPositionSeniorityLevels.SeniorRating,
        };
    }

    private async Task<Guid> SeedPositionAsync(string rankName, string department)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }
}
