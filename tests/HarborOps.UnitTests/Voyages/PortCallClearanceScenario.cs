using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Voyages;

internal static class PortCallClearanceScenario
{
    internal static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    internal static readonly DateTime UtcNow =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static HarborOpsDbContext CreateDbContext(
        string? databaseName = null,
        bool failOnSave = false)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        if (failOnSave)
        {
            builder.AddInterceptors(new FailingSaveChangesInterceptor());
        }

        return new HarborOpsDbContext(builder.Options);
    }

    internal static async Task<(Guid VoyageId, Guid StopId)> SeedArrivedStopWithClearanceAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber = "VOY-CLR-100")
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
        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            1,
            PlannedDepartureUtc.AddHours(2),
            PlannedDepartureUtc.AddHours(6));
        stop.RecordArrival(PlannedDepartureUtc.AddHours(2));

        var clearance = PortCallClearance.Create(voyage.Id, stop.Id);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.Set<PortCallClearance>().AddAsync(clearance);
        await dbContext.SaveChangesAsync();

        return (voyage.Id, stop.Id);
    }

    internal static async Task<PortCallClearanceItem> SeedItemAsync(
        HarborOpsDbContext dbContext,
        Guid stopId,
        string category,
        string title,
        bool isRequired,
        DateTime? dueAtUtc = null,
        string? authorityReference = null,
        Guid? evidenceDocumentId = null)
    {
        var clearance = await dbContext.Set<PortCallClearance>()
            .SingleAsync(candidate => candidate.VoyageStopId == stopId);

        var (item, recorded) = clearance.AddItem(
            category,
            title,
            isRequired,
            dueAtUtc,
            authorityReference,
            evidenceDocumentId,
            Guid.NewGuid(),
            UtcNow);

        await dbContext.Set<PortCallClearanceItem>().AddAsync(item);
        await dbContext.Set<PortCallClearanceEvent>().AddAsync(recorded);
        await dbContext.SaveChangesAsync();

        return item;
    }

    internal static async Task DepartStopAsync(HarborOpsDbContext dbContext, Guid stopId)
    {
        var stop = await dbContext.Set<VoyageStop>().SingleAsync(candidate => candidate.Id == stopId);
        stop.RecordDeparture(stop.Ata!.Value.AddHours(1));
        await dbContext.SaveChangesAsync();
    }

    internal static Task<PortCallClearance> GetClearanceAsync(
        HarborOpsDbContext dbContext,
        Guid stopId)
    {
        return dbContext.Set<PortCallClearance>()
            .SingleAsync(candidate => candidate.VoyageStopId == stopId);
    }

    internal static Task<List<PortCallClearanceEvent>> GetEventsAsync(
        HarborOpsDbContext dbContext,
        Guid clearanceId)
    {
        return dbContext.Set<PortCallClearanceEvent>()
            .AsNoTracking()
            .Where(recorded => recorded.PortCallClearanceId == clearanceId)
            .OrderBy(recorded => recorded.OccurredAtUtc)
            .ThenBy(recorded => recorded.Id)
            .ToListAsync();
    }

    internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("The save was rejected.");
        }
    }
}
