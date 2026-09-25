namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentReadinessItem(
    Guid RequirementId,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    int MinimumRemainingValidityDays,
    string? Instruction,
    DateOnly RequiredValidUntil,
    string Status,
    IReadOnlyList<string> Blockers,
    Guid? SelectedDocumentId,
    IReadOnlyList<DocumentReadinessCandidateItem> Candidates,
    DocumentReadinessDistribution? Distribution);
