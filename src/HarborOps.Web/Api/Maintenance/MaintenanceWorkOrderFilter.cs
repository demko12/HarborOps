namespace HarborOps.Web.Api.Maintenance;

internal sealed record MaintenanceWorkOrderFilter(
    Guid? VesselId,
    string? Status,
    string? Priority,
    bool? Overdue,
    int PageNumber,
    int PageSize);
