namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReadinessDistribution(
    string Status,
    Guid? IssueId,
    DateTime? IssuedAtUtc,
    DateOnly? DueDate,
    int PendingCount,
    int AcknowledgedCount,
    int OverdueCount,
    IReadOnlyList<DocumentReadinessRecipient>? Recipients);
