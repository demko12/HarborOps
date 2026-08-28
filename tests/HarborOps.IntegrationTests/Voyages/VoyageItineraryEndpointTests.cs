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

public sealed class VoyageItineraryEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public VoyageItineraryEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_itinerary_plans_ordered_stops_and_auto_generated_legs()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-100");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            new PlanVoyageItineraryRequest
            {
                Stops =
                [
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 1,
                        PortId = bar,
                        DistanceFromPrevious = 0,
                        EstimatedDurationFromPrevious = 0,
                        Eta = PlannedDepartureUtc,
                        Etd = PlannedDepartureUtc.AddHours(4),
                    },
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 2,
                        PortId = kotor,
                        DistanceFromPrevious = 60m,
                        EstimatedDurationFromPrevious = 300,
                        Eta = PlannedDepartureUtc.AddHours(10),
                        Etd = PlannedDepartureUtc.AddHours(14),
                    },
                ],
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<PlanVoyageItineraryResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Stops.Count);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var legCount = await dbContext.Set<VoyageLeg>()
            .CountAsync(leg => leg.VoyageId == voyage, CancellationToken.None);

        Assert.Equal(1, legCount);
    }

    [Fact]
    public async Task Post_itinerary_returns_validation_problem_for_non_increasing_chronology()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-101");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            new PlanVoyageItineraryRequest
            {
                Stops =
                [
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 1,
                        PortId = bar,
                        DistanceFromPrevious = 0,
                        EstimatedDurationFromPrevious = 0,
                        Eta = PlannedDepartureUtc,
                        Etd = PlannedDepartureUtc.AddHours(4),
                    },
                    new PlanVoyageItineraryStop
                    {
                        StopOrder = 2,
                        PortId = kotor,
                        DistanceFromPrevious = 60m,
                        EstimatedDurationFromPrevious = 300,
                        Eta = PlannedDepartureUtc.AddHours(2),
                        Etd = PlannedDepartureUtc.AddHours(6),
                    },
                ],
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(PlanVoyageItineraryValidator.StopsField, problem.Errors);
    }

    [Fact]
    public async Task Post_itinerary_returns_not_found_for_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PostAsJsonAsync(
            ItineraryUri(Guid.NewGuid()),
            ValidStopsRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_itinerary_returns_conflict_when_voyage_already_has_one()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-102");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");
        var request = ValidStopsRequest(bar, kotor);

        using var firstResponse = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            request,
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Get_itinerary_returns_stops_berth_and_delays()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-103");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            ValidStopsRequest(bar, kotor),
            CancellationToken.None);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await readClient.GetAsync(ItineraryUri(voyage), CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<VoyageItineraryResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Stops.Count);
        Assert.Equal([1, 2], result.Stops.Select(stop => stop.StopOrder));
        Assert.Empty(result.Delays);
    }

    [Fact]
    public async Task Berth_arrival_and_departure_execute_a_stop_end_to_end()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-104");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");
        var berth = await SeedBerthAsync(bar);

        using var planResponse = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            ValidStopsRequest(bar, kotor),
            CancellationToken.None);
        var plan = await planResponse.Content.ReadFromJsonAsync<PlanVoyageItineraryResponse>(
            CancellationToken.None);
        var firstStopId = plan!.Stops[0].StopId;

        using var berthResponse = await client.PostAsJsonAsync(
            BerthUri(voyage, firstStopId),
            new AssignBerthToStopRequest { BerthId = berth },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, berthResponse.StatusCode);

        using var departVoyageResponse = await client.PostAsJsonAsync(
            DepartureUri(voyage),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, departVoyageResponse.StatusCode);

        var actualArrivalUtc = DateTime.UtcNow;
        using var arrivalResponse = await client.PostAsJsonAsync(
            StopArrivalUri(voyage, firstStopId),
            new RecordStopArrivalRequest { ActualArrivalUtc = actualArrivalUtc },
            CancellationToken.None);
        var arrival = await arrivalResponse.Content.ReadFromJsonAsync<RecordStopArrivalResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, arrivalResponse.StatusCode);
        Assert.Equal(VoyageStop.ArrivedStatus, arrival!.Status);

        await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);

        using var departureResponse = await client.PostAsJsonAsync(
            StopDepartureUri(voyage, firstStopId),
            new RecordStopDepartureRequest { ActualDepartureUtc = DateTime.UtcNow },
            CancellationToken.None);
        var departure = await departureResponse.Content.ReadFromJsonAsync<RecordStopDepartureResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, departureResponse.StatusCode);
        Assert.Equal(VoyageStop.DepartedStatus, departure!.Status);
    }

    [Fact]
    public async Task Assign_berth_rejects_a_second_overlapping_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyageOne = await SeedPlannedVoyageAsync("VOY-ITN-105");
        var voyageTwo = await SeedPlannedVoyageAsync("VOY-ITN-106");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");
        var berth = await SeedBerthAsync(bar);

        var planOne = await (await client.PostAsJsonAsync(
                ItineraryUri(voyageOne),
                ValidStopsRequest(bar, kotor),
                CancellationToken.None))
            .Content.ReadFromJsonAsync<PlanVoyageItineraryResponse>(CancellationToken.None);
        var planTwo = await (await client.PostAsJsonAsync(
                ItineraryUri(voyageTwo),
                ValidStopsRequest(bar, kotor),
                CancellationToken.None))
            .Content.ReadFromJsonAsync<PlanVoyageItineraryResponse>(CancellationToken.None);

        using var firstAssignment = await client.PostAsJsonAsync(
            BerthUri(voyageOne, planOne!.Stops[0].StopId),
            new AssignBerthToStopRequest { BerthId = berth },
            CancellationToken.None);

        using var secondAssignment = await client.PostAsJsonAsync(
            BerthUri(voyageTwo, planTwo!.Stops[0].StopId),
            new AssignBerthToStopRequest { BerthId = berth },
            CancellationToken.None);
        var problem = await secondAssignment.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondAssignment.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Post_delay_records_reason_duration_and_server_time()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-107");

        var beforeUtc = DateTime.UtcNow;

        using var response = await client.PostAsJsonAsync(
            DelaysUri(voyage),
            new RecordDelayRequest { Reason = "Port congestion", Duration = 90 },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordDelayResponse>(
            CancellationToken.None);

        var afterUtc = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(voyage, result.VoyageId);
        Assert.InRange(result.CreatedAt, beforeUtc.AddSeconds(-1), afterUtc.AddSeconds(1));
    }

    [Fact]
    public async Task Post_delay_returns_validation_problem_for_non_positive_duration()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Manage);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-108");

        using var response = await client.PostAsJsonAsync(
            DelaysUri(voyage),
            new RecordDelayRequest { Reason = "Weather", Duration = 0 },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RecordDelayValidator.DurationField, problem.Errors);
    }

    [Fact]
    public async Task Get_itinerary_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(ItineraryUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_itinerary_without_the_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var voyage = await SeedPlannedVoyageAsync("VOY-ITN-109");
        var bar = await SeedPortAsync("Port of Bar");
        var kotor = await SeedPortAsync("Port of Kotor");

        using var response = await client.PostAsJsonAsync(
            ItineraryUri(voyage),
            ValidStopsRequest(bar, kotor),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_itinerary_without_the_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(ItineraryUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static PlanVoyageItineraryRequest ValidStopsRequest(Guid firstPortId, Guid secondPortId)
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

    private static Uri ItineraryUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/itinerary", UriKind.Relative);
    }

    private static Uri BerthUri(Guid voyageId, Guid stopId)
    {
        return new Uri($"/api/voyages/{voyageId}/stops/{stopId}/berth", UriKind.Relative);
    }

    private static Uri StopArrivalUri(Guid voyageId, Guid stopId)
    {
        return new Uri($"/api/voyages/{voyageId}/stops/{stopId}/arrival", UriKind.Relative);
    }

    private static Uri StopDepartureUri(Guid voyageId, Guid stopId)
    {
        return new Uri($"/api/voyages/{voyageId}/stops/{stopId}/departure", UriKind.Relative);
    }

    private static Uri DepartureUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/departure", UriKind.Relative);
    }

    private static Uri DelaysUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/delays", UriKind.Relative);
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
