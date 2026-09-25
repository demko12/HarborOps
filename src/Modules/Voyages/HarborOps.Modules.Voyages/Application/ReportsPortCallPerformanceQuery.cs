using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

internal sealed class ReportsPortCallPerformanceQuery(DbContext dbContext, int maxStops)
    : IReportsPortCallPerformanceQuery
{
    private static readonly string[] MilestoneLabels =
    [
        PortCallActivityType.AllFast,
        PortCallActivityType.CargoStarted,
        PortCallActivityType.CargoCompleted,
    ];

    public ReportsPortCallPerformanceQuery(DbContext dbContext)
        : this(dbContext, ReportsPortCallPerformanceOptions.MaxStops)
    {
    }

    public async Task<ReportsPortCallPerformanceResult> SearchAsync(
        ReportsPortCallPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureOneOf(
            request.StatementAvailability,
            ReportsPortCallPerformanceOptions.StatementAvailabilities,
            nameof(request.StatementAvailability));
        EnsureOneOf(request.SortBy, ReportsPortCallPerformanceOptions.SortFields, nameof(request.SortBy));
        EnsureOneOf(
            request.SortDirection,
            ReportsPortCallPerformanceOptions.SortDirections,
            nameof(request.SortDirection));

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, ReportsPortCallPerformanceOptions.MaxPageSize);
        var departedFromUtc = request.DepartedFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var departedToUtcExclusive = request.DepartedTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var candidates =
            from stop in dbContext.Set<VoyageStop>().AsNoTracking()
            join voyage in dbContext.Set<Voyage>().AsNoTracking() on stop.VoyageId equals voyage.Id
            join finalizedStatement in dbContext.Set<PortCallStatement>().AsNoTracking()
                    .Where(statement => statement.Status == PortCallStatement.FinalizedStatus)
                on stop.Id equals finalizedStatement.VoyageStopId into finalizedStatements
            from statement in finalizedStatements.DefaultIfEmpty()
            where statement != null
                ? statement.ActualDepartureUtc >= departedFromUtc && statement.ActualDepartureUtc < departedToUtcExclusive
                : stop.Atd >= departedFromUtc && stop.Atd < departedToUtcExclusive
            select new
            {
                Stop = stop,
                voyage.VesselId,
                voyage.VoyageNumber,
                Statement = statement,
            };

        if (request.VoyageId is { } voyageId)
        {
            candidates = candidates.Where(candidate => candidate.Stop.VoyageId == voyageId);
        }

        if (request.VesselId is { } vesselId)
        {
            candidates = candidates.Where(candidate => candidate.VesselId == vesselId);
        }

        if (request.PortId is { } portId)
        {
            candidates = candidates.Where(candidate => candidate.Stop.PortId == portId);
        }

        if (request.StatementAvailability == ReportsPortCallPerformanceOptions.Finalized)
        {
            candidates = candidates.Where(candidate => candidate.Statement != null);
        }
        else if (request.StatementAvailability == ReportsPortCallPerformanceOptions.NotFinalized)
        {
            candidates = candidates.Where(candidate => candidate.Statement == null);
        }

        var totalCount = await candidates.CountAsync(cancellationToken);

        if (totalCount > maxStops)
        {
            return new ReportsPortCallPerformanceResult([], pageNumber, pageSize, totalCount, true, Summarize([]), []);
        }

        var stops = await candidates
            .Select(candidate => new StopCandidate(
                candidate.Stop.Id,
                candidate.Stop.VoyageId,
                candidate.VesselId,
                candidate.Stop.PortId,
                candidate.VoyageNumber,
                candidate.Stop.StopOrder,
                candidate.Stop.Ata,
                candidate.Stop.Atd,
                candidate.Statement))
            .ToListAsync(cancellationToken);

        var entries = await LoadEntryAggregatesAsync(stops, cancellationToken);
        var livePorts = await LoadLivePortsAsync(stops, cancellationToken);

        var rows = stops.Select(stop => ToRow(stop, entries, livePorts)).ToList();

        var ports = rows
            .GroupBy(row => row.PortId)
            .Select(SummarizePort)
            .OrderBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(port => port.PortId)
            .ToList();

        var items = Sort(rows, request.SortBy, request.SortDirection == ReportsPortCallPerformanceOptions.Descending)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ReportsPortCallPerformanceResult(
            items,
            pageNumber,
            pageSize,
            totalCount,
            false,
            Summarize(rows),
            ports);
    }

    private async Task<ILookup<Guid, EntryAggregate>> LoadEntryAggregatesAsync(
        IReadOnlyList<StopCandidate> stops,
        CancellationToken cancellationToken)
    {
        var statementIds = stops
            .Where(stop => stop.Statement is not null)
            .Select(stop => stop.Statement!.Id)
            .ToList();

        if (statementIds.Count == 0)
        {
            return Array.Empty<EntryAggregate>().ToLookup(entry => entry.PortCallStatementId);
        }

        var aggregates = await dbContext.Set<PortCallStatementEntry>()
            .AsNoTracking()
            .Where(entry => statementIds.Contains(entry.PortCallStatementId)
                && (entry.Source == PortCallLogSource.Delay
                    || (entry.Source == PortCallLogSource.ManualActivity && MilestoneLabels.Contains(entry.Label))))
            .GroupBy(entry => new { entry.PortCallStatementId, entry.Source, entry.Label })
            .Select(group => new
            {
                group.Key.PortCallStatementId,
                group.Key.Source,
                group.Key.Label,
                Count = group.Count(),
                EarliestAtUtc = group.Min(entry => entry.OccurredAtUtc),
                DurationMinutes = group.Sum(entry => entry.DurationMinutes ?? 0),
            })
            .ToListAsync(cancellationToken);

        return aggregates
            .Select(aggregate => new EntryAggregate(
                aggregate.PortCallStatementId,
                aggregate.Source,
                aggregate.Label,
                aggregate.Count,
                aggregate.EarliestAtUtc,
                aggregate.DurationMinutes))
            .ToLookup(aggregate => aggregate.PortCallStatementId);
    }

    private async Task<Dictionary<Guid, LivePort>> LoadLivePortsAsync(
        IReadOnlyList<StopCandidate> stops,
        CancellationToken cancellationToken)
    {
        var portIds = stops.Select(stop => stop.PortId).Distinct().ToList();

        return await dbContext.Set<Port>()
            .AsNoTracking()
            .Where(port => portIds.Contains(port.Id))
            .Select(port => new LivePort(port.Id, port.Name, port.UnLocode))
            .ToDictionaryAsync(port => port.Id, cancellationToken);
    }

    private static ReportsPortCallPerformanceRow ToRow(
        StopCandidate stop,
        ILookup<Guid, EntryAggregate> entries,
        IReadOnlyDictionary<Guid, LivePort> livePorts)
    {
        if (stop.Statement is not { } statement)
        {
            livePorts.TryGetValue(stop.PortId, out var livePort);

            return new ReportsPortCallPerformanceRow(
                stop.StopId,
                stop.VoyageId,
                stop.VesselId,
                stop.PortId,
                stop.VoyageNumber,
                stop.StopOrder,
                livePort?.Name ?? string.Empty,
                livePort?.UnLocode,
                null,
                true,
                stop.Ata,
                stop.Atd!.Value,
                ReportsPortCallPerformanceOptions.NotFinalized,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var statementEntries = entries[statement.Id].ToList();
        var delays = statementEntries
            .Where(entry => entry.Source == PortCallLogSource.Delay)
            .ToList();

        var metrics = PortCallPerformanceCalculator.Calculate(new PortCallPerformanceInput(
            statement.ActualArrivalUtc,
            statement.ActualDepartureUtc,
            Occurrences(statementEntries, PortCallActivityType.AllFast),
            Occurrences(statementEntries, PortCallActivityType.CargoStarted),
            Occurrences(statementEntries, PortCallActivityType.CargoCompleted)));

        return new ReportsPortCallPerformanceRow(
            stop.StopId,
            stop.VoyageId,
            stop.VesselId,
            stop.PortId,
            statement.VoyageNumber!,
            statement.StopOrder!.Value,
            statement.PortName!,
            statement.PortUnLocode,
            statement.BerthName,
            false,
            statement.ActualArrivalUtc,
            statement.ActualDepartureUtc!.Value,
            ReportsPortCallPerformanceOptions.Finalized,
            statement.Id,
            statement.FinalizedAtUtc,
            ToContract(metrics.Turnaround),
            ToContract(metrics.ArrivalToAllFast),
            ToContract(metrics.CargoElapsed),
            ToContract(metrics.CargoCompletedToDeparture),
            delays.Sum(entry => entry.Count),
            delays.Sum(entry => entry.DurationMinutes));
    }

    private static PortCallMilestoneOccurrences Occurrences(IReadOnlyList<EntryAggregate> entries, string label)
    {
        var aggregate = entries.SingleOrDefault(entry =>
            entry.Source == PortCallLogSource.ManualActivity
            && string.Equals(entry.Label, label, StringComparison.Ordinal));

        return aggregate is null
            ? PortCallMilestoneOccurrences.None
            : new PortCallMilestoneOccurrences(aggregate.Count, aggregate.EarliestAtUtc);
    }

    private static ReportsPortCallMetric ToContract(PortCallMetric metric) =>
        new(
            metric.Minutes,
            metric.Quality switch
            {
                PortCallMetricQuality.Valid => ReportsPortCallPerformanceOptions.QualityValid,
                PortCallMetricQuality.Missing => ReportsPortCallPerformanceOptions.QualityMissing,
                PortCallMetricQuality.Ambiguous => ReportsPortCallPerformanceOptions.QualityAmbiguous,
                PortCallMetricQuality.InvalidChronology => ReportsPortCallPerformanceOptions.QualityInvalidChronology,
                _ => throw new InvalidOperationException($"Unknown metric quality '{metric.Quality}'."),
            },
            metric.Milestone);

    private static ReportsPortCallPerformancePortSummary SummarizePort(
        IGrouping<Guid, ReportsPortCallPerformanceRow> group)
    {
        var label = group
            .Where(row => row.StatementId is not null)
            .OrderByDescending(row => row.FinalizedAtUtc)
            .ThenByDescending(row => row.StatementId)
            .FirstOrDefault()
            ?? group.First();

        return new ReportsPortCallPerformancePortSummary(
            group.Key,
            label.PortName,
            label.PortUnLocode,
            Summarize(group.ToList()));
    }

    private static ReportsPortCallPerformanceSummary Summarize(IReadOnlyList<ReportsPortCallPerformanceRow> rows)
    {
        var finalized = rows.Where(row => row.StatementId is not null).ToList();

        return new ReportsPortCallPerformanceSummary(
            rows.Count,
            finalized.Count,
            rows.Count - finalized.Count,
            SummarizeMetric(finalized.Select(row => row.Turnaround!)),
            SummarizeMetric(finalized.Select(row => row.ArrivalToAllFast!)),
            SummarizeMetric(finalized.Select(row => row.CargoElapsed!)),
            SummarizeMetric(finalized.Select(row => row.CargoCompletedToDeparture!)),
            finalized.Sum(row => row.RecordedDelayCount ?? 0),
            finalized.Sum(row => row.RecordedDelayMinutes ?? 0));
    }

    private static ReportsPortCallMetricSummary SummarizeMetric(IEnumerable<ReportsPortCallMetric> metrics)
    {
        var all = metrics.ToList();
        var samples = all
            .Where(metric => metric.Minutes is not null)
            .Select(metric => (long)metric.Minutes!.Value)
            .ToList();

        return new ReportsPortCallMetricSummary(
            samples.Count == 0 ? null : (decimal)samples.Sum() / samples.Count,
            samples.Count,
            all.Count(metric => metric.Quality == ReportsPortCallPerformanceOptions.QualityMissing),
            all.Count(metric => metric.Quality == ReportsPortCallPerformanceOptions.QualityAmbiguous),
            all.Count(metric => metric.Quality == ReportsPortCallPerformanceOptions.QualityInvalidChronology));
    }

    private static IEnumerable<ReportsPortCallPerformanceRow> Sort(
        IEnumerable<ReportsPortCallPerformanceRow> rows,
        string sortBy,
        bool descending)
    {
        var ordered = sortBy switch
        {
            ReportsPortCallPerformanceOptions.SortByVoyage =>
                ThenBy(
                    OrderBy(rows, row => row.VoyageNumber, descending, StringComparer.OrdinalIgnoreCase),
                    row => row.StopOrder,
                    descending),
            ReportsPortCallPerformanceOptions.SortByPort =>
                OrderBy(rows, row => row.PortName, descending, StringComparer.OrdinalIgnoreCase),
            ReportsPortCallPerformanceOptions.SortByTurnaround =>
                OrderByMinutes(rows, row => row.Turnaround?.Minutes, descending),
            ReportsPortCallPerformanceOptions.SortByArrivalToAllFast =>
                OrderByMinutes(rows, row => row.ArrivalToAllFast?.Minutes, descending),
            ReportsPortCallPerformanceOptions.SortByCargoElapsed =>
                OrderByMinutes(rows, row => row.CargoElapsed?.Minutes, descending),
            ReportsPortCallPerformanceOptions.SortByCargoCompletedToDeparture =>
                OrderByMinutes(rows, row => row.CargoCompletedToDeparture?.Minutes, descending),
            ReportsPortCallPerformanceOptions.SortByRecordedDelay =>
                OrderByMinutes(rows, row => row.RecordedDelayMinutes, descending),
            _ => OrderBy(rows, row => row.ActualDepartureUtc, descending),
        };

        return ordered
            .ThenBy(row => row.ActualDepartureUtc)
            .ThenBy(row => row.VoyageStopId);
    }

    private static IOrderedEnumerable<ReportsPortCallPerformanceRow> OrderByMinutes(
        IEnumerable<ReportsPortCallPerformanceRow> rows,
        Func<ReportsPortCallPerformanceRow, int?> minutes,
        bool descending) =>
        ThenBy(rows.OrderBy(row => minutes(row) is null), minutes, descending);

    private static IOrderedEnumerable<ReportsPortCallPerformanceRow> OrderBy<TKey>(
        IEnumerable<ReportsPortCallPerformanceRow> rows,
        Func<ReportsPortCallPerformanceRow, TKey> key,
        bool descending,
        IComparer<TKey>? comparer = null) =>
        descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    private static IOrderedEnumerable<ReportsPortCallPerformanceRow> ThenBy<TKey>(
        IOrderedEnumerable<ReportsPortCallPerformanceRow> rows,
        Func<ReportsPortCallPerformanceRow, TKey> key,
        bool descending) =>
        descending ? rows.ThenByDescending(key) : rows.ThenBy(key);

    private static void EnsureOneOf(string value, IReadOnlyList<string> allowed, string parameterName)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{value}' is not one of: {string.Join(", ", allowed)}.",
                parameterName);
        }
    }

    private sealed record StopCandidate(
        Guid StopId,
        Guid VoyageId,
        Guid VesselId,
        Guid PortId,
        string VoyageNumber,
        int StopOrder,
        DateTime? Ata,
        DateTime? Atd,
        PortCallStatement? Statement);

    private sealed record EntryAggregate(
        Guid PortCallStatementId,
        string Source,
        string Label,
        int Count,
        DateTime EarliestAtUtc,
        int DurationMinutes);

    private sealed record LivePort(Guid Id, string Name, string? UnLocode);
}
