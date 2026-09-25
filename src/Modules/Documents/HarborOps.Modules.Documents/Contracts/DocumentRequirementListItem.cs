namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentRequirementListItem(
    Guid Id,
    Guid VesselId,
    int DocumentTypeId,
    string DocumentTypeName,
    bool IsEnabled,
    int MinimumRemainingValidityDays,
    string? Instruction,
    int Revision,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc,
    Guid Version);
