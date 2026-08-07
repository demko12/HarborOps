namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselListItem(
    Guid Id,
    string Name,
    string ImoNumber,
    string FlagState,
    string CurrentStatus,
    string? LifecycleStage,
    Guid VesselTypeId,
    string VesselTypeName,
    Guid? FleetId,
    string? FleetName);
