namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetVesselComplianceOverviewResponse(
    IReadOnlyList<ComplianceCertificateOverviewItem> Certificates,
    IReadOnlyList<PscInspectionOverviewItem> PscInspections,
    IReadOnlyList<OpenNonConformityOverviewItem> OpenNonConformities);
