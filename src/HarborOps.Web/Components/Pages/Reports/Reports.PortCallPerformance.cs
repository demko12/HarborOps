using System.Globalization;
using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class Reports
{
    private const int PortCallExportLimit = 10_000;
    private const int PortCallMaxRangeDays = 366;
    private const int PortCallDefaultRangeDays = 30;

    private const string AllStatements = "All";
    private const string FinalizedStatement = "Finalized";
    private const string NotFinalizedStatement = "NotFinalized";

    private const string SortByActualDeparture = "ActualDeparture";
    private const string SortByVoyage = "Voyage";
    private const string SortByPort = "Port";
    private const string SortByTurnaround = "Turnaround";
    private const string SortByArrivalToAllFast = "ArrivalToAllFast";
    private const string SortByCargoElapsed = "CargoElapsed";
    private const string SortByCargoCompletedToDeparture = "CargoCompletedToDeparture";
    private const string SortByRecordedDelay = "RecordedDelay";
    private const string SortByStops = "Stops";

    private const string Ascending = "Asc";
    private const string Descending = "Desc";

    private const string QualityMissing = "Missing";
    private const string QualityAmbiguous = "Ambiguous";
    private const string QualityInvalidChronology = "InvalidChronology";

    private const string TurnaroundDefinition = "Actual arrival (ATA) to actual departure (ATD).";
    private const string ArrivalToAllFastDefinition = "Actual arrival (ATA) to all fast.";
    private const string CargoElapsedDefinition = "Cargo started to cargo completed.";
    private const string CargoCompletedToDepartureDefinition = "Cargo completed to actual departure (ATD).";

    private static readonly (string Value, string Label)[] StatementAvailabilityOptions =
    [
        (AllStatements, "All stops"),
        (FinalizedStatement, "Finalized"),
        (NotFinalizedStatement, "Not finalized"),
    ];

    private DateOnly? portCallDepartedFromFilter = DefaultPortCallDepartedTo().AddDays(-PortCallDefaultRangeDays);
    private DateOnly? portCallDepartedToFilter = DefaultPortCallDepartedTo();
    private Guid? portCallPortIdFilter;
    private int portPickerResetToken;
    private Guid? portCallVoyageIdFilter;
    private string? portCallVoyageLabel;
    private string portCallAvailabilityFilter = AllStatements;
    private string portCallSortBy = SortByActualDeparture;
    private string portCallSortDirection = Ascending;
    private string portSummarySortBy = SortByPort;
    private string portSummarySortDirection = Ascending;

    private IReadOnlyList<PortCallPerformanceReportRow> portCallRows = [];
    private PortCallPerformanceReportSummary? portCallSummary;
    private IReadOnlyList<PortCallPerformanceReportPortSummary> portCallPorts = [];
    private IReadOnlyList<string> portCallServerErrors = [];

    private bool PortCallExportExceedsLimit => totalCount > PortCallExportLimit;

    private string PortCallExportLimitMessage => string.Create(
        CultureInfo.InvariantCulture,
        $"{totalCount:N0} stops match. CSV export is limited to {PortCallExportLimit:N0} rows. Narrow the date range or add a filter.");

    private string PortCallSubtitle => portCallDepartedFromFilter is { } from && portCallDepartedToFilter is { } to
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{StopsLabel(totalCount)} departed {from:dd MMM yyyy} – {to:dd MMM yyyy} (UTC) · elapsed operational time, not laytime")
        : "Choose a departure date range";

    private string PortCallRangeLabel => portCallDepartedFromFilter is { } from && portCallDepartedToFilter is { } to
        ? string.Create(CultureInfo.InvariantCulture, $"{from:dd MMM yyyy} – {to:dd MMM yyyy}")
        : "—";

    private static DateOnly DefaultPortCallDepartedTo() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string StopsLabel(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? "stop" : "stops")}");

    private void ResetPortCallFilters()
    {
        portCallDepartedToFilter = DefaultPortCallDepartedTo();
        portCallDepartedFromFilter = portCallDepartedToFilter.Value.AddDays(-PortCallDefaultRangeDays);
        portCallPortIdFilter = null;
        portPickerResetToken++;
        portCallVoyageIdFilter = null;
        portCallVoyageLabel = null;
        portCallAvailabilityFilter = AllStatements;
        portCallSortBy = SortByActualDeparture;
        portCallSortDirection = Ascending;
        portSummarySortBy = SortByPort;
        portSummarySortDirection = Ascending;
    }

    private string? ValidatePortCallRange()
    {
        if (portCallDepartedFromFilter is not { } from || portCallDepartedToFilter is not { } to)
        {
            return "Choose both a \"departed from\" and a \"departed to\" date.";
        }

        if (to < from)
        {
            return "The \"to\" date must be on or after the \"from\" date.";
        }

        return to.DayNumber - from.DayNumber + 1 > PortCallMaxRangeDays
            ? $"The departure date range must not exceed {PortCallMaxRangeDays} days."
            : null;
    }

    private PortCallPerformanceReportQuery CurrentPortCallQuery() =>
        new(
            portCallDepartedFromFilter!.Value,
            portCallDepartedToFilter!.Value,
            portCallVoyageIdFilter,
            vesselIdFilter,
            portCallPortIdFilter,
            portCallAvailabilityFilter,
            portCallSortBy,
            portCallSortDirection,
            pageNumber,
            PageSize);

    private async Task LoadPortCallPerformanceAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;
        portCallServerErrors = [];

        ReportsApiResult<PortCallPerformanceReportPage> result;

        try
        {
            result = await ReportsApi.GetPortCallPerformanceAsync(CurrentPortCallQuery(), cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            portCallRows = [];
            portCallSummary = null;
            portCallPorts = [];
            totalCount = 0;

            if (failure!.IsValidation)
            {
                portCallServerErrors = failure.Errors.Values.SelectMany(messages => messages).ToArray();
            }

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        portCallRows = page.Items;
        portCallSummary = page.Summary;
        portCallPorts = page.Ports;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

    private Task OnPortFilterChangedAsync(Guid? portId)
    {
        portCallPortIdFilter = portId;

        return SearchAsync();
    }

    private Task FilterByVoyageAsync(PortCallPerformanceReportRow row)
    {
        portCallVoyageIdFilter = row.VoyageId;
        portCallVoyageLabel = row.VoyageNumber;

        return SearchAsync();
    }

    private Task ClearVoyageFilterAsync()
    {
        portCallVoyageIdFilter = null;
        portCallVoyageLabel = null;

        return SearchAsync();
    }

    private Task SortPortCallsAsync(string sortBy)
    {
        if (portCallSortBy == sortBy)
        {
            portCallSortDirection = portCallSortDirection == Ascending ? Descending : Ascending;
        }
        else
        {
            portCallSortBy = sortBy;
            portCallSortDirection = Ascending;
        }

        return SearchAsync();
    }

    private void SortPortSummaries(string sortBy)
    {
        if (portSummarySortBy == sortBy)
        {
            portSummarySortDirection = portSummarySortDirection == Ascending ? Descending : Ascending;
        }
        else
        {
            portSummarySortBy = sortBy;
            portSummarySortDirection = Ascending;
        }
    }

    private IEnumerable<PortCallPerformanceReportPortSummary> SortedPortCallPorts
    {
        get
        {
            var descending = portSummarySortDirection == Descending;
            var ordered = portSummarySortBy switch
            {
                SortByStops => OrderPorts(portCallPorts, port => port.Summary.StopCount, descending),
                SortByTurnaround => OrderPortsByMean(portCallPorts, port => port.Summary.Turnaround, descending),
                SortByArrivalToAllFast => OrderPortsByMean(portCallPorts, port => port.Summary.ArrivalToAllFast, descending),
                SortByCargoElapsed => OrderPortsByMean(portCallPorts, port => port.Summary.CargoElapsed, descending),
                SortByCargoCompletedToDeparture =>
                    OrderPortsByMean(portCallPorts, port => port.Summary.CargoCompletedToDeparture, descending),
                SortByRecordedDelay => OrderPorts(portCallPorts, port => port.Summary.RecordedDelayMinutes, descending),
                _ => OrderPorts(portCallPorts, port => port.PortName, descending, StringComparer.OrdinalIgnoreCase),
            };

            return ordered
                .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(port => port.PortId);
        }
    }

    private static IOrderedEnumerable<PortCallPerformanceReportPortSummary> OrderPorts<TKey>(
        IEnumerable<PortCallPerformanceReportPortSummary> ports,
        Func<PortCallPerformanceReportPortSummary, TKey> key,
        bool descending,
        IComparer<TKey>? comparer = null) =>
        descending ? ports.OrderByDescending(key, comparer) : ports.OrderBy(key, comparer);

    private static IOrderedEnumerable<PortCallPerformanceReportPortSummary> OrderPortsByMean(
        IEnumerable<PortCallPerformanceReportPortSummary> ports,
        Func<PortCallPerformanceReportPortSummary, PortCallPerformanceReportMetricSummary> metric,
        bool descending)
    {
        var withMeansFirst = ports.OrderBy(port => metric(port).MeanMinutes is null);

        return descending
            ? withMeansFirst.ThenByDescending(port => metric(port).MeanMinutes)
            : withMeansFirst.ThenBy(port => metric(port).MeanMinutes);
    }

    private string PortCallAriaSort(string sortBy) => AriaSort(portCallSortBy, portCallSortDirection, sortBy);

    private string PortCallSortIcon(string sortBy) => SortIcon(portCallSortBy, portCallSortDirection, sortBy);

    private string PortSummaryAriaSort(string sortBy) => AriaSort(portSummarySortBy, portSummarySortDirection, sortBy);

    private string PortSummarySortIcon(string sortBy) => SortIcon(portSummarySortBy, portSummarySortDirection, sortBy);

    private static string AriaSort(string currentSortBy, string currentDirection, string sortBy) =>
        currentSortBy != sortBy ? "none" : currentDirection == Ascending ? "ascending" : "descending";

    private static string SortIcon(string currentSortBy, string currentDirection, string sortBy) =>
        currentSortBy != sortBy
            ? "bi-arrow-down-up reports-sort-idle"
            : currentDirection == Ascending ? "bi-arrow-up" : "bi-arrow-down";

    private string BuildPortCallPerformanceDownloadUrl() =>
        ValidatePortCallRange() is null
            ? $"/reports/download/port-call-performance?{ReportsApiClient.BuildPortCallPerformanceQueryString(CurrentPortCallQuery())}"
            : "#";

    private static string StatementPrintUrl(PortCallPerformanceReportRow row) =>
        $"/voyages/{row.VoyageId}/stops/{row.VoyageStopId}/statement/print";

    private static string FormatUtc(DateTime value) =>
        value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string FormatHours(decimal hours) =>
        hours.ToString("0.00", CultureInfo.InvariantCulture) + " h";

    private static string QualityLabel(PortCallPerformanceReportMetric metric) =>
        metric.Quality switch
        {
            QualityMissing => "Missing",
            QualityAmbiguous => "Ambiguous",
            QualityInvalidChronology => "Invalid",
            _ => metric.Quality,
        };

    private static string QualityClass(PortCallPerformanceReportMetric metric) =>
        metric.Quality switch
        {
            QualityMissing => "reports-status-warning",
            QualityAmbiguous => "reports-status-active",
            QualityInvalidChronology => "reports-status-expired",
            _ => "reports-status-neutral",
        };

    private static string QualityTitle(PortCallPerformanceReportMetric metric) =>
        metric.Quality switch
        {
            QualityMissing => $"No {MilestoneLabel(metric.Milestone)} recorded in the finalized statement.",
            QualityAmbiguous => $"{MilestoneLabel(metric.Milestone)} is recorded more than once; no pairing is guessed.",
            QualityInvalidChronology => "Milestones are out of order or outside the arrival–departure window.",
            _ => string.Empty,
        };

    private static string MilestoneLabel(string? milestone) =>
        milestone switch
        {
            "ActualArrival" => "actual arrival",
            "ActualDeparture" => "actual departure",
            "AllFast" => "all fast",
            "CargoStarted" => "cargo started",
            "CargoCompleted" => "cargo completed",
            _ => "milestone",
        };

    private static string RecordedDelaysLabel(PortCallPerformanceReportSummary summary) =>
        $"{(summary.RecordedDelayCount == 1 ? "recorded delay" : "recorded delays")} · {FormatHours(summary.RecordedDelayHours)}";

    private static string MeanLabel(PortCallPerformanceReportMetricSummary metric) =>
        metric.MeanHours is { } hours ? FormatHours(hours) : "—";

    private static string SampleLabel(
        PortCallPerformanceReportSummary summary,
        PortCallPerformanceReportMetricSummary metric,
        bool includeNotFinalized = true)
    {
        var parts = new List<string> { $"from {metric.SampleCount} of {StopsLabel(summary.StopCount)}" };

        if (metric.MissingCount > 0)
        {
            parts.Add($"{metric.MissingCount} missing a milestone");
        }

        if (metric.AmbiguousCount > 0)
        {
            parts.Add($"{metric.AmbiguousCount} with a duplicated milestone");
        }

        if (metric.InvalidChronologyCount > 0)
        {
            parts.Add($"{metric.InvalidChronologyCount} out of order");
        }

        if (includeNotFinalized && summary.NotFinalizedCount > 0)
        {
            parts.Add($"{summary.NotFinalizedCount} not finalized");
        }

        return string.Join(" · ", parts);
    }
}
