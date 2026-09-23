namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ReeferMonitoringProfileDetails(
    Guid ContainerId,
    decimal MinimumCelsius,
    decimal MaximumCelsius,
    decimal TargetCelsius,
    int Version,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc);

public sealed record ReeferProfileChangeListItem(
    Guid Id,
    int Version,
    decimal? FromMinimumCelsius,
    decimal? FromMaximumCelsius,
    decimal? FromTargetCelsius,
    decimal ToMinimumCelsius,
    decimal ToMaximumCelsius,
    decimal ToTargetCelsius,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredUtc);

public sealed record GetReeferMonitoringProfileResponse(
    Guid ContainerId,
    string ContainerNumber,
    ReeferMonitoringProfileDetails? Profile,
    IReadOnlyList<ReeferProfileChangeListItem> Changes,
    int OpenExcursionCount,
    decimal? LatestInspectionTemperatureCelsius);

public sealed record SaveReeferMonitoringProfileRequest
{
    public decimal? MinimumCelsius { get; init; }

    public decimal? MaximumCelsius { get; init; }

    public decimal? TargetCelsius { get; init; }

    public string? Reason { get; init; }
}

public sealed record ReeferTemperatureReadingListItem(
    Guid Id,
    Guid ContainerId,
    decimal TemperatureCelsius,
    DateTime MeasuredAtUtc,
    DateTime RecordedAtUtc,
    string? Notes,
    Guid RecordedByUserId,
    decimal ThresholdMinimumCelsius,
    decimal ThresholdMaximumCelsius,
    decimal ThresholdTargetCelsius,
    int ThresholdProfileVersion,
    string Status,
    bool IsWithinThresholds,
    Guid? CorrectsReadingId,
    Guid? SupersededByReadingId,
    string? CorrectionReason,
    Guid? CorrectedByUserId,
    DateTime? CorrectedAtUtc,
    string? VoidReason,
    Guid? VoidedByUserId,
    DateTime? VoidedAtUtc);

public sealed record GetReeferTemperatureReadingsRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}

public sealed record GetReeferTemperatureReadingsResponse(
    IReadOnlyList<ReeferTemperatureReadingListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record RecordReeferTemperatureReadingRequest
{
    public decimal? TemperatureCelsius { get; init; }

    public DateTime? MeasuredAtUtc { get; init; }

    public string? Notes { get; init; }
}

public sealed record CorrectReeferTemperatureReadingRequest
{
    public decimal? TemperatureCelsius { get; init; }

    public DateTime? MeasuredAtUtc { get; init; }

    public string? Notes { get; init; }

    public string? Reason { get; init; }
}

public sealed record VoidReeferTemperatureReadingRequest
{
    public string? Reason { get; init; }
}

public sealed record ReeferExcursionListItem(
    Guid Id,
    Guid ContainerId,
    Guid ReadingId,
    string Direction,
    decimal TemperatureCelsius,
    decimal ThresholdMinimumCelsius,
    decimal ThresholdMaximumCelsius,
    DateTime MeasuredAtUtc,
    DateTime DetectedAtUtc,
    string Status,
    string? Disposition,
    string? Explanation,
    Guid? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    Guid? SupersededByExcursionId,
    DateTime? SupersededAtUtc);

public sealed record RecordReeferTemperatureReadingResponse(
    ReeferTemperatureReadingListItem Reading,
    ReeferExcursionListItem? Excursion);

public sealed record GetReeferExcursionsResponse(
    IReadOnlyList<ReeferExcursionListItem> Items,
    int OpenCount);

public sealed record ResolveReeferExcursionRequest
{
    public string? Disposition { get; init; }

    public string? Explanation { get; init; }
}
