namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RegisterFleetAssignmentRequest
{
    public Guid? VesselId { get; init; }

    public Guid? FleetId { get; init; }
}
