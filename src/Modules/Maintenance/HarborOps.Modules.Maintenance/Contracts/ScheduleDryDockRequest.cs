namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ScheduleDryDockRequest
{
    public Guid? VesselId { get; init; }

    public Guid? PortId { get; init; }

    public DateOnly? StartDate { get; init; }

    public string? PlannedTasks { get; init; }
}
