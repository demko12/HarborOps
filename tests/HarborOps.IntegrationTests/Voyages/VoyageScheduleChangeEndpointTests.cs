using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class VoyageScheduleChangeEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public VoyageScheduleChangeEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_schedule_returns_the_revised_voyage_and_the_change_history()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-200");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        var beforeUtc = DateTime.UtcNow;

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageScheduleResponse>(
            CancellationToken.None);

        var afterUtc = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(Voyage.PlannedStatus, result.Voyage.Status);
        Assert.Equal(bar, result.Voyage.DeparturePortId);
        Assert.Equal("Port of Bar", result.Voyage.DeparturePortName);
        Assert.Equal(kotor, result.Voyage.ArrivalPortId);

        var change = Assert.Single(result.Changes);
        Assert.Equal(VoyageScheduleChange.AmendedChangeType, change.ChangeType);
        Assert.Equal("Berth window moved", change.Reason);
        Assert.NotEqual(Guid.Empty, change.ChangedByUserId);
        Assert.InRange(change.ChangedAtUtc, beforeUtc.AddSeconds(-1), afterUtc.AddSeconds(1));
        Assert.Equal(PlannedDepartureUtc, change.PreviousPlannedDeparture);
        Assert.Equal(PlannedDepartureUtc.AddDays(1), change.NewPlannedDeparture);
    }

    [Fact]
    public async Task Put_schedule_returns_validation_problem_without_a_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-201");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor) with { Reason = "  " },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(AmendVoyageScheduleValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Put_schedule_returns_not_found_for_an_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(Guid.NewGuid()),
            AmendRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_schedule_returns_not_found_for_an_unknown_port()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-202");
        var bar = await SeedPortAsync("Port of Bar");

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_schedule_returns_not_found_for_an_unknown_route()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-203");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor) with { RouteId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_schedule_returns_conflict_when_the_route_does_not_connect_the_ports()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-204");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");
        var genoa = await SeedPortAsync("Genoa terminal");
        var route = await SeedRouteAsync(bar, kotor, genoa);

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor) with { RouteId = route },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("The route does not connect the requested ports.", problem.Title);
    }

    [Fact]
    public async Task Put_schedule_returns_conflict_when_nothing_changes()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-205");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var firstResponse = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);
        using var secondResponse = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Put_schedule_returns_conflict_for_a_voyage_that_is_not_planned()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-206");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var departureResponse = await client.PostAsJsonAsync(
            DepartureUri(voyage),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, departureResponse.StatusCode);

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_schedule_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-207");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_cancel_marks_the_voyage_cancelled_and_records_the_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-208");

        using var response = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageScheduleResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(Voyage.CancelledStatus, result.Voyage.Status);

        var change = Assert.Single(result.Changes);
        Assert.Equal(VoyageScheduleChange.CancelledChangeType, change.ChangeType);
        Assert.Equal(Voyage.PlannedStatus, change.PreviousStatus);
        Assert.Equal(Voyage.CancelledStatus, change.NewStatus);
        Assert.Equal("Charter withdrawn", change.Reason);
    }

    [Fact]
    public async Task Post_cancel_preserves_the_itinerary_stops_legs_and_delays()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-209");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");
        var berth = await SeedBerthAsync(bar);

        using var planResponse = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            ItineraryRequest(bar, kotor),
            CancellationToken.None);
        var plan = await planResponse.Content.ReadFromJsonAsync<PlanVoyageItineraryResponse>(
            CancellationToken.None);

        using var berthResponse = await client.PostAsJsonAsync(
            BerthUri(voyage, plan!.Stops[0].StopId),
            new AssignBerthToStopRequest { BerthId = berth },
            CancellationToken.None);
        using var delayResponse = await client.PostAsJsonAsync(
            DelaysUri(voyage),
            new RecordDelayRequest { Reason = "Port congestion", Duration = 90 },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, berthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, delayResponse.StatusCode);

        using var cancelResponse = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var storedVoyage = await dbContext.Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage, CancellationToken.None);
        var stopCount = await dbContext.Set<VoyageStop>()
            .CountAsync(stop => stop.VoyageId == voyage, CancellationToken.None);
        var legCount = await dbContext.Set<VoyageLeg>()
            .CountAsync(leg => leg.VoyageId == voyage, CancellationToken.None);
        var delayCount = await dbContext.Set<Delay>()
            .CountAsync(delay => delay.VoyageId == voyage, CancellationToken.None);
        var berthScheduleCount = await dbContext.Set<BerthSchedule>()
            .CountAsync(schedule => schedule.BerthId == berth, CancellationToken.None);

        Assert.Equal(Voyage.CancelledStatus, storedVoyage.Status);
        Assert.Equal(2, stopCount);
        Assert.Equal(1, legCount);
        Assert.Equal(1, delayCount);
        Assert.Equal(1, berthScheduleCount);
    }

    [Fact]
    public async Task Post_cancel_returns_validation_problem_without_a_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-210");

        using var response = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CancelVoyageValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Post_cancel_returns_not_found_for_an_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);

        using var response = await client.PostAsJsonAsync(
            CancelUri(Guid.NewGuid()),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_cancel_returns_conflict_for_an_already_cancelled_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-211");

        using var firstResponse = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn again" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_cancel_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-212");

        using var response = await client.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_schedule_changes_returns_the_history_newest_first()
    {
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-SCH-213");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var amendResponse = await manageClient.PutAsJsonAsync(
            ScheduleUri(voyage),
            AmendRequest(bar, kotor),
            CancellationToken.None);
        using var cancelResponse = await manageClient.PostAsJsonAsync(
            CancelUri(voyage),
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, amendResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await readClient.GetAsync(
            ScheduleChangesUri(voyage),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageScheduleResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(Voyage.CancelledStatus, result.Voyage.Status);
        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(VoyageScheduleChange.CancelledChangeType, result.Changes[0].ChangeType);
        Assert.Equal(VoyageScheduleChange.AmendedChangeType, result.Changes[1].ChangeType);
        Assert.True(result.Changes[0].ChangedAtUtc >= result.Changes[1].ChangedAtUtc);
    }

    [Fact]
    public async Task Get_schedule_changes_returns_not_found_for_an_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            ScheduleChangesUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_schedule_changes_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            ScheduleChangesUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_schedule_changes_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            ScheduleChangesUri(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static AmendVoyageScheduleRequest AmendRequest(Guid departurePortId, Guid arrivalPortId)
    {
        return new AmendVoyageScheduleRequest
        {
            DeparturePortId = departurePortId,
            ArrivalPortId = arrivalPortId,
            PlannedDepartureUtc = PlannedDepartureUtc.AddDays(1),
            PlannedArrivalUtc = PlannedArrivalUtc.AddDays(1),
            Reason = "Berth window moved",
        };
    }

    private static PlanVoyageItineraryRequest ItineraryRequest(Guid firstPortId, Guid secondPortId)
    {
        return new PlanVoyageItineraryRequest
        {
            Stops =
            [
                new PlanVoyageItineraryStop
                {
                    StopOrder = 1,
                    PortId = firstPortId,
                    DistanceFromPrevious = 0,
                    EstimatedDurationFromPrevious = 0,
                    Eta = PlannedDepartureUtc,
                    Etd = PlannedDepartureUtc.AddHours(4),
                },
                new PlanVoyageItineraryStop
                {
                    StopOrder = 2,
                    PortId = secondPortId,
                    DistanceFromPrevious = 60m,
                    EstimatedDurationFromPrevious = 300,
                    Eta = PlannedDepartureUtc.AddHours(10),
                    Etd = PlannedDepartureUtc.AddHours(14),
                },
            ],
        };
    }

    private static Uri ScheduleUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/schedule", UriKind.Relative);
    }

    private static Uri CancelUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/cancel", UriKind.Relative);
    }

    private static Uri ScheduleChangesUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/schedule-changes", UriKind.Relative);
    }

    private static Uri ItineraryUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/itinerary", UriKind.Relative);
    }

    private static Uri BerthUri(Guid voyageId, Guid stopId)
    {
        return new Uri($"/api/voyages/{voyageId}/stops/{stopId}/berth", UriKind.Relative);
    }

    private static Uri DelaysUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/delays", UriKind.Relative);
    }

    private static Uri DepartureUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/departure", UriKind.Relative);
    }

    private async Task<Guid> SeedPlannedVoyageAsync(string voyageNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage.Id;
    }

    private async Task<Guid> SeedPortAsync(string name)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var port = Port.Create(name, "Montenegro");
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.SaveChangesAsync();

        return port.Id;
    }

    private async Task<Guid> SeedRouteAsync(params Guid[] portIds)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var route = Route.Create("Adriatic feeder", 560m, 2100, null);
        await dbContext.Set<Route>().AddAsync(route);
        await dbContext.Set<RoutePort>().AddRangeAsync(portIds.Select((portId, index) =>
            RoutePort.Create(route.Id, portId, index + 1, index == 0 ? 0m : 60m, index == 0 ? 0 : 300)));
        await dbContext.SaveChangesAsync();

        return route.Id;
    }

    private async Task<Guid> SeedBerthAsync(Guid portId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var berth = Berth.Create(portId, "Pier 1", 200m, 10m, BerthStatus.Available);
        await dbContext.Set<Berth>().AddAsync(berth);
        await dbContext.SaveChangesAsync();

        return berth.Id;
    }
}
