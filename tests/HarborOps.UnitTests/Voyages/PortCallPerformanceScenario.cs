using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.UnitTests.Voyages;

internal static class PortCallPerformanceScenario
{
    internal static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal const string ActorName = "reports@harborops.test";

    internal static HarborOpsDbContext CreateDbContext() => PortCallClearanceScenario.CreateDbContext();

    internal static ReportsPortCallPerformanceQuery Query(HarborOpsDbContext dbContext, int? maxStops = null) =>
        maxStops is { } limit
            ? new ReportsPortCallPerformanceQuery(dbContext, limit)
            : new ReportsPortCallPerformanceQuery(dbContext);

    internal static ReportsPortCallPerformanceRequest Request(DateOnly from, DateOnly to) =>
        new() { DepartedFrom = from, DepartedTo = to, PageSize = 100 };

    internal static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, DateTimeKind.Utc);

    internal static async Task<Port> SeedPortAsync(HarborOpsDbContext dbContext, string name, string unLocode)
    {
        var port = Port.Create(name, "Country", unLocode: unLocode);
        await dbContext.Set<Port>().AddAsync(port);
        await dbContext.SaveChangesAsync();

        return port;
    }

    internal static async Task<Voyage> SeedVoyageAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber,
        Guid? vesselId = null)
    {
        var voyage = Voyage.Schedule(
            vesselId ?? Guid.NewGuid(),
            voyageNumber,
            "NLRTM",
            "DEHAM",
            Utc(8, 1, 0),
            Utc(12, 31, 0));
        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage;
    }

    internal static async Task<VoyageStop> SeedStopAsync(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        Port port,
        int stopOrder,
        DateTime actualArrivalUtc,
        DateTime? actualDepartureUtc)
    {
        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            stopOrder,
            actualArrivalUtc,
            actualDepartureUtc ?? actualArrivalUtc.AddHours(12));
        stop.RecordArrival(actualArrivalUtc);

        if (actualDepartureUtc is { } departure)
        {
            stop.RecordDeparture(departure);
        }

        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        return stop;
    }

    internal static async Task<PortCallStatement> FinalizeAsync(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        VoyageStop stop,
        Port port,
        string? berthName,
        DateTime finalizedAtUtc,
        params PortCallLogEntry[] entries)
    {
        var statement = PortCallStatement.CreateDraft(voyage.Id, stop.Id, ActorId, finalizedAtUtc.AddHours(-1));
        var snapshot = new PortCallStatementSnapshot(
            voyage.VoyageNumber,
            stop.StopOrder,
            port.Name,
            port.UnLocode,
            port.TimeZone,
            berthName,
            stop.Ata!.Value,
            stop.Atd!.Value,
            new string('B', 64),
            [
                new PortCallLogEntry(
                    PortCallLogSource.StopArrival, stop.Id, null, 0, "Actual arrival", stop.Ata.Value, null, null, null),
                .. entries,
                new PortCallLogEntry(
                    PortCallLogSource.StopDeparture, stop.Id, null, 0, "Actual departure", stop.Atd.Value, null, null, null),
            ]);

        var capturedEntries = statement.Finalize(snapshot, ActorId, ActorName, finalizedAtUtc);

        await dbContext.Set<PortCallStatement>().AddAsync(statement);
        await dbContext.Set<PortCallStatementEntry>().AddRangeAsync(capturedEntries);
        await dbContext.SaveChangesAsync();

        return statement;
    }

    internal static async Task<PortCallStatement> SeedDraftAsync(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        VoyageStop stop)
    {
        var statement = PortCallStatement.CreateDraft(voyage.Id, stop.Id, ActorId, stop.Atd!.Value.AddHours(1));
        await dbContext.Set<PortCallStatement>().AddAsync(statement);
        await dbContext.SaveChangesAsync();

        return statement;
    }

    internal static PortCallLogEntry Milestone(string activityType, DateTime occurredAtUtc) =>
        new(PortCallLogSource.ManualActivity, Guid.NewGuid(), 1, 0, activityType, occurredAtUtc, null, null, null);

    internal static PortCallLogEntry DelayEntry(string reason, DateTime createdAtUtc, int durationMinutes) =>
        new(PortCallLogSource.Delay, Guid.NewGuid(), null, 0, reason, createdAtUtc, null, null, durationMinutes);
}
