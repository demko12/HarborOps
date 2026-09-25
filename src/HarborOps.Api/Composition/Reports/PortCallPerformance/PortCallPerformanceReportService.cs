using System.Globalization;
using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Api.Composition.Reports.PortCallPerformance;

public sealed class PortCallPerformanceReportService(IReportsPortCallPerformanceQuery portCallPerformanceQuery)
{
    public async Task<PortCallPerformanceReportPage> GetPageAsync(
        PortCallPerformanceReportFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureValid(filter, pageNumber, pageSize);

        var result = await portCallPerformanceQuery.SearchAsync(
            ToRequest(filter, pageNumber, pageSize),
            cancellationToken);

        if (result.StopLimitExceeded)
        {
            throw LimitExceeded(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.TotalCount:N0} stops match these filters; the report is limited to {ReportsPortCallPerformanceOptions.MaxStops:N0}. Narrow the date range or add a filter."));
        }

        return new PortCallPerformanceReportPage(
            filter.DepartedFrom!.Value,
            filter.DepartedTo!.Value,
            result.Items.Select(ComposeRow).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount,
            ComposeSummary(result.Summary),
            result.Ports.Select(ComposePort).ToArray());
    }

    public async Task<(DateOnly DepartedFrom, DateOnly DepartedTo, IReadOnlyList<PortCallPerformanceReportRow> Rows)> GetExportRowsAsync(
        PortCallPerformanceReportFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValid(filter, pageNumber: 1, pageSize: 1);

        var result = await portCallPerformanceQuery.SearchAsync(
            ToRequest(filter, 1, PortCallPerformanceReportValidator.MaxExportRows),
            cancellationToken);

        if (result.StopLimitExceeded || result.TotalCount > PortCallPerformanceReportValidator.MaxExportRows)
        {
            throw LimitExceeded(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.TotalCount:N0} stops match these filters; CSV export is limited to {PortCallPerformanceReportValidator.MaxExportRows:N0} rows. Narrow the date range or add a filter."));
        }

        return (filter.DepartedFrom!.Value, filter.DepartedTo!.Value, result.Items.Select(ComposeRow).ToArray());
    }

    internal static decimal ToHours(decimal minutes) =>
        Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    private static void EnsureValid(PortCallPerformanceReportFilter filter, int pageNumber, int pageSize)
    {
        var errors = PortCallPerformanceReportValidator.Validate(filter, pageNumber, pageSize);

        if (errors.Count > 0)
        {
            throw new PortCallPerformanceReportValidationException(errors);
        }
    }

    private static PortCallPerformanceReportValidationException LimitExceeded(string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [PortCallPerformanceReportValidator.LimitField] = [message],
        });

    private static ReportsPortCallPerformanceRequest ToRequest(
        PortCallPerformanceReportFilter filter,
        int pageNumber,
        int pageSize) =>
        new()
        {
            DepartedFrom = filter.DepartedFrom!.Value,
            DepartedTo = filter.DepartedTo!.Value,
            VoyageId = filter.VoyageId,
            VesselId = filter.VesselId,
            PortId = filter.PortId,
            StatementAvailability = PortCallPerformanceReportValidator.Resolve(
                filter.StatementAvailability,
                ReportsPortCallPerformanceOptions.StatementAvailabilities)!,
            SortBy = PortCallPerformanceReportValidator.Resolve(
                filter.SortBy,
                ReportsPortCallPerformanceOptions.SortFields)!,
            SortDirection = PortCallPerformanceReportValidator.Resolve(
                filter.SortDirection,
                ReportsPortCallPerformanceOptions.SortDirections)!,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

    private static PortCallPerformanceReportRow ComposeRow(ReportsPortCallPerformanceRow row) => new(
        row.VoyageStopId,
        row.VoyageId,
        row.VesselId,
        row.PortId,
        row.VoyageNumber,
        row.StopOrder,
        row.PortName,
        row.PortUnLocode,
        row.BerthName,
        row.UsesLiveValues,
        row.ActualArrivalUtc,
        row.ActualDepartureUtc,
        row.StatementAvailability,
        row.StatementId,
        row.FinalizedAtUtc,
        ComposeMetric(row.Turnaround),
        ComposeMetric(row.ArrivalToAllFast),
        ComposeMetric(row.CargoElapsed),
        ComposeMetric(row.CargoCompletedToDeparture),
        row.RecordedDelayCount,
        row.RecordedDelayMinutes,
        row.RecordedDelayMinutes is { } delayMinutes ? ToHours(delayMinutes) : null);

    private static PortCallPerformanceReportMetric? ComposeMetric(ReportsPortCallMetric? metric) =>
        metric is null
            ? null
            : new PortCallPerformanceReportMetric(
                metric.Minutes,
                metric.Minutes is { } minutes ? ToHours(minutes) : null,
                metric.Quality,
                metric.Milestone);

    private static PortCallPerformanceReportPortSummary ComposePort(ReportsPortCallPerformancePortSummary port) => new(
        port.PortId,
        port.PortName,
        port.PortUnLocode,
        ComposeSummary(port.Summary));

    private static PortCallPerformanceReportSummary ComposeSummary(ReportsPortCallPerformanceSummary summary) => new(
        summary.StopCount,
        summary.FinalizedCount,
        summary.NotFinalizedCount,
        ComposeMetricSummary(summary.Turnaround),
        ComposeMetricSummary(summary.ArrivalToAllFast),
        ComposeMetricSummary(summary.CargoElapsed),
        ComposeMetricSummary(summary.CargoCompletedToDeparture),
        summary.RecordedDelayCount,
        summary.RecordedDelayMinutes,
        ToHours(summary.RecordedDelayMinutes));

    private static PortCallPerformanceReportMetricSummary ComposeMetricSummary(ReportsPortCallMetricSummary summary) => new(
        summary.MeanMinutes is { } meanMinutes ? Math.Round(meanMinutes, 2, MidpointRounding.AwayFromZero) : null,
        summary.MeanMinutes is { } mean ? ToHours(mean) : null,
        summary.SampleCount,
        summary.MissingCount,
        summary.AmbiguousCount,
        summary.InvalidChronologyCount);
}
