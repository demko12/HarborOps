namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentRequirementDetailsResponse(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    int Revision,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc,
    Guid Version,
    IReadOnlyList<DocumentRequirementHistoryItem> History);
