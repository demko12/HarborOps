using System.Net;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class DeleteCrewAssignmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public DeleteCrewAssignmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Delete_removes_a_planned_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync(CrewAssignment.PlannedStatus);

        using var response = await client.DeleteAsync(
            new Uri($"/api/crew/assignments/{assignment.Id}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        Assert.False(await dbContext.Set<CrewAssignment>().AnyAsync(a => a.Id == assignment.Id));
    }

    [Fact]
    public async Task Delete_returns_not_found_for_unknown_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.DeleteAsync(
            new Uri($"/api/crew/assignments/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_conflict_for_a_non_planned_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync(CrewAssignment.ActiveStatus);

        using var response = await client.DeleteAsync(
            new Uri($"/api/crew/assignments/{assignment.Id}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.DeleteAsync(
            new Uri($"/api/crew/assignments/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.DeleteAsync(
            new Uri($"/api/crew/assignments/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The domain has no public transition from Planned to Active (signing on/off an
    // assignment is out of scope), so status is forced directly for the conflict test.
    private async Task<CrewAssignment> SeedAssignmentAsync(string status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var assignment = CrewAssignment.CreatePlanned(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 1));

        if (status != CrewAssignment.PlannedStatus)
        {
            typeof(CrewAssignment).GetProperty(nameof(CrewAssignment.Status))!.SetValue(assignment, status);
        }

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }
}
