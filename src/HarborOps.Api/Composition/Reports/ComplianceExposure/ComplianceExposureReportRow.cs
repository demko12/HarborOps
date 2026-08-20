namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

public sealed record ComplianceExposureReportRow(
    string ItemType,
    Guid VesselId,
    string Reference,
    string Description,
    DateOnly RelevantDate,
    string Status,
    string? Severity);
