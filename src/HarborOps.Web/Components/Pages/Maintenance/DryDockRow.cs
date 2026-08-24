namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record DryDockBoardRow(
    Guid DryDockId,
    Guid VesselId,
    string VesselDisplayName,
    Guid PortId,
    string PortDisplayName,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PlannedTasks,
    decimal? ActualCost,
    string Status,
    bool IsOverdue,
    int? DaysUntilStart);
