namespace HarborOps.Api.Composition.Operations;

public sealed record FleetVesselOverviewRow(
    Guid Id,
    string Name,
    string ImoNumber,
    string FlagState,
    string CurrentStatus,
    string? LifecycleStage,
    Guid VesselTypeId,
    string VesselTypeName,
    Guid? FleetId,
    string? FleetName,
    int OpenWorkOrderCount,
    int OverdueWorkOrderCount,
    int CriticalOpenWorkOrderCount,
    DateTime? NextWorkOrderDueUtc);
