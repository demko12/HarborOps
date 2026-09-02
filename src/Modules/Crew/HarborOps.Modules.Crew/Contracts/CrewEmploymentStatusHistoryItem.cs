namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewEmploymentStatusHistoryItem(
    Guid Id,
    string Status,
    Guid ChangedByUserId,
    string? Notes,
    DateTime ChangedAtUtc);
