namespace HarborOps.Modules.Compliance.Contracts;

public sealed record OpenNonConformityOverviewItem(
    int Id,
    Guid VesselId,
    Guid EvidenceDocumentId,
    string Severity,
    DateOnly CorrectiveActionDeadline,
    string ClosureStatus);
