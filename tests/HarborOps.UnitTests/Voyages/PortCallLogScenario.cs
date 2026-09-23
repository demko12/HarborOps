using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

internal static class PortCallLogScenario
{
    internal static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal const string ActorName = "operator@harborops.test";

    internal static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTime ActualArrivalUtc =
        new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTime ActualDepartureUtc =
        new(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTimeOffset UtcNow =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    internal static HarborOpsDbContext CreateDbContext(string? databaseName = null) =>
        PortCallClearanceScenario.CreateDbContext(databaseName);

    internal static TimeProvider Clock() => new PortCallClearanceScenario.FixedTimeProvider(UtcNow);

    internal static async Task<(Guid VoyageId, Guid StopId)> SeedStopAsync(
        HarborOpsDbContext dbContext,
        bool arrived = true,
        bool departed = false,
        string voyageNumber = "VOY-LOG-100",
        string portName = "Port of Bar",
        string? berthName = "Berth 3",
        string? timeZone = null)
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MEBAR",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedDepartureUtc.AddDays(3));
        voyage.Depart(PlannedDepartureUtc);

        var port = Port.Create(portName, "Montenegro", unLocode: "MEBAR", timeZone: timeZone);
        var berth = berthName is null ? null : Berth.Create(port.Id, berthName, 250m, 12m, BerthStatus.Available);

        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            1,
            ActualArrivalUtc,
            ActualDepartureUtc);

        if (berth is not null)
        {
            stop.AssignBerth(berth.Id);
        }

        if (arrived || departed)
        {
            stop.RecordArrival(ActualArrivalUtc);
        }

        if (departed)
        {
            stop.RecordDeparture(ActualDepartureUtc);
        }

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.Set<Port>().AddAsync(port);

        if (berth is not null)
        {
            await dbContext.Set<Berth>().AddAsync(berth);
        }

        await dbContext.Set<VoyageStop>().AddAsync(stop);
        await dbContext.SaveChangesAsync();

        return (voyage.Id, stop.Id);
    }

    internal static async Task DepartAsync(HarborOpsDbContext dbContext, Guid stopId)
    {
        var stop = await dbContext.Set<VoyageStop>().SingleAsync(candidate => candidate.Id == stopId);
        stop.RecordDeparture(ActualDepartureUtc);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    internal static async Task<PortCallActivityChangeResponse> RecordAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId,
        Guid stopId,
        string activityType,
        DateTimeOffset occurredAt,
        DateTimeOffset? endedAt = null,
        string? notes = null)
    {
        var response = await new RecordPortCallActivityService(dbContext, Clock()).RecordAsync(
            voyageId,
            stopId,
            new RecordPortCallActivityRequest
            {
                ActivityType = activityType,
                OccurredAt = occurredAt,
                EndedAt = endedAt,
                Notes = notes,
            },
            ActorId,
            ActorName,
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        return response;
    }

    internal static async Task<Delay> SeedDelayAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId,
        Guid stopId,
        string reason,
        DateTime createdAtUtc,
        int durationMinutes = 45)
    {
        var delay = Delay.Create(voyageId, reason, durationMinutes, createdAtUtc, stopId, "Waiting for tug");
        await dbContext.Set<Delay>().AddAsync(delay);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return delay;
    }

    internal static async Task<PortCallStatementResponse> CreateStatementAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId,
        Guid stopId)
    {
        await new CreatePortCallStatementService(dbContext, Clock()).CreateAsync(
            voyageId,
            stopId,
            ActorId,
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        return await StatementQuery(dbContext).GetAsync(voyageId, stopId, CancellationToken.None);
    }

    internal static async Task<PortCallStatementResponse> FinalizeAsync(
        HarborOpsDbContext dbContext,
        Guid voyageId,
        Guid stopId,
        PortCallStatementResponse preview)
    {
        await new FinalizePortCallStatementService(dbContext, Clock()).FinalizeAsync(
            voyageId,
            stopId,
            new FinalizePortCallStatementRequest
            {
                Version = preview.Version,
                SourceVersion = preview.SourceVersion,
            },
            ActorId,
            ActorName,
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        return await StatementQuery(dbContext).GetAsync(voyageId, stopId, CancellationToken.None);
    }

    internal static GetPortCallStatementQuery StatementQuery(HarborOpsDbContext dbContext) =>
        new(dbContext);

    internal static GetPortCallActivitiesQuery ActivitiesQuery(HarborOpsDbContext dbContext) =>
        new(dbContext);

    internal static DateTimeOffset At(DateTime utc, int offsetHours = 0) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(offsetHours));
}
