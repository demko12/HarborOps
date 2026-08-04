namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RecordVesselStatusChangeRequest
{
    public Guid? VesselId { get; init; }

    public string? Status { get; init; }

    public string? ChangedBy { get; init; }

    public string? Notes { get; init; }
}
