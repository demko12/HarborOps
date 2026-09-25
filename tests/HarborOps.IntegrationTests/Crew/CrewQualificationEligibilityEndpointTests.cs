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

public sealed class CrewQualificationEligibilityEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CrewQualificationEligibilityEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            $"/api/crew/qualification-eligibility?crewMemberId={Guid.NewGuid()}&rankId={Guid.NewGuid()}&from=2026-01-01&to=2026-01-10",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_only_the_read_permission_and_returns_not_configured_without_a_policy()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var (position, member) = await SeedRankAndMemberAsync();

        using var response = await client.GetAsync(
            $"/api/crew/qualification-eligibility?crewMemberId={member.Id}&rankId={position.Id}&from=2026-01-01&to=2026-01-10",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCrewQualificationEligibilityResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(QualificationStatuses.NotConfigured, result!.Status);
    }

    [Fact]
    public async Task Get_returns_400_when_the_window_exceeds_366_days()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            $"/api/crew/qualification-eligibility?crewMemberId={Guid.NewGuid()}&rankId={Guid.NewGuid()}&from=2026-01-01&to=2027-01-05",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetCrewQualificationEligibilityValidator.ToField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            $"/api/crew/qualification-eligibility?crewMemberId={Guid.NewGuid()}&rankId={Guid.NewGuid()}&from=2026-01-01&to=2026-01-10",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(CrewPosition Position, CrewMember Member)> SeedRankAndMemberAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Ana", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10], "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return (position, member);
    }
}
