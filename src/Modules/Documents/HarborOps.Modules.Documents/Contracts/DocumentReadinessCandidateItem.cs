namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReadinessCandidateItem(
    Guid DocumentId,
    string Number,
    string Status,
    string ReviewStatus,
    bool HasFile,
    string RetentionState,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    bool Qualifies,
    IReadOnlyList<string> Blockers);
