namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record EquipmentListItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);
