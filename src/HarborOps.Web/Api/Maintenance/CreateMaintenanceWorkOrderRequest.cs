namespace HarborOps.Web.Api.Maintenance;

internal sealed record CreateMaintenanceWorkOrderRequest
{
    public Guid? VesselId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Priority { get; init; }

    public DateTime? DueUtc { get; init; }
}
