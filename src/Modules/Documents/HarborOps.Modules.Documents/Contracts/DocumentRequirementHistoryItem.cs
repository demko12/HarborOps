namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentRequirementHistoryItem(
    Guid Id,
    int Revision,
    string EventType,
    string? Reason,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    Guid ActorUserId,
    string? ActorEmail,
    DateTime OccurredAtUtc);
