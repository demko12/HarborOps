namespace HarborOps.Modules.Voyages.Domain;

internal sealed record PortCallLogEntry(
    string Source,
    Guid? SourceId,
    int? SourceRevision,
    int SourceSequence,
    string Label,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    int? DurationMinutes);
