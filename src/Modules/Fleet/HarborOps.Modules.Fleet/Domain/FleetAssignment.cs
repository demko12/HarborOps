namespace HarborOps.Modules.Fleet.Domain;

internal sealed class FleetAssignment
{
    private FleetAssignment()
    {
    }

    private FleetAssignment(
        Guid id,
        Guid vesselId,
        Guid fleetId,
        DateTime assignedFromUtc)
    {
        Id = id;
        VesselId = vesselId;
        FleetId = fleetId;
        AssignedFromUtc = assignedFromUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public Guid FleetId { get; private set; }

    public DateTime AssignedFromUtc { get; private set; }

    public DateTime? AssignedToUtc { get; private set; }

    public bool IsActive { get; private set; }

    internal static FleetAssignment Register(
        Guid vesselId,
        Guid fleetId,
        DateTime assignedFromUtc)
    {
        return new FleetAssignment(
            Guid.NewGuid(),
            vesselId,
            fleetId,
            assignedFromUtc);
    }

    internal static FleetAssignment Register(Guid vesselId, Guid fleetId) =>
        Register(vesselId, fleetId, DateTime.UtcNow);

    internal void End(DateTime assignedToUtc)
    {
        AssignedToUtc = assignedToUtc;
        IsActive = false;
    }

    internal void End() => End(DateTime.UtcNow);
}
