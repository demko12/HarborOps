namespace HarborOps.Web.Api.Compliance;

internal sealed record ComplianceOverview(
    IReadOnlyList<ComplianceCertificateItem> Certificates,
    IReadOnlyList<PscInspectionItem> PscInspections,
    IReadOnlyList<OpenNonConformityItem> OpenNonConformities);

internal sealed record ComplianceCertificateItem(Guid Id, Guid VesselId, string CertificateType,
    string CertificateNumber, string IssuingAuthority, DateOnly IssuedOn, DateOnly ExpiresOn);

internal sealed record PscInspectionItem(int Id, Guid VesselId, Guid PortId, DateOnly InspectionDate,
    string InspectingAuthority, int DeficiencyCount, bool VesselDetained);

internal sealed record OpenNonConformityItem(int Id, Guid VesselId, Guid EvidenceDocumentId,
    string Severity, DateOnly CorrectiveActionDeadline, string ClosureStatus);

internal sealed record RegisterComplianceCertificateRequest(Guid? VesselId, string? CertificateType,
    string? CertificateNumber, string? IssuingAuthority, DateOnly? IssuedOn, DateOnly? ExpiresOn);

internal sealed record RegisterPscInspectionRequest(Guid? VesselId, Guid? PortId,
    DateOnly? InspectionDate, string? InspectingAuthority, int? DeficiencyCount, bool? VesselDetained);

internal sealed record RegisterComplianceCertificateResult(Guid Id);

internal sealed record RegisterPscInspectionResult(int Id);

internal sealed record ComplianceApiResult<T>(T? Value, ComplianceApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record ComplianceApiFailure(int StatusCode, string Title, string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;
}
