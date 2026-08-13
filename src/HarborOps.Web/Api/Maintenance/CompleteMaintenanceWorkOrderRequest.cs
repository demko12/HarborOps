namespace HarborOps.Web.Api.Maintenance;

internal sealed record CompleteMaintenanceWorkOrderRequest
{
    public Guid? TechnicianId { get; init; }

    public DateTime? CompletedUtc { get; init; }

    public string? Notes { get; init; }

    public decimal? Cost { get; init; }

    public decimal? LaborHours { get; init; }
}
