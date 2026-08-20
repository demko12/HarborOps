namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record MaintenanceScheduleEquipmentItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);
