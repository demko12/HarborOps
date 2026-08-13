namespace HarborOps.Web.Api.Maintenance;

internal sealed record MaintenanceWorkOrdersPage(
    IReadOnlyList<MaintenanceWorkOrderListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
