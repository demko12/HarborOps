namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record DryDockListItem(
    Guid DryDockId,
    Guid VesselId,
    Guid PortId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PlannedTasks,
    decimal? ActualCost,
    string Status,
    bool IsOverdue,
    int? DaysUntilStart);
