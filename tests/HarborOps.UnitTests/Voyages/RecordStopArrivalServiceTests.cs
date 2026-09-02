using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RecordStopArrivalServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Arrival_records_ata_moves_stop_to_arrived_and_occupies_the_berth()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, berth) = await SeedInProgressVoyageWithStopAsync(dbContext);
        var schedule = BerthSchedule.Create(berth.Id, stop.Id, stop.Eta, stop.Etd);
        await dbContext.Set<BerthSchedule>().AddAsync(schedule);
        await dbContext.SaveChangesAsync();

        var actualArrivalUtc = stop.Eta.AddMinutes(30);
        var service = CreateService(dbContext, actualArrivalUtc.AddHours(1));

        var response = await service.RecordArrivalAsync(
            voyage.Id,
            stop.Id,
            new RecordStopArrivalRequest { ActualArrivalUtc = actualArrivalUtc },
            CancellationToken.None);

        Assert.Equal(VoyageStop.ArrivedStatus, response.Status);

        var persistedStop = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stop.Id, CancellationToken.None);
        var persistedSchedule = await dbContext.Set<BerthSchedule>()
            .SingleAsync(candidate => candidate.Id == schedule.Id, CancellationToken.None);

        Assert.Equal(actualArrivalUtc, persistedStop.Ata);
        Assert.Equal(VoyageStop.ArrivedStatus, persistedStop.Status);
        Assert.Equal(BerthSchedule.OccupiedStatus, persistedSchedule.Status);
    }

    [Fact]
    public async Task Arrival_rejects_when_voyage_is_not_in_progress()
    {
        await using var dbContext = CreateDbContext();
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            "VOY-700",
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);
        var port = Port.Create("Port of Bar", "Montenegro");
        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            1,
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddHours(4));

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, PlannedDepartureUtc.AddHours(1));

        var exception = await Assert.ThrowsAsync<VoyageNotExecutingException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                stop.Id,
                new RecordStopArrivalRequest { ActualArrivalUtc = PlannedDepartureUtc },
                CancellationToken.None));

        Assert.Equal(Voyage.PlannedStatus, exception.CurrentStatus);
    }

    [Fact]
    public async Task Arrival_rejects_out_of_sequence_stop()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, firstStop, _) = await SeedInProgressVoyageWithStopAsync(dbContext);
        var secondStop = VoyageStop.Create(
            voyage.Id,
            firstStop.PortId,
            2,
            firstStop.Etd.AddHours(4),
            firstStop.Etd.AddHours(8));
        await dbContext.Set<VoyageStop>().AddAsync(secondStop);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, secondStop.Etd.AddHours(1));

        var exception = await Assert.ThrowsAsync<VoyageStopOutOfSequenceException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                secondStop.Id,
                new RecordStopArrivalRequest { ActualArrivalUtc = secondStop.Eta },
                CancellationToken.None));

        Assert.Equal(secondStop.Id, exception.VoyageStopId);

        var persisted = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == secondStop.Id, CancellationToken.None);
        Assert.Equal(VoyageStop.PlannedStatus, persisted.Status);
    }

    [Fact]
    public async Task Arrival_rejects_a_second_arrival_for_the_same_stop()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, _) = await SeedInProgressVoyageWithStopAsync(dbContext);
        var actualArrivalUtc = stop.Eta;
        var service = CreateService(dbContext, actualArrivalUtc.AddHours(2));
        var request = new RecordStopArrivalRequest { ActualArrivalUtc = actualArrivalUtc };

        await service.RecordArrivalAsync(voyage.Id, stop.Id, request, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<VoyageStopNotPlannedException>(
            () => service.RecordArrivalAsync(voyage.Id, stop.Id, request, CancellationToken.None));

        Assert.Equal(stop.Id, exception.VoyageStopId);
    }

    [Fact]
    public async Task Arrival_rejects_future_timestamp()
    {
        await using var dbContext = CreateDbContext();
        var (voyage, stop, _) = await SeedInProgressVoyageWithStopAsync(dbContext);
        var utcNow = stop.Eta;
        var service = CreateService(dbContext, utcNow);

        var exception = await Assert.ThrowsAsync<RecordStopArrivalValidationException>(
            () => service.RecordArrivalAsync(
                voyage.Id,
                stop.Id,
                new RecordStopArrivalRequest { ActualArrivalUtc = utcNow.AddMinutes(1) },
                CancellationToken.None));

        Assert.Contains(RecordStopArrivalValidator.ActualArrivalUtcField, exception.Errors);
    }

    private static RecordStopArrivalService CreateService(HarborOpsDbContext dbContext, DateTime utcNow)
    {
        return new RecordStopArrivalService(dbContext, new FixedTimeProvider(new DateTimeOffset(utcNow)));
    }

    private static async Task<(Voyage Voyage, VoyageStop Stop, Berth Berth)>
        SeedInProgressVoyageWithStopAsync(HarborOpsDbContext dbContext, string voyageNumber = "VOY-701")
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
