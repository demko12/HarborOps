namespace HarborOps.Web.Api.Compliance;

internal sealed record ComplianceOverview(
    IReadOnlyList<ComplianceCertificateItem> Certificates,
    IReadOnlyList<PscInspectionItem> PscInspections,
    IReadOnlyList<SurveyAuditItem> SurveyAudits,
    IReadOnlyList<OpenNonConformityItem> OpenNonConformities,
    IReadOnlyList<InsurancePolicyItem> InsurancePolicies);

internal sealed record ComplianceCertificateItem(Guid Id, Guid VesselId, string CertificateType,
    string CertificateNumber, string IssuingAuthority, DateOnly IssuedOn, DateOnly ExpiresOn,
    string Status, Guid? PredecessorCertificateId, DateTime? RevokedAtUtc, Guid? SuccessorCertificateId,
    string? SuccessorCertificateNumber, DateOnly? SuccessorIssuedOn, bool SuccessorIsRevoked,
    bool IsRenewalCovered, int? RenewalGapDays);

internal sealed record ComplianceCertificateChainItem(Guid Id, string CertificateNumber, string IssuingAuthority,
    DateOnly IssuedOn, DateOnly ExpiresOn, string Status, DateTime? RevokedAtUtc, Guid? PredecessorCertificateId,
    int? GapDaysAfterPredecessor, bool CoversPredecessorExpiry);

internal sealed record ComplianceCertificateHistoryItem(Guid Id, Guid CertificateId, string EventType,
    Guid ActorUserId, DateTime OccurredAtUtc, string? Reason, Guid? SuccessorCertificateId);

internal sealed record ComplianceCertificateDetail(Guid Id, Guid VesselId, string CertificateType,
    string CertificateNumber, string IssuingAuthority, DateOnly IssuedOn, DateOnly ExpiresOn, string Status,
    DateTime CreatedUtc, Guid? PredecessorCertificateId, Guid? SuccessorCertificateId, DateTime? RevokedAtUtc,
    Guid? RevokedByUserId, string? RevocationReason, bool CanRenew, bool CanRevoke, Guid ConcurrencyToken,
    IReadOnlyList<ComplianceCertificateChainItem> Chain, IReadOnlyList<ComplianceCertificateHistoryItem> History);

internal sealed record RenewComplianceCertificateRequest(Guid? ExpectedConcurrencyToken, string? CertificateNumber,
    string? IssuingAuthority, DateOnly? IssuedOn, DateOnly? ExpiresOn);

internal sealed record RenewComplianceCertificateResult(Guid Id, Guid PredecessorCertificateId);

internal sealed record RevokeComplianceCertificateRequest(Guid? ExpectedConcurrencyToken, string? Reason);

internal sealed record RevokeComplianceCertificateResult(Guid Id, DateTime RevokedAtUtc, Guid RevokedByUserId);

internal sealed record PscInspectionItem(int Id, Guid VesselId, Guid PortId, DateOnly InspectionDate,
    string InspectingAuthority, int DeficiencyCount, bool VesselDetained, string? DetentionState = null);

internal sealed record SurveyAuditItem(int Id, Guid VesselId, DateOnly Date, string Type, string Result);

internal sealed record OpenNonConformityItem(int Id, Guid VesselId, Guid EvidenceDocumentId,
    string Severity, DateOnly CorrectiveActionDeadline, string ClosureStatus);

internal sealed record RegisterComplianceCertificateRequest(Guid? VesselId, string? CertificateType,
    string? CertificateNumber, string? IssuingAuthority, DateOnly? IssuedOn, DateOnly? ExpiresOn);

internal sealed record RegisterPscInspectionRequest(Guid? VesselId, Guid? PortId,
    DateOnly? InspectionDate, string? InspectingAuthority, int? DeficiencyCount, bool? VesselDetained);

internal sealed record RegisterNonConformityRequest(
    int? SurveyId,
    int? PscId,
    Guid? EvidenceDocumentId,
    string? Severity,
    DateOnly? CorrectiveActionDeadline);

internal sealed record RegisterComplianceCertificateResult(Guid Id);

internal sealed record RegisterPscInspectionResult(int Id);

internal sealed record RegisterPscDeficiencyRequest(string? DeficiencyCode, string? Description, string? Severity, DateOnly? CorrectiveActionDeadline, Guid? EvidenceDocumentId);
internal sealed record RegisterPscDeficiencyResult(int NonConformityId);
internal sealed record DetainPscInspectionRequest(string? AuthorityReference, string? Reason);
internal sealed record ReleasePscInspectionRequest(string? AuthorityReference, DateOnly? ReleaseDate, Guid? DocumentId);
internal sealed record PscDetentionDecisionItem(Guid Id, string Decision, string AuthorityReference, string? Reason, DateOnly? ReleaseDate, Guid? DocumentId, Guid ActorUserId, DateTimeOffset OccurredAtUtc);
internal sealed record PscDeficiencyItem(int NonConformityId, string DeficiencyCode, string Description, string Severity, DateOnly CorrectiveActionDeadline, string ClosureStatus, Guid EvidenceDocumentId, int CorrectiveActionCount);
internal sealed record GetPscDeficienciesResponse(IReadOnlyList<PscDeficiencyItem> Items);
internal sealed record GetPscInspectionDetailResponse(int Id, Guid VesselId, Guid PortId, DateOnly InspectionDate, string InspectingAuthority, int ReportedDeficiencyCount, int RegisteredDeficiencyCount, int OpenDeficiencyCount, int ClosedDeficiencyCount, bool HasDeficiencyCountMismatch, string DetentionState, PscDetentionDecisionItem? LatestDecision, IReadOnlyList<PscDetentionDecisionItem> DetentionHistory);

internal sealed record RegisterNonConformityResult(int Id);

internal sealed record RegisterSurveyAuditRequest(Guid? VesselId, DateOnly? SurveyDate, string? Type, string? InspectorName, string? Result, string? Findings, Guid? DocumentId);
internal sealed record RegisterSurveyAuditResult(int Id);
internal sealed record SurveyAuditList(IReadOnlyList<SurveyAuditListItem> Items, int TotalCount, int PageNumber, int PageSize);
internal sealed record SurveyAuditListItem(int Id, Guid VesselId, DateOnly SurveyDate, string Type, string? InspectorName, string Result, Guid? DocumentId);
internal sealed record SurveyAuditDetails(int Id, Guid VesselId, DateOnly SurveyDate, string Type, string? InspectorName, string Result, string Findings, Guid? DocumentId);

internal sealed record RegisterInsurancePolicyRequest(Guid? VesselId, string? Insurer, string? PolicyNumber, string? Coverage, DateOnly? ExpiryDate);
internal sealed record RegisterInsurancePolicyResult(int Id);
internal sealed record InsurancePolicyItem(int Id, Guid VesselId, string Insurer, string PolicyNumber, string Coverage, DateOnly ExpiryDate, string ExpiryClassification);
internal sealed record InsurancePoliciesPage(IReadOnlyList<InsurancePolicyItem> Items, int TotalCount, int PageNumber, int PageSize);

internal sealed record GetNonConformitiesRequest
{
    public string? Search { get; init; }

    public string? Severity { get; init; }

    public string? ClosureStatus { get; init; }

    public bool? Overdue { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

internal sealed record NonConformityListItem(
    int Id,
    Guid VesselId,
    int? SurveyId,
    int? PscId,
    string Severity,
    string ClosureStatus,
    DateOnly CorrectiveActionDeadline,
    bool IsOverdue,
    Guid EvidenceDocumentId,
    int CorrectiveActionCount);

internal sealed record GetNonConformitiesResponse(
    IReadOnlyList<NonConformityListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

internal sealed record NonConformitiesSummary(
    int UnresolvedCriticalCount,
    int UnresolvedMajorCount,
    int UnresolvedMinorCount,
    int OverdueCount);

internal sealed record CorrectiveActionItem(
    Guid Id,
    string ActionText,
    Guid ActorUserId,
    DateTimeOffset RecordedAtUtc,
    Guid? EvidenceDocumentId);

internal sealed record GetNonConformityDetailResponse(
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

internal sealed record RecordCorrectiveActionRequest(string? ActionText, Guid? EvidenceDocumentId);
internal sealed record RecordCorrectiveActionResult(Guid Id, string ClosureStatus);

internal sealed record CloseNonConformityRequest(string? ClosureNotes, Guid? FinalEvidenceDocumentId);
internal sealed record CloseNonConformityResult(int Id, string ClosureStatus, DateTimeOffset ClosedAtUtc);

internal sealed record ComplianceApiResult<T>(T? Value, ComplianceApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record ComplianceApiFailure(int StatusCode, string Title, string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;
    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;
    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
}

internal sealed record GetAuditPlansRequest
{
    public Guid? VesselId { get; init; }

    public string? Type { get; init; }

    public string? Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

internal sealed record AuditPlanListItem(
    Guid Id,
    Guid VesselId,
    string Type,
    string Status,
    DateOnly PlannedStartDate,
    DateOnly DueDate,
    Guid? AssignedInspectorUserId,
    bool IsOverdue);

internal sealed record GetAuditPlansResponse(
    IReadOnlyList<AuditPlanListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

internal sealed record AuditPlanHistoryItem(
    Guid Id,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string PreviousStatus,
    string NewStatus,
    DateOnly? PreviousPlannedStartDate,
    DateOnly? NewPlannedStartDate,
    DateOnly? PreviousDueDate,
    DateOnly? NewDueDate,
    Guid? PreviousInspectorUserId,
    Guid? NewInspectorUserId);

internal sealed record GetAuditPlanDetailResponse(
    Guid Id,
    Guid VesselId,
    string Type,
    string Status,
    DateOnly PlannedStartDate,
    DateOnly DueDate,
    Guid? AssignedInspectorUserId,
    string Scope,
    string? Notes,
    bool IsOverdue,
    int? SurveyAuditId,
    IReadOnlyList<AuditPlanHistoryItem> History);

internal sealed record RegisterAuditPlanRequest
{
    public Guid? VesselId { get; init; }

    public string? Type { get; init; }

    public DateOnly? PlannedStartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? AssignedInspectorUserId { get; init; }

    public string? Scope { get; init; }

    public string? Notes { get; init; }
}

internal sealed record RegisterAuditPlanResponse(Guid Id);

internal sealed record RescheduleAuditPlanRequest
{
    public DateOnly? PlannedStartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public string? Reason { get; init; }
}

internal sealed record RescheduleAuditPlanResponse(Guid Id, DateOnly PlannedStartDate, DateOnly DueDate);

internal sealed record AssignAuditPlanInspectorRequest
{
    public Guid? InspectorUserId { get; init; }

    public string? Reason { get; init; }
}

internal sealed record AssignAuditPlanInspectorResponse(Guid Id, Guid InspectorUserId);

internal sealed record StartAuditPlanResponse(Guid Id, string Status, DateTime StartedAtUtc);

internal sealed record CompleteAuditPlanRequest
{
    public string? Result { get; init; }

    public string? Findings { get; init; }

    public Guid? DocumentId { get; init; }

    public DateOnly? ActualAuditDate { get; init; }
}

internal sealed record CompleteAuditPlanResponse(Guid Id, int SurveyAuditId, DateTime CompletedAtUtc);

internal sealed record CancelAuditPlanRequest(string? Reason);

internal sealed record CancelAuditPlanResponse(Guid Id, string Status, DateTime CancelledAtUtc);
