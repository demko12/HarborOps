using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetVoyageScheduleChangesQueryTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime ChangedAtUtc = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ActingUserId =
        new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Query_projects_the_current_schedule_with_port_and_route_names()
    {
        await using var dbContext = CreateDbContext();
        var bar = Port.Create("Port of Bar", "Montenegro");
        var kotor = Port.Create("Port of Kotor", "Montenegro");
        var route = Route.Create("Adriatic feeder", 560m, 2100, null);
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-100");

        await dbContext.Set<Port>().AddRangeAsync(bar, kotor);
        await dbContext.Set<Route>().AddAsync(route);
        await dbContext.SaveChangesAsync();

        voyage.Amend(
            route.Id,
            bar.Id,
            kotor.Id,
            PlannedDepartureUtc.AddDays(1),
            PlannedArrivalUtc.AddDays(1));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetScheduleChangesAsync(voyage.Id, CancellationToken.None);

        Assert.Equal(voyage.Id, response.Voyage.VoyageId);
        Assert.Equal("VOY-SCH-100", response.Voyage.VoyageNumber);
        Assert.Equal(Voyage.PlannedStatus, response.Voyage.Status);
        Assert.Equal(route.Id, response.Voyage.RouteId);
        Assert.Equal("Adriatic feeder", response.Voyage.RouteName);
        Assert.Equal("Port of Bar", response.Voyage.DeparturePortName);
        Assert.Equal("Port of Kotor", response.Voyage.ArrivalPortName);
        Assert.Equal(PlannedDepartureUtc.AddDays(1), response.Voyage.PlannedDeparture);
        Assert.Empty(response.Changes);
    }

    [Fact]
    public async Task Query_projects_change_values_with_the_referenced_port_names()
    {
        await using var dbContext = CreateDbContext();
        var bar = Port.Create("Port of Bar", "Montenegro");
        var kotor = Port.Create("Port of Kotor", "Montenegro");
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-101");

        await dbContext.Set<Port>().AddRangeAsync(bar, kotor);
        await dbContext.Set<VoyageScheduleChange>().AddAsync(VoyageScheduleChange.RecordAmendment(
            voyage.Id,
            "Berth window moved",
            ActingUserId,
            ChangedAtUtc,
            Voyage.PlannedStatus,
            null,
            null,
            null,
            bar.Id,
            null,
            kotor.Id,
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddDays(1),
            PlannedArrivalUtc,
            PlannedArrivalUtc.AddDays(1)));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetScheduleChangesAsync(voyage.Id, CancellationToken.None);

        var change = Assert.Single(response.Changes);
        Assert.Equal(VoyageScheduleChange.AmendedChangeType, change.ChangeType);
        Assert.Equal("Berth window moved", change.Reason);
        Assert.Equal(ActingUserId, change.ChangedByUserId);
        Assert.Equal(ChangedAtUtc, change.ChangedAtUtc);
        Assert.Null(change.PreviousDeparturePortId);
        Assert.Null(change.PreviousDeparturePortName);
        Assert.Equal(bar.Id, change.NewDeparturePortId);
        Assert.Equal("Port of Bar", change.NewDeparturePortName);
        Assert.Equal(kotor.Id, change.NewArrivalPortId);
        Assert.Equal("Port of Kotor", change.NewArrivalPortName);
    }

    [Fact]
    public async Task Query_returns_changes_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-102");

        await dbContext.Set<VoyageScheduleChange>().AddRangeAsync(
            Amendment(voyage.Id, "First revision", ChangedAtUtc),
            Amendment(voyage.Id, "Second revision", ChangedAtUtc.AddHours(2)),
            Amendment(voyage.Id, "Third revision", ChangedAtUtc.AddHours(4)));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetScheduleChangesAsync(voyage.Id, CancellationToken.None);

        Assert.Equal(
            ["Third revision", "Second revision", "First revision"],
            response.Changes.Select(change => change.Reason));
    }

    [Fact]
    public async Task Query_orders_changes_with_the_same_timestamp_deterministically()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-103");
        var changes = new[]
        {
            Amendment(voyage.Id, "Revision A", ChangedAtUtc),
            Amendment(voyage.Id, "Revision B", ChangedAtUtc),
            Amendment(voyage.Id, "Revision C", ChangedAtUtc),
        };

        await dbContext.Set<VoyageScheduleChange>().AddRangeAsync(changes);
        await dbContext.SaveChangesAsync();

        var query = CreateQuery(dbContext);
        var first = await query.GetScheduleChangesAsync(voyage.Id, CancellationToken.None);
        var second = await query.GetScheduleChangesAsync(voyage.Id, CancellationToken.None);

        var expected = changes.OrderByDescending(change => change.Id).Select(change => change.Id);

        Assert.Equal(expected, first.Changes.Select(change => change.ChangeId));
        Assert.Equal(
            first.Changes.Select(change => change.ChangeId),
            second.Changes.Select(change => change.ChangeId));
    }

    [Fact]
    public async Task Query_excludes_changes_recorded_for_another_voyage()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-104");
        var otherVoyage = await SeedPlannedVoyageAsync(dbContext, "VOY-SCH-105");

        await dbContext.Set<VoyageScheduleChange>().AddRangeAsync(
            Amendment(voyage.Id, "Mine", ChangedAtUtc),
            Amendment(otherVoyage.Id, "Theirs", ChangedAtUtc));
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext)
            .GetScheduleChangesAsync(voyage.Id, CancellationToken.None);

        var change = Assert.Single(response.Changes);
        Assert.Equal("Mine", change.Reason);
    }

    [Fact]
    public async Task Query_throws_for_an_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => CreateQuery(dbContext).GetScheduleChangesAsync(
                unknownVoyageId,
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    private static VoyageScheduleChange Amendment(Guid voyageId, string reason, DateTime changedAtUtc)
    {
        return VoyageScheduleChange.RecordAmendment(
            voyageId,
            reason,
            ActingUserId,
            changedAtUtc,
            Voyage.PlannedStatus,
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddDays(1),
            PlannedArrivalUtc,
            PlannedArrivalUtc.AddDays(1));
    }

    private static IGetVoyageScheduleChangesQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetVoyageScheduleChangesQuery(dbContext);
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
