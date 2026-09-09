using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

internal sealed class VoyageDelayImpactScenario
{
    internal const string DefaultReason = "Downstream schedule revised";

    internal static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    internal static readonly DateTime UtcNow = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    internal static readonly Guid ActorId = Guid.Parse("2f2ba1a9-6f0e-4a4d-93a1-3f6f2b1d0c77");

    private VoyageDelayImpactScenario(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        IReadOnlyList<VoyageStop> stops,
        IReadOnlyList<Port> ports)
    {
        DbContext = dbContext;
        Voyage = voyage;
        Stops = stops;
        Ports = ports;
    }

    internal HarborOpsDbContext DbContext { get; }

    internal Voyage Voyage { get; }

    internal IReadOnlyList<VoyageStop> Stops { get; }

    internal IReadOnlyList<Port> Ports { get; }

    internal static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    internal DateTime CurrentUtc { get; private set; } = UtcNow;

    internal void Advance(TimeSpan elapsed)
    {
        CurrentUtc = CurrentUtc.Add(elapsed);
    }

    internal TimeProvider CreateTimeProvider()
    {
        return new MovableTimeProvider(() => new DateTimeOffset(CurrentUtc));
    }

    internal static async Task<VoyageDelayImpactScenario> CreateAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber = "VOY-IMPACT-1")
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        var ports = new[]
        {
            Port.Create("Port of Bar", "Montenegro"),
            Port.Create("Port of Kotor", "Montenegro"),
            Port.Create("Genoa terminal", "Italy"),
        };

        var stops = new[]
        {
            VoyageStop.Create(
                voyage.Id,
                ports[0].Id,
                1,
                new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc)),
            VoyageStop.Create(
                voyage.Id,
                ports[1].Id,
                2,
                new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)),
            VoyageStop.Create(
                voyage.Id,
                ports[2].Id,
                3,
                new DateTime(2026, 9, 3, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc)),
        };

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddRangeAsync(ports);
        await dbContext.Set<VoyageStop>().AddRangeAsync(stops);
        await dbContext.SaveChangesAsync();

        return new VoyageDelayImpactScenario(dbContext, voyage, stops, ports);
    }

    internal async Task<Delay> AddDelayAsync(int durationMinutes, Guid? voyageStopId = null)
    {
        var delay = Delay.Create(
            Voyage.Id,
            "Port congestion",
            durationMinutes,
            UtcNow,
            voyageStopId);

        await DbContext.Set<Delay>().AddAsync(delay);
        await DbContext.SaveChangesAsync();

        return delay;
    }

    internal async Task<Berth> ReserveBerthAsync(
        VoyageStop stop,
        Guid otherVoyageStopId,
        DateTime startTime,
        DateTime endTime)
    {
        var berth = Berth.Create(stop.PortId, "Berth 1", 200m, 12m, BerthStatus.Available);
        var schedule = BerthSchedule.Create(berth.Id, otherVoyageStopId, startTime, endTime);

        stop.AssignBerth(berth.Id);

        await DbContext.Set<Berth>().AddAsync(berth);
        await DbContext.Set<BerthSchedule>().AddAsync(schedule);
        await DbContext.SaveChangesAsync();

        return berth;
    }

    internal Task<DelayImpactPreviewResponse> PreviewAsync(Delay delay)
    {
        return new DelayImpactPreviewService(DbContext).PreviewAsync(
            Voyage.Id,
            delay.Id,
            CancellationToken.None);
    }

    internal async Task<ApplyDelayImpactResponse> ApplyProposedImpactAsync(
        Delay delay,
        Guid? actingUserId = null,
        string reason = DefaultReason)
    {
        var preview = await PreviewAsync(delay);

        return await CreateApplyService().ApplyAsync(
            Voyage.Id,
            delay.Id,
            actingUserId ?? ActorId,
            ToApplyRequest(preview, reason),
            CancellationToken.None);
    }

    internal ApplyDelayImpactService CreateApplyService()
    {
        return new ApplyDelayImpactService(DbContext, CreateTimeProvider());
    }

    internal static ApplyDelayImpactRequest ToApplyRequest(
        DelayImpactPreviewResponse preview,
        string reason = DefaultReason)
    {
        return new ApplyDelayImpactRequest
        {
            Reason = reason,
            Stops = preview.Stops
                .Select(stop => new ApplyDelayImpactStopRequest
                {
                    VoyageStopId = stop.StopId,
                    ExpectedEta = stop.CurrentEta,
                    ExpectedEtd = stop.CurrentEtd,
                    Eta = stop.ProposedEta,
                    Etd = stop.ProposedEtd,
                })
                .ToList(),
        };
    }

    private sealed class MovableTimeProvider(Func<DateTimeOffset> utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow();
        }
    }
}
