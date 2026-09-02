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

public sealed class PlanCrewRotationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public PlanCrewRotationEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Post_plans_a_rotation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_a_missing_rotation_pattern()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new PlanCrewRotationRequest { PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CrewRotationValidator.RotationPatternField, problem!.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation",
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_conflict_when_a_rotation_already_exists()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();
        await SeedRotationAsync(assignment.Id);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new PlanCrewRotationRequest { RotationPattern = "4-4", PlannedReliefDate = new DateOnly(2026, 8, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_conflict_for_self_relief()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new PlanCrewRotationRequest
            {
                RelievingCrewMemberId = assignment.CrewMemberId,
                RotationPattern = "2-2",
                PlannedReliefDate = new DateOnly(2026, 7, 1),
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation",
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation",
            new PlanCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<CrewAssignment> SeedAssignmentAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900003", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }

    private async Task SeedRotationAsync(Guid assignmentId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignmentId, null, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
    }
}
