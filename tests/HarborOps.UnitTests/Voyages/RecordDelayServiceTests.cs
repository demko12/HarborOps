using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class RecordDelayServiceTests
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime UtcNow = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Record_creates_a_voyage_level_delay_with_server_derived_time()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedVoyageAsync(dbContext);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            voyage.Id,
            new RecordDelayRequest { Reason = "Port congestion", Duration = 120 },
            CancellationToken.None);

        Assert.Equal(voyage.Id, response.VoyageId);
        Assert.Null(response.VoyageStopId);
        Assert.Equal(UtcNow, response.CreatedAt);

        var persisted = await dbContext.Set<Delay>()
            .SingleAsync(candidate => candidate.Id == response.DelayId, CancellationToken.None);

        Assert.Equal("Port congestion", persisted.Reason);
        Assert.Equal(120, persisted.Duration);
        Assert.Equal(UtcNow, persisted.CreatedAt);
    }

    [Fact]
    public async Task Record_creates_a_stop_level_delay()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedVoyageAsync(dbContext);
        var port = Port.Create("Port of Bar", "Montenegro");
        var stop = VoyageStop.Create(voyage.Id, port.Id, 1, PlannedDepartureUtc, PlannedDepartureUtc.AddHours(4));
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            voyage.Id,
            new RecordDelayRequest
            {
                Reason = "Berth unavailable",
                Duration = 45,
                Description = "Waiting for tug assistance",
                VoyageStopId = stop.Id,
            },
            CancellationToken.None);

        Assert.Equal(stop.Id, response.VoyageStopId);
        Assert.Equal("Waiting for tug assistance", response.Description);
    }

    [Fact]
    public async Task Record_rejects_missing_reason_and_non_positive_duration()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedVoyageAsync(dbContext);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordDelayValidationException>(
            () => service.RecordAsync(
                voyage.Id,
                new RecordDelayRequest { Reason = string.Empty, Duration = 0 },
                CancellationToken.None));

        Assert.Contains(RecordDelayValidator.ReasonField, exception.Errors);
        Assert.Contains(RecordDelayValidator.DurationField, exception.Errors);
    }

    [Fact]
    public async Task Record_rejects_unknown_voyage()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var unknownVoyageId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VoyageNotFoundException>(
            () => service.RecordAsync(
                unknownVoyageId,
                new RecordDelayRequest { Reason = "Weather", Duration = 30 },
                CancellationToken.None));

        Assert.Equal(unknownVoyageId, exception.VoyageId);
    }

    [Fact]
    public async Task Record_rejects_a_stop_that_does_not_belong_to_the_voyage()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedVoyageAsync(dbContext);
        var otherVoyage = await SeedVoyageAsync(dbContext, "VOY-901");
        var port = Port.Create("Port of Bar", "Montenegro");
        var foreignStop = VoyageStop.Create(
            otherVoyage.Id,
            port.Id,
            1,
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddHours(4));
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<VoyageStop>().AddAsync(foreignStop);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<VoyageStopNotFoundException>(
            () => service.RecordAsync(
                voyage.Id,
                new RecordDelayRequest
                {
                    Reason = "Weather",
                    Duration = 30,
                    VoyageStopId = foreignStop.Id,
                },
                CancellationToken.None));

        Assert.Equal(foreignStop.Id, exception.VoyageStopId);
    }

    private static RecordDelayService CreateService(HarborOpsDbContext dbContext)
    {
        return new RecordDelayService(dbContext, new FixedTimeProvider(new DateTimeOffset(UtcNow)));
    }

    private static async Task<Voyage> SeedVoyageAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber = "VOY-900")
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
