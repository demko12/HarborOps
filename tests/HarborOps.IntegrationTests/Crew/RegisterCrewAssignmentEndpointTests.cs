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
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterCrewAssignmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterCrewAssignmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/assignments",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_authentication_reaches_normal_validation_behavior()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = new RegisterCrewAssignmentRequest
        {
            CrewMemberId = null,
            VesselId = null,
            RankId = null,
            SignOnPortId = null,
            SignOnDate = null,
            PlannedSignOffDate = null,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/crew/assignments",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCrewAssignmentValidator.CrewMemberIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.RankIdField, problem.Errors);
        Assert.Contains(RegisterCrewAssignmentValidator.SignOnDateField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_when_the_crew_member_has_approved_unavailability()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var (memberId, rankId) = await SeedMemberAndRankAsync();
        await SeedApprovedUnavailabilityAsync(memberId, new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1));

        using var response = await client.PostAsJsonAsync(
            "/api/crew/assignments",
            new RegisterCrewAssignmentRequest
            {
                CrewMemberId = memberId,
                VesselId = Guid.NewGuid(),
                RankId = rankId,
                SignOnDate = new DateOnly(2026, 1, 15),
                PlannedSignOffDate = new DateOnly(2026, 6, 1),
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ApprovedUnavailability", problem!.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Seeded private notes", problem.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<(Guid MemberId, Guid RankId)> SeedMemberAndRankAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Ana", "Petrović", new DateOnly(1990, 1, 1), "Montenegro", $"IMO{Guid.NewGuid():N}"[..10],
            "P1234567", new DateOnly(2030, 1, 1), "CDC1234567", new DateOnly(2020, 1, 1), CrewEmploymentStatuses.Active);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
        return (member.Id, position.Id);
    }

    private async Task SeedApprovedUnavailabilityAsync(Guid crewMemberId, DateOnly startDate, DateOnly endDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var request = CrewAvailabilityRequest.Create(
            crewMemberId, "AnnualLeave", startDate, endDate, "Seeded reason.", "Seeded private notes.",
            Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        request.Approve(Guid.NewGuid(), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(request);
        await dbContext.SaveChangesAsync();
    }

    private static RegisterCrewAssignmentRequest CreateValidRequest()
    {
        return new RegisterCrewAssignmentRequest
        {
            CrewMemberId = Guid.NewGuid(),
            VesselId = Guid.NewGuid(),
            RankId = Guid.NewGuid(),
            SignOnPortId = null,
            SignOnDate = new DateOnly(2026, 1, 1),
            PlannedSignOffDate = new DateOnly(2026, 6, 1),
        };
    }
}
