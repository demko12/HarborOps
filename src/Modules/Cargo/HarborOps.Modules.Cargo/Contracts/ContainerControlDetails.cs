namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ContainerControlDetails(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    bool IsReefer,
    string? SealNumber,
    string SealStatus,
    string State,
    string? LatestInspectionResult,
    string? LatestInspectionNotes,
    decimal? LatestInspectionTemperatureCelsius,
    DateTime? LatestInspectedAtUtc,
    Guid? LatestInspectedByUserId,
    string? StateReason,
    Guid? StateChangedByUserId,
    DateTime? StateChangedAtUtc,
    bool CanRelease,
    string? ReleaseBlockedReason);
