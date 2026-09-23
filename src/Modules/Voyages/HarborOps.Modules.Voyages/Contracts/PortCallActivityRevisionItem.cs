namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallActivityRevisionItem(
    int Revision,
    string ChangeType,
    string ActivityType,
    DateTime OccurredAtUtc,
    DateTime? EndedAtUtc,
    string? Notes,
    string? Reason,
    Guid ActorUserId,
    string ActorName,
    DateTime ChangedAtUtc);
