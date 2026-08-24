using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
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
    public async Task Get_returns_positions_ordered_by_rank_name()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Second Officer");
        await SeedCrewPositionAsync("Bosun");

        using var response = await client.GetAsync(
            new Uri("/api/crew/positions", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCrewPositionsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Bosun", result.Items[0].RankName);
        Assert.Equal("Second Officer", result.Items[1].RankName);
    }

    [Fact]
    public async Task Get_filters_by_search_query_parameter()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedCrewPositionAsync("Bosun");
        await SeedCrewPositionAsync("Chief Engineer");

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

    private async Task SeedCrewPositionAsync(string rankName)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();
    }
}
