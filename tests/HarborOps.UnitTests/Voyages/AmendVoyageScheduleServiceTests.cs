using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class AmendVoyageScheduleServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime UtcNow = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ActingUserId =
        new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Amend_updates_the_voyage_and_records_old_and_new_values()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-100");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = CreateService(dbContext);

        var response = await service.AmendAsync(
            voyage.Id,
            ActingUserId,
            Request(bar.Id, kotor.Id, reason: " Berth window moved "),
            CancellationToken.None);

        Assert.Equal(Voyage.PlannedStatus, response.Voyage.Status);
        Assert.Equal(bar.Id, response.Voyage.DeparturePortId);
        Assert.Equal("Port of Bar", response.Voyage.DeparturePortName);
        Assert.Equal(kotor.Id, response.Voyage.ArrivalPortId);
        Assert.Equal(PlannedDepartureUtc.AddDays(1), response.Voyage.PlannedDeparture);

        var change = Assert.Single(response.Changes);
        Assert.Equal(VoyageScheduleChange.AmendedChangeType, change.ChangeType);
        Assert.Equal("Berth window moved", change.Reason);
        Assert.Null(change.PreviousDeparturePortId);
        Assert.Equal(bar.Id, change.NewDeparturePortId);
        Assert.Null(change.PreviousArrivalPortId);
        Assert.Equal(kotor.Id, change.NewArrivalPortId);
        Assert.Equal(PlannedDepartureUtc, change.PreviousPlannedDeparture);
        Assert.Equal(PlannedDepartureUtc.AddDays(1), change.NewPlannedDeparture);
        Assert.Equal(PlannedArrivalUtc, change.PreviousPlannedArrival);
        Assert.Equal(PlannedArrivalUtc.AddDays(1), change.NewPlannedArrival);
        Assert.Equal(Voyage.PlannedStatus, change.PreviousStatus);
        Assert.Equal(Voyage.PlannedStatus, change.NewStatus);
    }

    [Fact]
    public async Task Amend_records_the_acting_user_and_the_server_timestamp()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-101");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = CreateService(dbContext);

        var response = await service.AmendAsync(
            voyage.Id,
            ActingUserId,
            Request(bar.Id, kotor.Id),
            CancellationToken.None);

        var change = Assert.Single(response.Changes);
        Assert.Equal(ActingUserId, change.ChangedByUserId);
        Assert.Equal(UtcNow, change.ChangedAtUtc);

        var persisted = await dbContext.Set<VoyageScheduleChange>()
            .SingleAsync(candidate => candidate.Id == change.ChangeId, CancellationToken.None);

        Assert.Equal(ActingUserId, persisted.ChangedByUserId);
        Assert.Equal(UtcNow, persisted.ChangedAtUtc);
    }

    [Fact]
    public async Task Amend_persists_the_voyage_and_the_history_entry_together()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;
        Guid barId;
        Guid kotorId;

        await using (var dbContext = CreateDbContext(databaseName))
        {
            var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-102");
            var bar = await SeedPortAsync(dbContext, "Port of Bar");
            var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
            voyageId = voyage.Id;
            barId = bar.Id;
            kotorId = kotor.Id;

            await CreateService(dbContext).AmendAsync(
                voyageId,
                ActingUserId,
                Request(barId, kotorId),
                CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext(databaseName);
        var storedVoyage = await verificationContext.Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyageId, CancellationToken.None);
        var storedChange = await verificationContext.Set<VoyageScheduleChange>()
            .SingleAsync(candidate => candidate.VoyageId == voyageId, CancellationToken.None);

        Assert.Equal(barId, storedVoyage.DeparturePortId);
        Assert.Equal(kotorId, storedVoyage.ArrivalPortId);
        Assert.Equal(PlannedDepartureUtc.AddDays(1), storedVoyage.PlannedDeparture);
        Assert.Equal(VoyageScheduleChange.AmendedChangeType, storedChange.ChangeType);
    }

    [Fact]
    public async Task Amend_rejects_a_request_that_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-103");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var service = CreateService(dbContext);

        await service.AmendAsync(
            voyage.Id,
            ActingUserId,
            Request(bar.Id, kotor.Id),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageScheduleUnchangedException>(
            () => service.AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, kotor.Id, reason: "Same schedule again"),
                CancellationToken.None));

        Assert.Equal(voyage.Id, exception.VoyageId);

        var changeCount = await dbContext.Set<VoyageScheduleChange>()
            .CountAsync(change => change.VoyageId == voyage.Id, CancellationToken.None);

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task Amend_rejects_a_voyage_that_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-104");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        voyage.Depart(PlannedDepartureUtc);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageScheduleNotAmendableException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, kotor.Id),
                CancellationToken.None));

        Assert.Equal(voyage.Id, exception.VoyageId);
        Assert.Equal(Voyage.InProgressStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Amend_rejects_an_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => CreateService(dbContext).AmendAsync(
                unknownVoyageId,
                ActingUserId,
                Request(bar.Id, kotor.Id),
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    [Fact]
    public async Task Amend_rejects_an_unknown_port()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-105");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var unknownPortId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PortNotFoundException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, unknownPortId),
                CancellationToken.None));

        Assert.Equal(unknownPortId, exception.PortId);
    }

    [Fact]
    public async Task Amend_rejects_an_unknown_route()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-106");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var unknownRouteId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<RouteNotFoundException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, kotor.Id, routeId: unknownRouteId),
                CancellationToken.None));

        Assert.Equal(unknownRouteId, exception.RouteId);
    }

    [Fact]
    public async Task Amend_accepts_a_route_whose_endpoints_match_the_requested_ports()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-107");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var genoa = await SeedPortAsync(dbContext, "Genoa terminal");
        var route = await SeedRouteAsync(dbContext, bar.Id, kotor.Id, genoa.Id);

        var response = await CreateService(dbContext).AmendAsync(
            voyage.Id,
            ActingUserId,
            Request(bar.Id, genoa.Id, routeId: route.Id),
            CancellationToken.None);

        Assert.Equal(route.Id, response.Voyage.RouteId);
        Assert.Equal("Adriatic feeder", response.Voyage.RouteName);

        var change = Assert.Single(response.Changes);
        Assert.Null(change.PreviousRouteId);
        Assert.Equal(route.Id, change.NewRouteId);
        Assert.Equal("Adriatic feeder", change.NewRouteName);
    }

    [Fact]
    public async Task Amend_rejects_a_route_that_does_not_connect_the_requested_ports()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-108");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");
        var genoa = await SeedPortAsync(dbContext, "Genoa terminal");
        var route = await SeedRouteAsync(dbContext, bar.Id, kotor.Id, genoa.Id);

        var exception = await Assert.ThrowsAsync<VoyageRouteEndpointMismatchException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, kotor.Id, routeId: route.Id),
                CancellationToken.None));

        Assert.Equal(route.Id, exception.RouteId);

        var storedVoyage = await dbContext.Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);
        var changeCount = await dbContext.Set<VoyageScheduleChange>()
            .CountAsync(change => change.VoyageId == voyage.Id, CancellationToken.None);

        Assert.Null(storedVoyage.DeparturePortId);
        Assert.Equal(PlannedDepartureUtc, storedVoyage.PlannedDeparture);
        Assert.Equal(0, changeCount);
    }

    [Theory]
    [InlineData("VOY-AMD-113", "")]
    [InlineData("VOY-AMD-114", "   ")]
    public async Task Amend_requires_a_reason(string voyageNumber, string reason)
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, voyageNumber);
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");

        var exception = await Assert.ThrowsAsync<AmendVoyageScheduleValidationException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, kotor.Id, reason: reason),
                CancellationToken.None));

        Assert.Contains(
            AmendVoyageScheduleValidator.ReasonField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Amend_rejects_a_non_utc_planned_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-110");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");

        var request = new AmendVoyageScheduleRequest
        {
            DeparturePortId = bar.Id,
            ArrivalPortId = kotor.Id,
            PlannedDepartureUtc = DateTime.SpecifyKind(PlannedDepartureUtc, DateTimeKind.Local),
            PlannedArrivalUtc = PlannedArrivalUtc,
            Reason = "Schedule revised",
        };

        var exception = await Assert.ThrowsAsync<AmendVoyageScheduleValidationException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                request,
                CancellationToken.None));

        Assert.Contains(
            AmendVoyageScheduleValidator.PlannedDepartureUtcField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Amend_rejects_an_arrival_that_is_not_after_the_departure()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-111");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");
        var kotor = await SeedPortAsync(dbContext, "Port of Kotor");

        var request = new AmendVoyageScheduleRequest
        {
            DeparturePortId = bar.Id,
            ArrivalPortId = kotor.Id,
            PlannedDepartureUtc = PlannedArrivalUtc,
            PlannedArrivalUtc = PlannedDepartureUtc,
            Reason = "Schedule revised",
        };

        var exception = await Assert.ThrowsAsync<AmendVoyageScheduleValidationException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                request,
                CancellationToken.None));

        Assert.Contains(
            AmendVoyageScheduleValidator.PlannedArrivalUtcField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Amend_rejects_identical_departure_and_arrival_ports()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-AMD-112");
        var bar = await SeedPortAsync(dbContext, "Port of Bar");

        var exception = await Assert.ThrowsAsync<AmendVoyageScheduleValidationException>(
            () => CreateService(dbContext).AmendAsync(
                voyage.Id,
                ActingUserId,
                Request(bar.Id, bar.Id),
                CancellationToken.None));

        Assert.Contains(
            AmendVoyageScheduleValidator.ArrivalPortIdField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    private static AmendVoyageScheduleRequest Request(
        Guid departurePortId,
        Guid arrivalPortId,
        Guid? routeId = null,
        string reason = "Schedule revised")
    {
        return new AmendVoyageScheduleRequest
        {
            RouteId = routeId,
            DeparturePortId = departurePortId,
            ArrivalPortId = arrivalPortId,
            PlannedDepartureUtc = PlannedDepartureUtc.AddDays(1),
            PlannedArrivalUtc = PlannedArrivalUtc.AddDays(1),
            Reason = reason,
        };
    }

    private static AmendVoyageScheduleService CreateService(HarborOpsDbContext dbContext)
    {
        return new AmendVoyageScheduleService(
            dbContext,
            new FixedTimeProvider(new DateTimeOffset(UtcNow)));
    }

    private static async Task<Voyage> SeedPlannedVoyageAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber)
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage;
    }

    private static async Task<Port> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.SaveChangesAsync();

        return port;
    }

    private static async Task<Route> SeedRouteAsync(
        HarborOpsDbContext dbContext,
        params Guid[] portIds)
    {
        var route = Route.Create("Adriatic feeder", 560m, 2100, null);

        await dbContext.Set<Route>().AddAsync(route);
        await dbContext.Set<RoutePort>().AddRangeAsync(portIds.Select((portId, index) =>
            RoutePort.Create(route.Id, portId, index + 1, index == 0 ? 0m : 60m, index == 0 ? 0 : 300)));
        await dbContext.SaveChangesAsync();

        return route;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
