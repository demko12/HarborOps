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

public sealed class CancelCrewAssignmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CancelCrewAssignmentEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Cancel_retains_a_planned_assignment_with_its_audit_state()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync(CrewAssignment.PlannedStatus);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/cancel",
            new CancelCrewAssignmentRequest { Notes = "Vessel change" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var cancelled = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.CancelledStatus, cancelled.Status);
        Assert.Equal("Vessel change", cancelled.CancellationNotes);
        Assert.NotNull(cancelled.CancelledAtUtc);
    }

    [Fact]
    public async Task Cancel_returns_not_found_for_unknown_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/cancel",
            new CancelCrewAssignmentRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_allows_an_active_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync(CrewAssignment.ActiveStatus);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{assignment.Id}/cancel",
            new CancelCrewAssignmentRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/cancel",
            new CancelCrewAssignmentRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/assignments/{Guid.NewGuid()}/cancel",
            new CancelCrewAssignmentRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

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
