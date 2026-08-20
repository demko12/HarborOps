namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetEquipmentRequest
{
    public Guid? VesselId { get; init; }

    public string? Category { get; init; }

    public string? Manufacturer { get; init; }

    public string? Search { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
