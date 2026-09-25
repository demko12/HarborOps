namespace HarborOps.Modules.Documents.Contracts;

public sealed record VesselReadinessOverviewItem(
    Guid VesselId,
    string VesselName,
    string Status,
    DocumentReadinessSummary Summary);
