using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class UpdateCrewRotationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateCrewRotationEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Put_updates_the_planned_rotation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();
        await SeedRotationAsync(assignment.Id);

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new UpdateCrewRotationRequest { RotationPattern = "4-4", PlannedReliefDate = new DateOnly(2026, 8, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_not_found_when_no_rotation_is_planned()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/rotation",
            new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation",
            new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/rotation",
            new UpdateCrewRotationRequest { RotationPattern = "2-2", PlannedReliefDate = new DateOnly(2026, 7, 1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<CrewAssignment> SeedAssignmentAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900004", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
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
