using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class GetCrewRotationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetCrewRotationEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_returns_the_planned_rotation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var assignment = await SeedAssignmentAsync();
        await SeedRotationAsync(assignment.Id);

        using var response = await client.GetAsync($"/api/crew/assignments/{assignment.Id}/rotation");
        var item = await response.Content.ReadFromJsonAsync<CrewRotationListItem>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(assignment.Id, item!.AssignmentId);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync($"/api/crew/assignments/{Guid.NewGuid()}/rotation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_not_found_when_no_rotation_is_planned()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.GetAsync($"/api/crew/assignments/{assignment.Id}/rotation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync($"/api/crew/assignments/{Guid.NewGuid()}/rotation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync($"/api/crew/assignments/{Guid.NewGuid()}/rotation");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<CrewAssignment> SeedAssignmentAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900002", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
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
