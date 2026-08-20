namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record EquipmentRow(
    Guid EquipmentId,
    Guid VesselId,
    string VesselDisplayName,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);
