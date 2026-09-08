using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class SearchCrewPositionsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public SearchCrewPositionsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_positions_ordered_by_department_then_rank_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Second Officer", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Chief Engineer", CrewPositionDepartments.Engine);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Bosun", result.Items[0].RankName);
        Assert.Equal("Second Officer", result.Items[1].RankName);
        Assert.Equal("Chief Engineer", result.Items[2].RankName);
    }

    [Fact]
    public async Task Get_filters_by_search_query_parameter()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Chief Engineer", CrewPositionDepartments.Engine);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions?search=Bos", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal("Bosun", item.RankName);
    }

    [Fact]
    public async Task Get_filters_by_department_query_parameter()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Chief Engineer", CrewPositionDepartments.Engine);

        using var response = await client.GetAsync(
            new Uri($"/api/crew/positions?department={CrewPositionDepartments.Engine}", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal("Chief Engineer", item.RankName);
    }

    [Fact]
    public async Task Get_paginates_using_page_number_and_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Second Officer", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync("Chief Engineer", CrewPositionDepartments.Engine);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions?pageNumber=2&pageSize=2", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task Get_returns_crew_member_and_assignment_usage_counts()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var positionId = await SeedCrewPositionAsync("Bosun", CrewPositionDepartments.Deck);
        await SeedCrewMemberAsync(positionId);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.CrewMemberUsageCount);
        Assert.Equal(0, item.AssignmentUsageCount);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions?pageSize=101", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("pageSize", problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_crew_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedCrewPositionAsync(string rankName, string department)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private async Task SeedCrewMemberAsync(Guid rankId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var member = CrewMember.Create(
            rankId,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10],
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
    }
}
