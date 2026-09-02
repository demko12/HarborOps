using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class GetCrewEmploymentStatusHistoryEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetCrewEmploymentStatusHistoryEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_an_empty_list_when_no_changes_are_recorded()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930001");

        using var response = await client.GetAsync($"/api/crew/members/{memberId}/employment-status-history");
        var result = await response.Content.ReadFromJsonAsync<GetCrewEmploymentStatusHistoryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Get_returns_entries_newest_first()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930002");
        await SeedHistoryAsync(memberId, "OnLeave", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = await SeedHistoryAsync(memberId, "Active", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync($"/api/crew/members/{memberId}/employment-status-history");
        var result = await response.Content.ReadFromJsonAsync<GetCrewEmploymentStatusHistoryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(newest, result.Items[0].Id);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/employment-status-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/employment-status-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_no_permissions_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);
        var memberId = await SeedMemberAsync("IMO930003");

        using var response = await client.GetAsync($"/api/crew/members/{memberId}/employment-status-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedHistoryAsync(Guid crewMemberId, string status, DateTime changedAtUtc)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var history = CrewEmploymentStatusHistory.Record(crewMemberId, status, Guid.NewGuid(), null, changedAtUtc);

        await dbContext.Set<CrewEmploymentStatusHistory>().AddAsync(history);
        await dbContext.SaveChangesAsync();

        return history.Id;
    }

    private async Task<Guid> SeedMemberAsync(string imoSeafarerId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");

        var member = CrewMember.Create(
            position.Id,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            "Active");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member.Id;
    }
}
