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
        await SeedMemberAsync("Ana Engine", "IMO700002", "OnLeave", new DateOnly(2024, 1, 1));

        using var response = await client.GetAsync("/api/crew/members?search=Ana&employmentStatus=OnLeave&pageNumber=1&pageSize=25");
        var page = await response.Content.ReadFromJsonAsync<SearchCrewMembersResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = Assert.Single(page!.Items);
        Assert.Equal("Ana Engine", member.Name);
        Assert.Equal("Chief Officer", member.BaseRankName);
        Assert.Equal("OnLeave", member.EmploymentStatus);
        Assert.Equal(new DateOnly(2024, 1, 1), member.PassportExpiry);
    }

    [Fact]
    public async Task Get_filters_roster_by_passport_expiry()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        await SeedMemberAsync("Passportexpired Crew", "IMO700101", "Active", today.AddDays(-1));
        await SeedMemberAsync("Passportsoon Crew", "IMO700102", "Active", today.AddDays(30));
        await SeedMemberAsync("Passportvalid Crew", "IMO700103", "Active", today.AddDays(400));

        var expired = await client.GetFromJsonAsync<SearchCrewMembersResponse>("/api/crew/members?search=Passport&passportExpiry=Expired");
        var expiringSoon = await client.GetFromJsonAsync<SearchCrewMembersResponse>("/api/crew/members?search=Passport&passportExpiry=ExpiringSoon");

        Assert.Equal("Passportexpired Crew", Assert.Single(expired!.Items).Name);
        Assert.Equal("Passportsoon Crew", Assert.Single(expiringSoon!.Items).Name);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_unknown_passport_expiry()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        using var response = await client.GetAsync("/api/crew/members?passportExpiry=Soon");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(SearchCrewMembersValidator.PassportExpiryField, problem!.Errors);
    }

    [Fact]
    public async Task Summary_counts_active_crew_and_passport_expiry_across_the_roster()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var today = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow);
        var before = await client.GetFromJsonAsync<CrewMembersSummaryResponse>("/api/crew/members/summary");
        await SeedMemberAsync("Summary Expired", "IMO700201", "Active", today.AddDays(-10));
        await SeedMemberAsync("Summary Soon", "IMO700202", "OnLeave", today.AddDays(60));
        await SeedMemberAsync("Summary Valid", "IMO700203", "Active", today.AddDays(500));

        using var response = await client.GetAsync("/api/crew/members/summary");
        var after = await response.Content.ReadFromJsonAsync<CrewMembersSummaryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.TotalCount + 3, after.TotalCount);
        Assert.Equal(before.ActiveCount + 2, after.ActiveCount);
        Assert.Equal(before.ExpiredPassportCount + 1, after.ExpiredPassportCount);
        Assert.Equal(before.PassportsExpiringSoonCount + 1, after.PassportsExpiringSoonCount);
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
        var nameParts = name.Split(' ', 2);
        var member = CrewMember.Create(position.Id, nameParts[0], nameParts.Length > 1 ? nameParts[1] : string.Empty, new DateOnly(1990, 1, 1), "Croatia", imoSeafarerId, "P12345", passportExpiry, "CDC12345", new DateOnly(2020, 1, 1), status);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
    }
}
