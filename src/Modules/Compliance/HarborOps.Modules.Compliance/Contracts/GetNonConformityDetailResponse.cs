namespace HarborOps.Modules.Compliance.Contracts;

public sealed record CorrectiveActionItem(
    Guid Id,
    string ActionText,
    Guid ActorUserId,
    DateTimeOffset RecordedAtUtc,
    Guid? EvidenceDocumentId);

public sealed record GetNonConformityDetailResponse(
    int Id,
    Guid VesselId,
    int? SurveyId,
    int? PscId,
    string Severity,
    string ClosureStatus,
    DateOnly CorrectiveActionDeadline,
    bool IsOverdue,
    Guid EvidenceDocumentId,
    string? ClosureNotes,
    DateTimeOffset? ClosedAtUtc,
    Guid? ClosedByUserId,
    Guid? FinalEvidenceDocumentId,
    IReadOnlyList<CorrectiveActionItem> CorrectiveActions);
