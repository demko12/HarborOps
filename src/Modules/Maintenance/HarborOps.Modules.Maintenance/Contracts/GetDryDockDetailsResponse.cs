namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetDryDockDetailsResponse(
    Guid DryDockId,
    Guid VesselId,
    Guid PortId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PlannedTasks,
    decimal? ActualCost,
    string Status,
    IReadOnlyList<string> AllowedNextStatuses,
    bool IsOverdue,
    int? DaysUntilStart);
