using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class AssignBerthServiceTests
{
    private static readonly DateTime Eta = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Etd = new(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Assign_reserves_the_berth_for_the_stop_window()
    {
        await using var dbContext = CreateDbContext();
        var (stop, berth) = await SeedStopAndBerthAsync(dbContext);
        var service = new AssignBerthService(dbContext);

        var response = await service.AssignAsync(
            stop.VoyageId,
            stop.Id,
            new AssignBerthToStopRequest { BerthId = berth.Id },
            CancellationToken.None);

        Assert.Equal(berth.Id, response.BerthId);
        Assert.Equal(BerthSchedule.ReservedStatus, response.ScheduleStatus);

        var persistedStop = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stop.Id, CancellationToken.None);

        Assert.Equal(berth.Id, persistedStop.BerthId);
    }

    [Fact]
    public async Task Assign_rejects_overlapping_active_schedule_on_the_same_berth()
    {
        await using var dbContext = CreateDbContext();
        var (stopOne, berth) = await SeedStopAndBerthAsync(dbContext, "STOP-A");
        var stopTwo = await SeedAdditionalStopAsync(
            dbContext,
            stopOne.VoyageId,
            stopOne.PortId,
            2,
            Eta.AddHours(2),
            Etd.AddHours(2));
        var service = new AssignBerthService(dbContext);

        await service.AssignAsync(
            stopOne.VoyageId,
            stopOne.Id,
            new AssignBerthToStopRequest { BerthId = berth.Id },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BerthOverlapException>(
            () => service.AssignAsync(
                stopTwo.VoyageId,
                stopTwo.Id,
                new AssignBerthToStopRequest { BerthId = berth.Id },
                CancellationToken.None));

        Assert.Equal(berth.Id, exception.BerthId);

        var persistedStopTwo = await dbContext.Set<VoyageStop>()
            .SingleAsync(candidate => candidate.Id == stopTwo.Id, CancellationToken.None);

        Assert.Null(persistedStopTwo.BerthId);
    }

    [Fact]
    public async Task Assign_allows_a_non_overlapping_window_on_the_same_berth()
    {
        await using var dbContext = CreateDbContext();
        var (stopOne, berth) = await SeedStopAndBerthAsync(dbContext, "STOP-A");
        var stopTwo = await SeedAdditionalStopAsync(
            dbContext,
            stopOne.VoyageId,
            stopOne.PortId,
            2,
            Etd.AddHours(1),
            Etd.AddHours(5));
        var service = new AssignBerthService(dbContext);

        await service.AssignAsync(
            stopOne.VoyageId,
            stopOne.Id,
            new AssignBerthToStopRequest { BerthId = berth.Id },
            CancellationToken.None);

        var response = await service.AssignAsync(
            stopTwo.VoyageId,
            stopTwo.Id,
            new AssignBerthToStopRequest { BerthId = berth.Id },
            CancellationToken.None);

        Assert.Equal(berth.Id, response.BerthId);
    }

    [Fact]
    public async Task Assign_rejects_a_berth_belonging_to_a_different_port()
    {
        await using var dbContext = CreateDbContext();
        var (stop, _) = await SeedStopAndBerthAsync(dbContext);
        var otherPort = Port.Create("Other port", "Italy");
        await dbContext.Set<Port>().AddAsync(otherPort);
        var otherBerth = Berth.Create(otherPort.Id, "Pier 9", 200m, 10m, BerthStatus.Available);
        await dbContext.Set<Berth>().AddAsync(otherBerth);
        await dbContext.SaveChangesAsync();

        var service = new AssignBerthService(dbContext);

        var exception = await Assert.ThrowsAsync<BerthPortMismatchException>(
            () => service.AssignAsync(
                stop.VoyageId,
                stop.Id,
                new AssignBerthToStopRequest { BerthId = otherBerth.Id },
                CancellationToken.None));

        Assert.Equal(otherBerth.Id, exception.BerthId);
    }

    [Fact]
    public async Task Assign_rejects_unknown_berth()
    {
        await using var dbContext = CreateDbContext();
        var (stop, _) = await SeedStopAndBerthAsync(dbContext);
        var service = new AssignBerthService(dbContext);
        var unknownBerthId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<BerthNotFoundException>(
            () => service.AssignAsync(
                stop.VoyageId,
                stop.Id,
                new AssignBerthToStopRequest { BerthId = unknownBerthId },
                CancellationToken.None));

        Assert.Equal(unknownBerthId, exception.BerthId);
    }

    [Fact]
    public async Task Assign_rejects_a_stop_that_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var (stop, berth) = await SeedStopAndBerthAsync(dbContext);
        stop.RecordArrival(Eta);
        await dbContext.SaveChangesAsync();

        var service = new AssignBerthService(dbContext);

        var exception = await Assert.ThrowsAsync<VoyageStopNotPlannedException>(
            () => service.AssignAsync(
                stop.VoyageId,
                stop.Id,
                new AssignBerthToStopRequest { BerthId = berth.Id },
                CancellationToken.None));

        Assert.Equal(stop.Id, exception.VoyageStopId);
    }

    private static async Task<(VoyageStop Stop, Berth Berth)> SeedStopAndBerthAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber = "VOY-600")
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            Eta.AddHours(-4),
            Etd.AddDays(3));
        var port = Port.Create("Port of Bar", "Montenegro");
        var berth = Berth.Create(port.Id, "Pier 1", 200m, 10m, BerthStatus.Available);
        var stop = VoyageStop.Create(voyage.Id, port.Id, 1, Eta, Etd);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<Berth>().AddAsync(berth);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        return (stop, berth);
    }

    private static async Task<VoyageStop> SeedAdditionalStopAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId,
        Guid portId,
        int stopOrder,
        DateTime eta,
        DateTime etd)
    {
        var stop = VoyageStop.Create(voyageId, portId, stopOrder, eta, etd);

        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        return stop;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
