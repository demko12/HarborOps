namespace HarborOps.Modules.Compliance.Contracts;

public sealed record CloseNonConformityRequest(
    string? ClosureNotes,
    Guid? FinalEvidenceDocumentId);

public sealed record CloseNonConformityResponse(
    int Id,
    string ClosureStatus,
    DateTimeOffset ClosedAtUtc);
