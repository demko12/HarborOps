namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ConvertDefectToWorkOrderRequest
{
    public DateTime? DueUtc { get; init; }
}
