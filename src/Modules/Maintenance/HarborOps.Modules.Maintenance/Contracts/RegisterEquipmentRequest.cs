namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RegisterEquipmentRequest
{
    public Guid? VesselId { get; init; }

    public string? Name { get; init; }

    public string? Category { get; init; }

    public string? Manufacturer { get; init; }

    public string? SerialNumber { get; init; }
}
