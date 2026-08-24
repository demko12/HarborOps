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

public sealed class ExecuteCrewRotationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ExecuteCrewRotationEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Post_executes_the_rotation_and_stands_up_the_replacement()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var (assignment, relievingMember) = await SeedRotationReadyForExecutionAsync();

        using var response = await client.PostAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation/execute", null, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ExecuteCrewRotationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, result!.ReplacementAssignmentId);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var original = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.NotNull(original.ActualSignOffDate);
        var replacement = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == result.ReplacementAssignmentId);
        Assert.Equal(relievingMember.Id, replacement.CrewMemberId);
        Assert.False(await dbContext.Set<CrewRotation>().AnyAsync(r => r.AssignmentId == assignment.Id));
    }

    [Fact]
    public async Task Post_returns_not_found_when_no_rotation_is_planned()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PostAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation/execute", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_conflict_when_no_relieving_member_is_selected()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();
        await SeedRotationAsync(assignment.Id, null);

        using var response = await client.PostAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation/execute", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation/execute", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PostAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation/execute", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(CrewAssignment Assignment, CrewMember RelievingMember)> SeedRotationReadyForExecutionAsync()
    {
        var assignment = await SeedAssignmentAsync();

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var relievingMember = CrewMember.Create(
            assignment.RankId, "Relief Member", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900006", "P7654321", new DateOnly(2030, 1, 1), "CDC7654321",
            new DateOnly(2020, 1, 1), "Active");
        await dbContext.Set<CrewMember>().AddAsync(relievingMember);
        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignment.Id, relievingMember.Id, "2-2", new DateOnly(2026, 6, 15)));
        await dbContext.SaveChangesAsync();

        return (assignment, relievingMember);
    }

    private async Task<CrewAssignment> SeedAssignmentAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900005", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }

    private async Task SeedRotationAsync(Guid assignmentId, Guid? relievingCrewMemberId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        await dbContext.Set<CrewRotation>().AddAsync(
            CrewRotation.Create(assignmentId, relievingCrewMemberId, "2-2", new DateOnly(2026, 7, 1)));
        await dbContext.SaveChangesAsync();
    }
}
