namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReadinessRecipient(
    Guid UserId,
    string? Email,
    string State,
    bool IsOverdue,
    DateTime? AcknowledgedAtUtc,
    DateTime? StateChangedAtUtc,
    string? StateReason);
