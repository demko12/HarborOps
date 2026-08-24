using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class GetCrewMemberProfileEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetCrewMemberProfileEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_profile_with_certificates_and_assignments()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var position = await SeedCrewPositionAsync("Bosun");
        var member = await SeedCrewMemberAsync(position.Id, "IMO200001");
        await SeedCrewCertificateAsync(member.Id);
        await SeedCrewAssignmentAsync(member.Id, position.Id);

        using var response = await client.GetAsync(
            new Uri($"/api/crew/members/{member.Id}/profile", UriKind.Relative),
            CancellationToken.None);
        var profile = await response.Content.ReadFromJsonAsync<CrewMemberProfileResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(member.Id, profile.CrewMemberId);
        Assert.Equal(position.Id, profile.BaseRankId);
        Assert.Equal(position.RankName, profile.BaseRankName);
        Assert.Single(profile.Certificates);
        Assert.Single(profile.Assignments);
    }

    [Fact]
    public async Task Get_returns_empty_sections_when_no_certificates_or_assignments_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var position = await SeedCrewPositionAsync("Bosun");
        var member = await SeedCrewMemberAsync(position.Id, "IMO200002");

        using var response = await client.GetAsync(
            new Uri($"/api/crew/members/{member.Id}/profile", UriKind.Relative),
            CancellationToken.None);
        var profile = await response.Content.ReadFromJsonAsync<CrewMemberProfileResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Empty(profile.Certificates);
        Assert.Empty(profile.Assignments);
    }

    [Fact]
    public async Task Get_returns_not_found_for_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/crew/members/{Guid.NewGuid()}/profile", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/crew/members/{Guid.NewGuid()}/profile", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_crew_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);

        using var response = await client.GetAsync(
            new Uri($"/api/crew/members/{Guid.NewGuid()}/profile", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<CrewPosition> SeedCrewPositionAsync(string rankName)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position;
    }

    private async Task<CrewMember> SeedCrewMemberAsync(Guid baseRankId, string imoSeafarerId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var member = CrewMember.Create(
            baseRankId,
            "Ivan Petrović",
            new DateOnly(1990, 5, 14),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2028, 1, 1),
            "CDC987654",
            new DateOnly(2024, 1, 15),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member;
    }

    private async Task SeedCrewCertificateAsync(Guid crewMemberId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var certificate = CrewCertificate.Create(
            crewMemberId,
            "STCW",
            "Basic Safety Training",
            "Flag State Authority",
            "LIC-1001",
            new DateOnly(2024, 1, 1),
            new DateOnly(2027, 1, 1),
            "Valid");

        await dbContext.Set<CrewCertificate>().AddAsync(certificate);
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedCrewAssignmentAsync(Guid crewMemberId, Guid rankId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var assignment = CrewAssignment.CreatePlanned(
            crewMemberId,
            Guid.NewGuid(),
            rankId,
            null,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 1));

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
    }
}
