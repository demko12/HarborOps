using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class CrewAvailabilityEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CrewAvailabilityEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_request_creates_a_pending_period_with_a_transition()
    {
        var (client, userId) = await CreateManageClientWithIdAsync();
        using var authenticatedClient = client;
        var memberId = await SeedMemberAsync("IMO930001");

        using var response = await authenticatedClient.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/availability-requests",
            new CreateCrewAvailabilityRequestRequest
            {
                Type = "AnnualLeave",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 10),
                Reason = "Family holiday.",
            });
        var created = await response.Content.ReadFromJsonAsync<CreateCrewAvailabilityRequestResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var saved = await dbContext.Set<CrewAvailabilityRequest>().SingleAsync(r => r.Id == created!.Id);
        Assert.Equal(CrewAvailabilityRequest.RequestedStatus, saved.Status);
        Assert.Equal(userId, saved.RequestedByUserId);
        var transition = await dbContext.Set<CrewAvailabilityTransition>().SingleAsync(t => t.AvailabilityRequestId == created!.Id);
        Assert.Equal(CrewAvailabilityTransition.RequestedEvent, transition.EventType);
        Assert.Equal(userId, transition.ActorUserId);
    }

    [Fact]
    public async Task Post_request_returns_validation_problem_for_a_reversed_range()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO930002");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/availability-requests",
            new CreateCrewAvailabilityRequestRequest
            {
                Type = "AnnualLeave",
                StartDate = new DateOnly(2026, 10, 10),
                EndDate = new DateOnly(2026, 10, 1),
                Reason = "Reversed.",
            });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(RequestCrewAvailabilityValidator.EndDateField, problem!.Errors);
    }

    [Fact]
    public async Task Post_request_returns_not_found_for_an_unknown_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/availability-requests",
            new CreateCrewAvailabilityRequestRequest
            {
                Type = "AnnualLeave",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 10),
                Reason = "Ghost.",
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_request_returns_conflict_for_an_overlapping_period()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO930003");
        await SeedRequestAsync(memberId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 15));

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/availability-requests",
            new CreateCrewAvailabilityRequestRequest
            {
                Type = "SickLeave",
                StartDate = new DateOnly(2026, 10, 15),
                EndDate = new DateOnly(2026, 10, 20),
                Reason = "Touching endpoint.",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_request_with_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930004");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{memberId}/availability-requests",
            new CreateCrewAvailabilityRequestRequest
            {
                Type = "AnnualLeave",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 10),
                Reason = "Denied.",
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_list_is_filterable_and_requires_crew_read()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930005");
        await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        var body = await client.GetFromJsonAsync<GetCrewAvailabilityResponse>(
            $"/api/crew/availability?crewMemberId={memberId}&status=Requested");

        Assert.NotNull(body);
        Assert.Single(body!.Items);

        using var forbidden = await _application.CreateAuthenticatedClientAsync(roleNames: []);
        using var forbiddenResponse = await forbidden.GetAsync("/api/crew/availability");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Get_member_availability_returns_history_and_404_for_unknown()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930006");
        await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        var body = await client.GetFromJsonAsync<GetCrewMemberAvailabilityResponse>(
            $"/api/crew/members/{memberId}/availability");
        Assert.Single(body!.Items);
        Assert.Single(body.Items[0].History);

        using var unknown = await client.GetAsync($"/api/crew/members/{Guid.NewGuid()}/availability");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Approve_transitions_the_request_and_records_the_actor()
    {
        var (client, reviewerId) = await CreateManageClientWithIdAsync();
        using var authenticatedClient = client;
        var memberId = await SeedMemberAsync("IMO930007");
        var requestId = await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        using var response = await authenticatedClient.PostAsync(
            $"/api/crew/availability-requests/{requestId}/approve", null, CancellationToken.None);
        var decision = await response.Content.ReadFromJsonAsync<CrewAvailabilityDecisionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CrewAvailabilityRequest.ApprovedStatus, decision!.NewStatus);
        Assert.Equal(reviewerId, decision.ActorUserId);
    }

    [Fact]
    public async Task Approve_returns_conflict_when_the_requester_is_the_reviewer()
    {
        var (client, reviewerId) = await CreateManageClientWithIdAsync();
        using var authenticatedClient = client;
        var memberId = await SeedMemberAsync("IMO930008");
        var requestId = await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), requestedByUserId: reviewerId);

        using var response = await authenticatedClient.PostAsync(
            $"/api/crew/availability-requests/{requestId}/approve", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Approve_returns_conflict_when_the_period_overlaps_an_assignment()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO930009");
        await SeedAssignmentAsync(memberId, new DateOnly(2026, 9, 20), new DateOnly(2026, 11, 20));
        var requestId = await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        using var response = await client.PostAsync(
            $"/api/crew/availability-requests/{requestId}/approve", null, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Seeded private notes", problem!.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_returns_conflict_when_the_request_is_stale()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO930010");
        var requestId = await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));
        using (var first = await client.PostAsync($"/api/crew/availability-requests/{requestId}/approve", null, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await client.PostAsync($"/api/crew/availability-requests/{requestId}/approve", null, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Reject_requires_a_reason_and_transitions_the_request()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO930011");
        var requestId = await SeedRequestAsync(memberId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));

        using var missingReason = await client.PostAsJsonAsync(
            $"/api/crew/availability-requests/{requestId}/reject", new RejectCrewAvailabilityRequestRequest());
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        using var rejected = await client.PostAsJsonAsync(
            $"/api/crew/availability-requests/{requestId}/reject", new RejectCrewAvailabilityRequestRequest { Reason = "No cover." });
        var decision = await rejected.Content.ReadFromJsonAsync<CrewAvailabilityDecisionResponse>();

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(CrewAvailabilityRequest.RejectedStatus, decision!.NewStatus);
    }

    [Fact]
    public async Task Cancel_allows_a_future_approved_period_but_not_one_that_has_started()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var future = await SeedMemberAsync("IMO930012");
        var futureRequestId = await SeedRequestAsync(future, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 10), CrewAvailabilityRequest.ApprovedStatus);

        using var cancelFuture = await client.PostAsJsonAsync(
            $"/api/crew/availability-requests/{futureRequestId}/cancel", new CancelCrewAvailabilityRequestRequest { Reason = "Plans changed." });
        Assert.Equal(HttpStatusCode.OK, cancelFuture.StatusCode);

        var started = await SeedMemberAsync("IMO930013");
        var startedRequestId = await SeedRequestAsync(started, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20), CrewAvailabilityRequest.ApprovedStatus);

        using var cancelStarted = await client.PostAsJsonAsync(
            $"/api/crew/availability-requests/{startedRequestId}/cancel", new CancelCrewAvailabilityRequestRequest { Reason = "Too late." });
        Assert.Equal(HttpStatusCode.Conflict, cancelStarted.StatusCode);
    }

    [Fact]
    public async Task Get_conflicts_reports_approved_unavailability_without_private_text()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO930014");
        await SeedRequestAsync(memberId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), CrewAvailabilityRequest.ApprovedStatus);

        var body = await client.GetFromJsonAsync<GetCrewAvailabilityConflictsResponse>(
            $"/api/crew/availability/conflicts?crewMemberId={memberId}&from=2026-10-01&to=2026-10-31");

        var conflict = Assert.Single(body!.Conflicts);
        Assert.Equal("ApprovedUnavailability", conflict.RecordType);
        Assert.Equal("AnnualLeave", conflict.Detail);
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        using var client = _application.CreateClient();

        using var listResponse = await client.GetAsync("/api/crew/availability");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateManageClientWithIdAsync()
    {
        var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        using var meResponse = await client.GetAsync("/api/identity/me");
        meResponse.EnsureSuccessStatusCode();
        var account = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, account.GetProperty("id").GetGuid());
    }

    private async Task<Guid> SeedMemberAsync(string imoSeafarerId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Ana", "Petrović", new DateOnly(1990, 1, 1), "Montenegro", imoSeafarerId,
            "P1234567", new DateOnly(2030, 1, 1), "CDC1234567", new DateOnly(2020, 1, 1), CrewEmploymentStatuses.Active);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
        return member.Id;
    }

    private async Task<Guid> SeedRequestAsync(
        Guid crewMemberId,
        DateOnly startDate,
        DateOnly endDate,
        string status = CrewAvailabilityRequest.RequestedStatus,
        Guid? requestedByUserId = null)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var request = CrewAvailabilityRequest.Create(
            crewMemberId, "AnnualLeave", startDate, endDate, "Seeded reason.", "Seeded private notes.",
            requestedByUserId ?? Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        if (status == CrewAvailabilityRequest.ApprovedStatus)
        {
            request.Approve(Guid.NewGuid(), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }

        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(request);
        await dbContext.Set<CrewAvailabilityTransition>().AddAsync(
            CrewAvailabilityTransition.RecordRequested(request.Id, request.RequestedByUserId, request.RequestedAtUtc));
        await dbContext.SaveChangesAsync();
        return request.Id;
    }

    private async Task SeedAssignmentAsync(Guid crewMemberId, DateOnly signOnDate, DateOnly plannedSignOffDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        await dbContext.Set<CrewAssignment>().AddAsync(
            CrewAssignment.CreatePlanned(crewMemberId, Guid.NewGuid(), Guid.NewGuid(), null, signOnDate, plannedSignOffDate));
        await dbContext.SaveChangesAsync();
    }
}
