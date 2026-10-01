namespace HarborOps.Web.Api.Cargo;

internal sealed record CargoDiscrepancyRow(
    Guid Id,
    Guid CargoBookingId,
    Guid? ContainerId,
    string? ContainerNumber,
    string Type,
    string Severity,
    string Description,
    DateTime ObservedAtUtc,
    Guid ReportedByUserId,
    string? ReportedByName,
    DateTime ReportedAtUtc,
    string Status,
    string? Disposition,
    Guid? ResolvedByUserId,
    string? ResolvedByName,
    DateTime? ResolvedAtUtc,
    Guid? VoidedByUserId,
    string? VoidedByName,
    DateTime? VoidedAtUtc,
    Guid? SourceExcursionId);

internal sealed record CargoDiscrepancySummaryView(
    int ReportedCount,
    int InvestigatingCount,
    int ResolvedCount,
    int VoidedCount,
    int OpenCount);

internal sealed record CargoDiscrepancyPage(
    IReadOnlyList<CargoDiscrepancyRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    CargoDiscrepancySummaryView Summary);

internal sealed record CargoDiscrepancyHistoryRow(
    Guid Id,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    string? Text,
    Guid ActorUserId,
    string? ActorName,
    DateTime OccurredAtUtc);

internal sealed record CargoDiscrepancySourceView(
    Guid ExcursionId,
    Guid ContainerId,
    string? Direction,
    decimal? TemperatureCelsius,
    decimal? ThresholdMinimumCelsius,
    decimal? ThresholdMaximumCelsius,
    DateTime? MeasuredAtUtc,
    DateTime? DetectedAtUtc,
    string CurrentStatus,
    string? CurrentDisposition,
    Guid? SupersededByExcursionId,
    bool BlocksResolution);

internal sealed record CargoDiscrepancyView(
    CargoDiscrepancyRow Discrepancy,
    string BookingNumber,
    string BookingStatus,
    CargoDiscrepancySourceView? Source,
    int InvestigationNoteCount,
    string? ResolutionBlocker,
    IReadOnlyList<CargoDiscrepancyHistoryRow> History);

internal sealed record OpenedExcursionDiscrepancy(
    CargoDiscrepancyView Details,
    bool Created);

internal sealed record CargoCompletionBlockerRow(
    Guid DiscrepancyId,
    string Type,
    string Severity,
    string Status,
    Guid? ContainerId,
    string? ContainerNumber);

internal sealed record CargoCompletionBlockersView(
    Guid CargoBookingId,
    int OpenCount,
    int ReportedCount,
    int InvestigatingCount,
    IReadOnlyList<CargoCompletionBlockerRow> Discrepancies);

internal sealed record CargoDiscrepancyFilter(
    string? Status = null,
    Guid? SourceExcursionId = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? Type = null,
    string? Severity = null);

internal sealed record RegisterCargoDiscrepancyWebRequest(
    string Type,
    string Severity,
    Guid? ContainerId,
    DateTime ObservedAtUtc,
    string Description);

internal sealed record OpenExcursionDiscrepancyWebRequest(
    string Severity,
    string Description);

internal sealed record AddCargoDiscrepancyNoteWebRequest(string Note);

internal sealed record ResolveCargoDiscrepancyWebRequest(string Disposition, string Summary);

internal sealed record VoidCargoDiscrepancyWebRequest(string Reason);

internal sealed record CargoApiProblemDetails
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public IReadOnlyDictionary<string, string[]> Errors { get; init; }
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public CargoCompletionBlockersView? CompletionBlockers { get; init; }
}
