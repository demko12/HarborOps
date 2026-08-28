using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RecordStopDepartureServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Departure_records_atd_moves_stop_to_departed_and_releases_the_berth()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, berth) = await SeedArrivedStopAsync(dbContext);
        var schedule = BerthSchedule.Create(berth.Id, stop.Id, stop.Eta, stop.Etd);
        schedule.MarkOccupied();
        await dbContext.Set<BerthSchedule>().AddAsync(schedule);
        await dbContext.SaveChangesAsync();

        var actualDepartureUtc = stop.Ata!.Value.AddHours(2);
        var service = CreateService(dbContext, actualDepartureUtc.AddHours(1));

        var response = await service.RecordDepartureAsync(
            voyage.Id,
            stop.Id,
            new RecordStopDepartureRequest { ActualDepartureUtc = actualDepartureUtc },
            CancellationToken.None);

        Assert.Equal(VoyageStop.DepartedStatus, response.Status);

        var persistedStop = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stop.Id, CancellationToken.None);
        var persistedSchedule = await dbContext.Set<BerthSchedule>()
            .SingleAsync(candidate => candidate.Id == schedule.Id, CancellationToken.None);

        Assert.Equal(actualDepartureUtc, persistedStop.Atd);
        Assert.Equal(VoyageStop.DepartedStatus, persistedStop.Status);
        Assert.Equal(BerthSchedule.ReleasedStatus, persistedSchedule.Status);
    }

    [Fact]
    public async Task Departure_rejects_a_stop_that_has_not_arrived()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, _) = await SeedInProgressVoyageWithPlannedStopAsync(dbContext);
        var service = CreateService(dbContext, stop.Eta.AddHours(1));

        var exception = await Assert.ThrowsAsync<VoyageStopNotArrivedException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                stop.Id,
                new RecordStopDepartureRequest { ActualDepartureUtc = stop.Eta },
                CancellationToken.None));

        Assert.Equal(stop.Id, exception.VoyageStopId);
    }

    [Fact]
    public async Task Departure_rejects_a_timestamp_earlier_than_actual_arrival()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, _) = await SeedArrivedStopAsync(dbContext);
        var service = CreateService(dbContext, stop.Ata!.Value.AddHours(2));

        var exception = await Assert.ThrowsAsync<RecordStopDepartureValidationException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                stop.Id,
                new RecordStopDepartureRequest { ActualDepartureUtc = stop.Ata.Value.AddMinutes(-1) },
                CancellationToken.None));

        Assert.Contains(RecordStopDepartureValidator.ActualDepartureUtcField, exception.Errors);

        var persisted = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stop.Id, CancellationToken.None);
        Assert.Equal(VoyageStop.ArrivedStatus, persisted.Status);
        Assert.Null(persisted.Atd);
    }

    [Fact]
    public async Task Departure_rejects_when_voyage_is_not_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, _) = await SeedArrivedStopAsync(dbContext);
        voyage.Arrive(PlannedArrivalUtc);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, PlannedArrivalUtc.AddHours(1));

        var exception = await Assert.ThrowsAsync<VoyageNotExecutingException>(
            () => service.RecordDepartureAsync(
                voyage.Id,
                stop.Id,
                new RecordStopDepartureRequest { ActualDepartureUtc = stop.Ata!.Value.AddHours(1) },
                CancellationToken.None));

        Assert.Equal(Voyage.CompletedStatus, exception.CurrentStatus);
    }

    private static RecordStopDepartureService CreateService(
        HarborOpsDbContext dbContext,
        DateTime utcNow)
    {
        return new RecordStopDepartureService(
            dbContext,
            new FixedTimeProvider(new DateTimeOffset(utcNow)));
    }

    private static async Task<(Voyage Voyage, VoyageStop Stop, Berth Berth)>
        SeedInProgressVoyageWithPlannedStopAsync(
            HarborOpsDbContext dbContext,
            string voyageNumber = "VOY-800")
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);
        voyage.Depart(PlannedDepartureUtc);

        var port = Port.Create("Port of Bar", "Montenegro");
        var berth = Berth.Create(port.Id, "Pier 1", 200m, 10m, BerthStatus.Available);
        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            1,
            PlannedDepartureUtc.AddHours(2),
            PlannedDepartureUtc.AddHours(6));

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<Berth>().AddAsync(berth);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        return (voyage, stop, berth);
    }

    private static async Task<(Voyage Voyage, VoyageStop Stop, Berth Berth)> SeedArrivedStopAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber = "VOY-801")
    {
        var (voyage, stop, berth) = await SeedInProgressVoyageWithPlannedStopAsync(
            dbContext,
            voyageNumber);

        stop.RecordArrival(stop.Eta.AddMinutes(30));
        await dbContext.SaveChangesAsync();

        return (voyage, stop, berth);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
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
