namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RecordVesselStatusChangeRequest
{

    public string? Status { get; init; }

    public string? Notes { get; init; }
}
