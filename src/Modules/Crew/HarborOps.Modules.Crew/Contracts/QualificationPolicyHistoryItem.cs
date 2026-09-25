namespace HarborOps.Modules.Crew.Contracts;

public sealed record QualificationPolicyHistoryItem(
    Guid Id,
    string EventType,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    bool? PreviousEnabled,
    bool? NewEnabled,
    IReadOnlyList<string>? PreviousRequiredTypes,
    IReadOnlyList<string>? NewRequiredTypes);
