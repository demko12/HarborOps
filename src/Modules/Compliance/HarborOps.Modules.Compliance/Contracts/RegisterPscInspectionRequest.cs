namespace HarborOps.Modules.Compliance.Contracts;

public sealed record RegisterPscInspectionRequest
{
    public Guid? VesselId { get; init; }

    public Guid? PortId { get; init; }

    public DateOnly? InspectionDate { get; init; }

    public string? InspectingAuthority { get; init; }

    public int? DeficiencyCount { get; init; }

    public bool? VesselDetained { get; init; }
}
