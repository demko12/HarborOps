namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallLogEntryItem(
    int Position,
    string Source,
    Guid? SourceId,
    int? SourceRevision,
    string Label,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    int? DurationMinutes,
    bool IsOutsidePortCall);
