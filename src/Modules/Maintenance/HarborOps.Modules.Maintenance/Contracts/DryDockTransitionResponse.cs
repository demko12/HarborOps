namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record DryDockTransitionResponse(
    Guid DryDockId,
    string Status,
    IReadOnlyList<string> AllowedNextStatuses,
    DateOnly? EndDate,
    decimal? ActualCost);
