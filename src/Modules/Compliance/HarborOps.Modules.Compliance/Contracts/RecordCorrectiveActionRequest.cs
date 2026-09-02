namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RecordCorrectiveActionRequest(
    string? ActionText,
    Guid? EvidenceDocumentId);

public sealed record RecordCorrectiveActionResponse(
    Guid Id,
    string ClosureStatus);
