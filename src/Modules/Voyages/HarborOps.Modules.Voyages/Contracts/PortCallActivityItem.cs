namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallActivityItem(
    Guid ActivityId,
    int Sequence,
    string ActivityType,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    string Status,
    int Revision,
    bool IsLocked,
    Guid RecordedByUserId,
    DateTime RecordedAtUtc,
    Guid LastChangedByUserId,
    DateTime LastChangedAtUtc,
    Guid Version);
