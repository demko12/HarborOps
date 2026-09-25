using System.Net;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class ActivateCrewAssignmentEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ActivateCrewAssignmentEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Post_activates_the_assignment_when_no_policy_is_configured()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        using var response = await client.PostAsync($"/api/crew/assignments/{assignment.Id}/activate", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.ActiveStatus, saved.Status);
    }

    [Fact]
    public async Task Post_returns_409_when_a_configured_policy_finds_a_certificate_gap()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            var policy = CrewQualificationPolicy.Create(assignment.RankId, true, Guid.NewGuid(), DateTime.UtcNow);
            await dbContext.Set<CrewQualificationPolicy>().AddAsync(policy);
            await dbContext.Set<CrewQualificationRequirement>().AddAsync(
                CrewQualificationRequirement.Create(policy.Id, "STCW-II/1"));
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.PostAsync($"/api/crew/assignments/{assignment.Id}/activate", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verifyScope = _application.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await verifyDbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.PlannedStatus, saved.Status);
    }

    [Fact]
    public async Task Post_activates_when_the_member_holds_the_required_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var assignment = await SeedAssignmentAsync();

        await using (var scope = _application.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            var policy = CrewQualificationPolicy.Create(assignment.RankId, true, Guid.NewGuid(), DateTime.UtcNow);
            await dbContext.Set<CrewQualificationPolicy>().AddAsync(policy);
            await dbContext.Set<CrewQualificationRequirement>().AddAsync(
                CrewQualificationRequirement.Create(policy.Id, "STCW-II/1"));
            await dbContext.Set<CrewCertificate>().AddAsync(CrewCertificate.Create(
                assignment.CrewMemberId, "STCW-II/1", "STCW II/1 certificate", "Flag Authority", "LIC-0001",
                new DateOnly(2020, 1, 1), new DateOnly(2030, 12, 31), "Valid"));
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.PostAsync($"/api/crew/assignments/{assignment.Id}/activate", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsync($"/api/crew/assignments/{Guid.NewGuid()}/activate", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.PostAsync($"/api/crew/assignments/{Guid.NewGuid()}/activate", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<CrewAssignment> SeedAssignmentAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko", "Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10], "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }
}
