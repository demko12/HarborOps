namespace HarborOps.Modules.Voyages.Contracts;

public interface IReportsPortCallPerformanceQuery
{
    Task<ReportsPortCallPerformanceResult> SearchAsync(
        ReportsPortCallPerformanceRequest request,
        CancellationToken cancellationToken);
}

public static class ReportsPortCallPerformanceOptions
{
    public const int MaxRangeDays = 366;
    public const int MaxPageSize = 10_000;
    public const int MaxStops = 20_000;

    public const string AllStatements = "All";
    public const string Finalized = "Finalized";
    public const string NotFinalized = "NotFinalized";

    public const string SortByActualDeparture = "ActualDeparture";
    public const string SortByVoyage = "Voyage";
    public const string SortByPort = "Port";
    public const string SortByTurnaround = "Turnaround";
    public const string SortByArrivalToAllFast = "ArrivalToAllFast";
    public const string SortByCargoElapsed = "CargoElapsed";
    public const string SortByCargoCompletedToDeparture = "CargoCompletedToDeparture";
    public const string SortByRecordedDelay = "RecordedDelay";

    public const string Ascending = "Asc";
    public const string Descending = "Desc";

    public const string QualityValid = "Valid";
    public const string QualityMissing = "Missing";
    public const string QualityAmbiguous = "Ambiguous";
    public const string QualityInvalidChronology = "InvalidChronology";

    public static readonly IReadOnlyList<string> StatementAvailabilities = [AllStatements, Finalized, NotFinalized];

    public static readonly IReadOnlyList<string> SortFields =
    [
        SortByActualDeparture,
        SortByVoyage,
        SortByPort,
        SortByTurnaround,
        SortByArrivalToAllFast,
        SortByCargoElapsed,
        SortByCargoCompletedToDeparture,
        SortByRecordedDelay,
    ];

    public static readonly IReadOnlyList<string> SortDirections = [Ascending, Descending];
}

public sealed record ReportsPortCallPerformanceRequest
{
    public required DateOnly DepartedFrom { get; init; }
    public required DateOnly DepartedTo { get; init; }
    public Guid? VoyageId { get; init; }
    public Guid? VesselId { get; init; }
    public Guid? PortId { get; init; }
    public string StatementAvailability { get; init; } = ReportsPortCallPerformanceOptions.AllStatements;
    public string SortBy { get; init; } = ReportsPortCallPerformanceOptions.SortByActualDeparture;
    public string SortDirection { get; init; } = ReportsPortCallPerformanceOptions.Ascending;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record ReportsPortCallPerformanceResult(
    IReadOnlyList<ReportsPortCallPerformanceRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    bool StopLimitExceeded,
    ReportsPortCallPerformanceSummary Summary,
    IReadOnlyList<ReportsPortCallPerformancePortSummary> Ports);

public sealed record ReportsPortCallPerformanceRow(
    Guid VoyageStopId,
    Guid VoyageId,
    Guid VesselId,
    Guid PortId,
    string VoyageNumber,
    int StopOrder,
    string PortName,
    string? PortUnLocode,
    string? BerthName,
    bool UsesLiveValues,
    DateTime? ActualArrivalUtc,
    DateTime ActualDepartureUtc,
    string StatementAvailability,
    Guid? StatementId,
    DateTime? FinalizedAtUtc,
    ReportsPortCallMetric? Turnaround,
    ReportsPortCallMetric? ArrivalToAllFast,
    ReportsPortCallMetric? CargoElapsed,
    ReportsPortCallMetric? CargoCompletedToDeparture,
    int? RecordedDelayCount,
    int? RecordedDelayMinutes);

public sealed record ReportsPortCallMetric(int? Minutes, string Quality, string? Milestone);

public sealed record ReportsPortCallPerformanceSummary(
    int StopCount,
    int FinalizedCount,
    int NotFinalizedCount,
    ReportsPortCallMetricSummary Turnaround,
    ReportsPortCallMetricSummary ArrivalToAllFast,
    ReportsPortCallMetricSummary CargoElapsed,
    ReportsPortCallMetricSummary CargoCompletedToDeparture,
    int RecordedDelayCount,
    int RecordedDelayMinutes);

public sealed record ReportsPortCallMetricSummary(
    decimal? MeanMinutes,
    int SampleCount,
    int MissingCount,
    int AmbiguousCount,
    int InvalidChronologyCount);

public sealed record ReportsPortCallPerformancePortSummary(
    Guid PortId,
    string PortName,
    string? PortUnLocode,
    ReportsPortCallPerformanceSummary Summary);
