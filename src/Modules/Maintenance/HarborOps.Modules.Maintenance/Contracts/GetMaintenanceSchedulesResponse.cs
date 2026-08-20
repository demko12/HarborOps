namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceSchedulesResponse(
    IReadOnlyList<MaintenanceScheduleListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
