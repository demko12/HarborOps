namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

public sealed record MaintenanceBacklogReportPage(
    DateOnly AsOf,
    IReadOnlyList<MaintenanceBacklogReportRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
