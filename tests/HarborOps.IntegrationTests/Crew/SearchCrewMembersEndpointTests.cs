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

public sealed class SearchCrewMembersEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public SearchCrewMembersEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_returns_paged_roster_with_rank_and_filters()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        await SeedMemberAsync("Zara Deck", "IMO700001", "Active", new DateOnly(2028, 1, 1));
        await SeedMemberAsync("Ana Engine", "IMO700002", "Inactive", new DateOnly(2024, 1, 1));

        using var response = await client.GetAsync("/api/crew/members?search=Ana&employmentStatus=Inactive&pageNumber=1&pageSize=25");
        var page = await response.Content.ReadFromJsonAsync<SearchCrewMembersResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = Assert.Single(page!.Items);
        Assert.Equal("Ana Engine", member.Name);
        Assert.Equal("Chief Officer", member.BaseRankName);
        Assert.Equal("Inactive", member.EmploymentStatus);
        Assert.Equal(new DateOnly(2024, 1, 1), member.PassportExpiry);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_invalid_paging()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        using var response = await client.GetAsync("/api/crew/members?pageSize=101");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(SearchCrewMembersValidator.PageSizeField, problem!.Errors);
    }

    private async Task SeedMemberAsync(string name, string imoSeafarerId, string status, DateOnly passportExpiry)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Chief Officer", "Deck", "Senior");
        var member = CrewMember.Create(position.Id, name, new DateOnly(1990, 1, 1), "Croatia", imoSeafarerId, "P12345", passportExpiry, "CDC12345", new DateOnly(2020, 1, 1), status);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
    }
}
