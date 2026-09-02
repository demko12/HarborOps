using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class CancelVoyageServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime UtcNow = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private static readonly Guid ActingUserId =
        new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Cancel_sets_the_cancelled_status_and_records_the_actor_and_server_time()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-CNL-100");

        var response = await CreateService(dbContext).CancelAsync(
            voyage.Id,
            ActingUserId,
            new CancelVoyageRequest { Reason = " Charter withdrawn " },
            CancellationToken.None);

        Assert.Equal(Voyage.CancelledStatus, response.Voyage.Status);

        var change = Assert.Single(response.Changes);
        Assert.Equal(VoyageScheduleChange.CancelledChangeType, change.ChangeType);
        Assert.Equal("Charter withdrawn", change.Reason);
        Assert.Equal(Voyage.PlannedStatus, change.PreviousStatus);
        Assert.Equal(Voyage.CancelledStatus, change.NewStatus);
        Assert.Equal(ActingUserId, change.ChangedByUserId);
        Assert.Equal(UtcNow, change.ChangedAtUtc);
        Assert.Equal(PlannedDepartureUtc, change.PreviousPlannedDeparture);
        Assert.Equal(PlannedDepartureUtc, change.NewPlannedDeparture);
    }

    [Fact]
    public async Task Cancel_persists_the_status_and_the_history_entry_together()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid voyageId;

        await using (var dbContext = CreateDbContext(databaseName))
        {
            var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-CNL-101");
            voyageId = voyage.Id;

            await CreateService(dbContext).CancelAsync(
                voyageId,
                ActingUserId,
                new CancelVoyageRequest { Reason = "Charter withdrawn" },
                CancellationToken.None);
        }

        await using var verificationContext = CreateDbContext(databaseName);
        var storedVoyage = await verificationContext.Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyageId, CancellationToken.None);
        var storedChange = await verificationContext.Set<VoyageScheduleChange>()
            .SingleAsync(candidate => candidate.VoyageId == voyageId, CancellationToken.None);

        Assert.Equal(Voyage.CancelledStatus, storedVoyage.Status);
        Assert.Equal(VoyageScheduleChange.CancelledChangeType, storedChange.ChangeType);
    }

    [Fact]
    public async Task Cancel_preserves_the_voyage_itinerary_legs_and_delays()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-CNL-102");
        var bar = Port.Create("Port of Bar", "Montenegro");
        var kotor = Port.Create("Port of Kotor", "Montenegro");
        var berth = Berth.Create(bar.Id, "Pier 1", 200m, 10m, BerthStatus.Available);
        var firstStop = VoyageStop.Create(
            voyage.Id,
            bar.Id,
            1,
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddHours(4));
        var secondStop = VoyageStop.Create(
            voyage.Id,
            kotor.Id,
            2,
            PlannedDepartureUtc.AddHours(10),
            PlannedDepartureUtc.AddHours(14));
        firstStop.AssignBerth(berth.Id);

        await dbContext.Set<Port>().AddRangeAsync(bar, kotor);
        await dbContext.Set<Berth>().AddAsync(berth);
        await dbContext.Set<VoyageStop>().AddRangeAsync(firstStop, secondStop);
        await dbContext.Set<VoyageLeg>().AddAsync(
            VoyageLeg.Create(voyage.Id, firstStop.Id, secondStop.Id, 60m, 300));
        await dbContext.Set<Delay>().AddAsync(
            Delay.Create(voyage.Id, "Port congestion", 90, UtcNow));
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).CancelAsync(
            voyage.Id,
            ActingUserId,
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        var storedVoyage = await dbContext.Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyage.Id, CancellationToken.None);
        var stops = await dbContext.Set<VoyageStop>()
            .Where(stop => stop.VoyageId == voyage.Id)
            .ToListAsync(CancellationToken.None);
        var legCount = await dbContext.Set<VoyageLeg>()
            .CountAsync(leg => leg.VoyageId == voyage.Id, CancellationToken.None);
        var delayCount = await dbContext.Set<Delay>()
            .CountAsync(delay => delay.VoyageId == voyage.Id, CancellationToken.None);

        Assert.Equal(Voyage.CancelledStatus, storedVoyage.Status);
        Assert.Equal(2, stops.Count);
        Assert.Equal(berth.Id, stops.Single(stop => stop.StopOrder == 1).BerthId);
        Assert.Equal(1, legCount);
        Assert.Equal(1, delayCount);
    }

    [Fact]
    public async Task Cancel_rejects_a_voyage_that_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-CNL-103");
        voyage.Depart(PlannedDepartureUtc);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<VoyageScheduleNotAmendableException>(
            () => CreateService(dbContext).CancelAsync(
                voyage.Id,
                ActingUserId,
                new CancelVoyageRequest { Reason = "Charter withdrawn" },
                CancellationToken.None));

        Assert.Equal(Voyage.InProgressStatus, exception.CurrentStatus);

        var changeCount = await dbContext.Set<VoyageScheduleChange>()
            .CountAsync(change => change.VoyageId == voyage.Id, CancellationToken.None);

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public async Task Cancel_rejects_a_voyage_that_is_already_cancelled()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, "VOY-CNL-104");
        var service = CreateService(dbContext);

        await service.CancelAsync(
            voyage.Id,
            ActingUserId,
            new CancelVoyageRequest { Reason = "Charter withdrawn" },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageScheduleNotAmendableException>(
            () => service.CancelAsync(
                voyage.Id,
                ActingUserId,
                new CancelVoyageRequest { Reason = "Charter withdrawn again" },
                CancellationToken.None));

        Assert.Equal(Voyage.CancelledStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Cancel_rejects_an_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => CreateService(dbContext).CancelAsync(
                unknownVoyageId,
                ActingUserId,
                new CancelVoyageRequest { Reason = "Charter withdrawn" },
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    [Theory]
    [InlineData("VOY-CNL-105", "")]
    [InlineData("VOY-CNL-106", "   ")]
    public async Task Cancel_requires_a_reason(string voyageNumber, string reason)
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedPlannedVoyageAsync(dbContext, voyageNumber);

        var exception = await Assert.ThrowsAsync<CancelVoyageValidationException>(
            () => CreateService(dbContext).CancelAsync(
                voyage.Id,
                ActingUserId,
                new CancelVoyageRequest { Reason = reason },
                CancellationToken.None));

        Assert.Contains(
            CancelVoyageValidator.ReasonField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    private static CancelVoyageService CreateService(HarborOpsDbContext dbContext)
    {
        return new CancelVoyageService(
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
