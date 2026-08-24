namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CompleteDryDockRequest
{
    public DateOnly? EndDate { get; init; }

    public decimal? ActualCost { get; init; }
}
