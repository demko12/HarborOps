using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed record PortCallLogSources(
    VoyageStop Stop,
    string VoyageNumber,
    string PortName,
    string? PortUnLocode,
    string? PortTimeZone,
    string? BerthName,
    IReadOnlyList<PortCallActivity> Activities,
    IReadOnlyList<Delay> Delays);

internal static class PortCallLog
{
    internal const string ActualArrivalLabel = "Actual arrival (ATA)";
    internal const string ActualDepartureLabel = "Actual departure (ATD)";

    internal static async Task<(string VoyageNumber, VoyageStop Stop)> LoadStopAsync(
        DbContext dbContext,
        Guid voyageId,
        Guid stopId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var voyageNumber = await dbContext.Set<Voyage>()
            .AsNoTracking()
            .Where(voyage => voyage.Id == voyageId)
            .Select(voyage => voyage.VoyageNumber)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new VoyageNotFoundException(voyageId);

        var stops = tracking
            ? dbContext.Set<VoyageStop>()
            : dbContext.Set<VoyageStop>().AsNoTracking();

        var stop = await stops
            .SingleOrDefaultAsync(
                candidate => candidate.Id == stopId && candidate.VoyageId == voyageId,
                cancellationToken)
            ?? throw new VoyageStopNotFoundException(stopId);

        return (voyageNumber, stop);
    }

    internal static async Task<PortCallStatement?> FindStatementAsync(
        DbContext dbContext,
        Guid stopId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var statements = tracking
            ? dbContext.Set<PortCallStatement>()
            : dbContext.Set<PortCallStatement>().AsNoTracking();

        return await statements.SingleOrDefaultAsync(
            statement => statement.VoyageStopId == stopId,
            cancellationToken);
    }

    internal static async Task<PortCallLogSources> LoadSourcesAsync(
        DbContext dbContext,
        string voyageNumber,
        VoyageStop stop,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var port = await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == stop.PortId)
            .Select(candidate => new { candidate.Name, candidate.UnLocode, candidate.TimeZone })
            .SingleAsync(cancellationToken);

        var berthName = stop.BerthId is { } berthId
            ? await dbContext.Set<Berth>()
                .AsNoTracking()
                .Where(berth => berth.Id == berthId)
                .Select(berth => berth.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var activityQuery = tracking
            ? dbContext.Set<PortCallActivity>()
            : dbContext.Set<PortCallActivity>().AsNoTracking();

        var activities = await activityQuery
            .Where(activity => activity.VoyageStopId == stop.Id)
            .OrderBy(activity => activity.Sequence)
            .ToListAsync(cancellationToken);

        var delays = await dbContext.Set<Delay>()
            .AsNoTracking()
            .Where(delay => delay.VoyageStopId == stop.Id)
            .OrderBy(delay => delay.CreatedAt)
            .ThenBy(delay => delay.Id)
            .ToListAsync(cancellationToken);

        return new PortCallLogSources(
            stop,
            voyageNumber,
            port.Name,
            port.UnLocode,
            port.TimeZone,
            berthName,
            activities,
            delays);
    }

    internal static IReadOnlyList<PortCallLogEntry> BuildEntries(PortCallLogSources sources)
    {
        var stop = sources.Stop;
        var entries = new List<PortCallLogEntry>();

        if (stop.Ata is { } actualArrival)
        {
            entries.Add(new PortCallLogEntry(
                PortCallLogSource.StopArrival,
                stop.Id,
                null,
                0,
                ActualArrivalLabel,
                actualArrival,
                null,
                null,
                null));
        }

        entries.AddRange(sources.Activities
            .Where(activity => activity.IsActive)
            .Select(activity => new PortCallLogEntry(
                PortCallLogSource.ManualActivity,
                activity.Id,
                activity.Revision,
                activity.Sequence,
                activity.ActivityType,
                activity.OccurredAtUtc,
                activity.EndedAtUtc,
                activity.Notes,
                null)));

        entries.AddRange(sources.Delays.Select(delay => new PortCallLogEntry(
            PortCallLogSource.Delay,
            delay.Id,
            null,
            0,
            delay.Reason,
            delay.CreatedAt,
            null,
            delay.Description,
            delay.Duration)));

        if (stop.Atd is { } actualDeparture)
        {
            entries.Add(new PortCallLogEntry(
                PortCallLogSource.StopDeparture,
                stop.Id,
                null,
                0,
                ActualDepartureLabel,
                actualDeparture,
                null,
                null,
                null));
        }

        return entries
            .OrderBy(entry => entry.OccurredAtUtc)
            .ThenBy(entry => PortCallLogSource.Rank(entry.Source))
            .ThenBy(entry => entry.SourceSequence)
            .ThenBy(entry => entry.SourceId)
            .ToList();
    }

    internal static IReadOnlyList<PortCallStatementBlocker> FindBlockers(PortCallLogSources sources)
    {
        var stop = sources.Stop;
        var blockers = new List<PortCallStatementBlocker>();

        if (!string.Equals(stop.Status, VoyageStop.DepartedStatus, StringComparison.Ordinal))
        {
            blockers.Add(new PortCallStatementBlocker(
                PortCallStatementBlocker.StopNotDepartedCode,
                $"The stop must be departed before its statement can be finalized; its status is '{stop.Status}'.",
                null));
        }

        if (stop.Ata is null)
        {
            blockers.Add(new PortCallStatementBlocker(
                PortCallStatementBlocker.ActualArrivalMissingCode,
                "The stop has no actual arrival time (ATA).",
                null));
        }

        if (stop.Atd is null)
        {
            blockers.Add(new PortCallStatementBlocker(
                PortCallStatementBlocker.ActualDepartureMissingCode,
                "The stop has no actual departure time (ATD).",
                null));
        }

        if (stop.Ata is { } actualArrival && stop.Atd is { } actualDeparture)
        {
            blockers.AddRange(sources.Activities
                .Where(activity => activity.IsActive
                    && activity.IsOutsidePortCall(actualArrival, actualDeparture))
                .OrderBy(activity => activity.OccurredAtUtc)
                .ThenBy(activity => activity.Sequence)
                .Select(activity => new PortCallStatementBlocker(
                    PortCallStatementBlocker.ActivityOutsidePortCallCode,
                    $"{activity.ActivityType} at {FormatUtc(activity.OccurredAtUtc)} falls outside the port call "
                        + $"({FormatUtc(actualArrival)} to {FormatUtc(actualDeparture)}). Correct or void it.",
                    activity.Id)));
        }

        return blockers;
    }

    internal static string ComputeSourceVersion(PortCallLogSources sources)
    {
        var stop = sources.Stop;
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"stop|{stop.Id}|{stop.Status}|{stop.Ata?.Ticks}|{stop.Atd?.Ticks}\n");
        builder.Append(CultureInfo.InvariantCulture, $"labels|{sources.VoyageNumber}|{stop.StopOrder}|{sources.PortName}|{sources.PortUnLocode}|{sources.PortTimeZone}|{stop.BerthId}|{sources.BerthName}\n");

        foreach (var activity in sources.Activities.OrderBy(activity => activity.Sequence))
        {
            builder.Append(CultureInfo.InvariantCulture, $"activity|{activity.Id}|{activity.ConcurrencyToken}\n");
        }

        foreach (var delay in sources.Delays.OrderBy(delay => delay.CreatedAt).ThenBy(delay => delay.Id))
        {
            builder.Append(CultureInfo.InvariantCulture, $"delay|{delay.Id}|{delay.Reason}|{delay.Description}|{delay.Duration}|{delay.CreatedAt.Ticks}\n");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    internal static IReadOnlyList<PortCallLogEntryItem> ToEntryItems(
        IReadOnlyList<PortCallLogEntry> entries,
        DateTime? actualArrivalUtc,
        DateTime? actualDepartureUtc)
    {
        return entries
            .Select((entry, index) => new PortCallLogEntryItem(
                index + 1,
                entry.Source,
                entry.SourceId,
                entry.SourceRevision,
                entry.Label,
                entry.OccurredAtUtc,
                entry.EndedAtUtc,
                entry.Notes,
                entry.DurationMinutes,
                string.Equals(entry.Source, PortCallLogSource.ManualActivity, StringComparison.Ordinal)
                    && IsOutsidePortCall(entry, actualArrivalUtc, actualDepartureUtc)))
            .ToList();
    }

    internal static PortCallActivityItem ToItem(PortCallActivity activity) =>
        new(
            activity.Id,
            activity.Sequence,
            activity.ActivityType,
            activity.OccurredAtUtc,
            activity.EndedAtUtc,
            activity.Notes,
            activity.Status,
            activity.Revision,
            activity.IsLocked,
            activity.RecordedByUserId,
            activity.RecordedAtUtc,
            activity.LastChangedByUserId,
            activity.LastChangedAtUtc,
            activity.ConcurrencyToken);

    internal static PortCallStatementBlockerItem ToItem(PortCallStatementBlocker blocker) =>
        new(blocker.Code, blocker.Message, blocker.ActivityId);

    private static bool IsOutsidePortCall(
        PortCallLogEntry entry,
        DateTime? actualArrivalUtc,
        DateTime? actualDepartureUtc)
    {
        var endedAtUtc = entry.EndedAtUtc ?? entry.OccurredAtUtc;

        return (actualArrivalUtc is { } arrival && entry.OccurredAtUtc < arrival)
            || (actualDepartureUtc is { } departure && endedAtUtc > departure);
    }

    private static string FormatUtc(DateTime? value) =>
        value is { } utc
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : string.Empty;
}
