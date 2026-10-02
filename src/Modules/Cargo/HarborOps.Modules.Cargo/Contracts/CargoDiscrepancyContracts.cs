namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoDiscrepancyActor(Guid UserId, string? Name);

public sealed record RegisterCargoDiscrepancyRequest
{
    public string? Type { get; init; }

    public string? Severity { get; init; }

    public Guid? ContainerId { get; init; }

    public DateTime? ObservedAtUtc { get; init; }

    public string? Description { get; init; }
}

public sealed record OpenExcursionDiscrepancyRequest
{
    public string? Severity { get; init; }

    public DateTime? ObservedAtUtc { get; init; }

    public string? Description { get; init; }
}

public sealed record GetCargoDiscrepanciesRequest
{
    public string? Status { get; init; }

    public string? Type { get; init; }

    public string? Severity { get; init; }

    public Guid? ContainerId { get; init; }

    public Guid? SourceExcursionId { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}

public sealed record AddCargoDiscrepancyNoteRequest
{
    public string? Note { get; init; }
}

public sealed record ResolveCargoDiscrepancyRequest
{
    public string? Disposition { get; init; }

    public string? Summary { get; init; }
}

public sealed record VoidCargoDiscrepancyRequest
{
    public string? Reason { get; init; }
}

public sealed record CargoDiscrepancyListItem(
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

public sealed record CargoDiscrepancySummary(
    int ReportedCount,
    int InvestigatingCount,
    int ResolvedCount,
    int VoidedCount,
    int OpenCount);

public sealed record GetCargoDiscrepanciesResponse(
    IReadOnlyList<CargoDiscrepancyListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    CargoDiscrepancySummary Summary);

public sealed record CargoDiscrepancyEventListItem(
    Guid Id,
    string EventType,
    string? FromStatus,
    string? ToStatus,
    string? Text,
    Guid ActorUserId,
    string? ActorName,
    DateTime OccurredAtUtc);

public sealed record CargoDiscrepancySource(
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

public sealed record CargoDiscrepancyDetails(
    CargoDiscrepancyListItem Discrepancy,
    string BookingNumber,
    string BookingStatus,
    CargoDiscrepancySource? Source,
    int InvestigationNoteCount,
    string? ResolutionBlocker,
    IReadOnlyList<CargoDiscrepancyEventListItem> History);

public sealed record OpenExcursionDiscrepancyResponse(
    CargoDiscrepancyDetails Details,
    bool Created);

public sealed record CargoCompletionBlocker(
    Guid DiscrepancyId,
    string Type,
    string Severity,
    string Status,
    Guid? ContainerId,
    string? ContainerNumber);

public sealed record CargoCompletionBlockers(
    Guid CargoBookingId,
    int OpenCount,
    int ReportedCount,
    int InvestigatingCount,
    IReadOnlyList<CargoCompletionBlocker> Discrepancies);
