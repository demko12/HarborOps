namespace HarborOps.Modules.Compliance.Contracts;

public sealed record PscInspectionOverviewItem(
    int Id,
    Guid VesselId,
    Guid PortId,
    DateOnly InspectionDate,
    string InspectingAuthority,
    int DeficiencyCount,
    bool VesselDetained,
    string DetentionState);
