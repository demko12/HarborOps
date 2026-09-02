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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class ChangeCrewMemberEmploymentStatusEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ChangeCrewMemberEmploymentStatusEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_changes_the_status_and_persists_a_history_entry()
    {
        var (client, userId) = await CreateAuthenticatedClientWithPermissionsAndIdAsync(Permissions.Crew.Manage);
        using var authenticatedClient = client;
        var memberId = await SeedMemberAsync("IMO920001", CrewEmploymentStatuses.Active);

        using var response = await authenticatedClient.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave, Notes = "Medical leave" });
        var result = await response.Content.ReadFromJsonAsync<ChangeCrewMemberEmploymentStatusResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(memberId, result.CrewMemberId);
        Assert.Equal(CrewEmploymentStatuses.Active, result.PreviousStatus);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, result.NewStatus);
        Assert.Equal(userId, result.ChangedByUserId);
        Assert.Equal("Medical leave", result.Notes);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == memberId);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, updated.EmploymentStatus);

        var history = await dbContext.Set<CrewEmploymentStatusHistory>()
            .SingleAsync(h => h.CrewMemberId == memberId);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, history.Status);
        Assert.Equal(userId, history.ChangedByUserId);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_an_unsupported_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO920002", CrewEmploymentStatuses.Active);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = "Inactive" });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ChangeCrewMemberEmploymentStatusValidator.StatusField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_returns_conflict_for_an_unchanged_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO920003", CrewEmploymentStatuses.Active);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Active });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_returns_conflict_when_terminating_with_an_open_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO920004", CrewEmploymentStatuses.Active);
        await SeedOpenAssignmentAsync(memberId);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Terminated });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_read_only_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO920005", CrewEmploymentStatuses.Active);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/employment-status-changes",
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientWithPermissionsAndIdAsync(
        params string[] permissions)
    {
        var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(permissions);

        using var meResponse = await client.GetAsync("/api/identity/me");
        meResponse.EnsureSuccessStatusCode();
        var account = await meResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var userId = account.GetProperty("id").GetGuid();

        return (client, userId);
    }

    private async Task<Guid> SeedOpenAssignmentAsync(Guid crewMemberId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var assignment = CrewAssignment.CreatePlanned(
            crewMemberId, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment.Id;
    }

    private async Task<Guid> SeedMemberAsync(string imoSeafarerId, string employmentStatus)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");

        var member = CrewMember.Create(
            position.Id,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            employmentStatus);

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member.Id;
    }
}
