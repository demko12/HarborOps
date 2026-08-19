namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetVesselComplianceOverviewResponse(
    IReadOnlyList<ComplianceCertificateOverviewItem> Certificates,
    IReadOnlyList<PscInspectionOverviewItem> PscInspections,
    IReadOnlyList<SurveyAuditOverviewItem> SurveyAudits,
    IReadOnlyList<OpenNonConformityOverviewItem> OpenNonConformities,
    IReadOnlyList<InsurancePolicyOverviewItem> InsurancePolicies);
