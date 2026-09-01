namespace HarborOps.Web.Api.Compliance;

internal sealed record ComplianceOverview(
    IReadOnlyList<ComplianceCertificateItem> Certificates,
    IReadOnlyList<PscInspectionItem> PscInspections,
    IReadOnlyList<SurveyAuditItem> SurveyAudits,
    IReadOnlyList<OpenNonConformityItem> OpenNonConformities,
    IReadOnlyList<InsurancePolicyItem> InsurancePolicies);

internal sealed record ComplianceCertificateItem(Guid Id, Guid VesselId, string CertificateType,
    string CertificateNumber, string IssuingAuthority, DateOnly IssuedOn, DateOnly ExpiresOn);

internal sealed record PscInspectionItem(int Id, Guid VesselId, Guid PortId, DateOnly InspectionDate,
    string InspectingAuthority, int DeficiencyCount, bool VesselDetained);

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
}
