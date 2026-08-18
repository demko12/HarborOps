namespace HarborOps.Web.Api.Compliance;

internal sealed record ComplianceOverview(
    IReadOnlyList<ComplianceCertificateItem> Certificates,
    IReadOnlyList<PscInspectionItem> PscInspections,
    IReadOnlyList<SurveyAuditItem> SurveyAudits,
    IReadOnlyList<OpenNonConformityItem> OpenNonConformities);

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

internal sealed record ComplianceApiResult<T>(T? Value, ComplianceApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record ComplianceApiFailure(int StatusCode, string Title, string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;
}
