using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

public sealed class MaintenanceBacklogReportService(IReportsMaintenanceBacklogQuery maintenanceBacklogQuery)
{
    public async Task<MaintenanceBacklogReportPage> GetPageAsync(
        DateOnly? asOf,
        Guid? vesselId,
        string? priority,
        string? status,
        bool? overdue,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var errors = MaintenanceBacklogReportValidator.Validate(
            asOf, pageNumber, pageSize, priority, status, dueFrom, dueTo);

        if (errors.Count > 0)
        {
            throw new MaintenanceBacklogReportValidationException(errors);
        }

        var result = await maintenanceBacklogQuery.SearchAsync(
            new ReportsMaintenanceBacklogRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                Priority = priority,
                Status = status,
                Overdue = overdue,
                DueFrom = dueFrom,
                DueTo = dueTo,
                PageNumber = pageNumber,
                PageSize = pageSize,
            },
            cancellationToken);

        return new MaintenanceBacklogReportPage(
            asOf.Value,
            result.Items.Select(Compose).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<(DateOnly AsOf, IReadOnlyList<MaintenanceBacklogReportRow> Rows)> GetExportRowsAsync(
        DateOnly? asOf,
        Guid? vesselId,
        string? priority,
        string? status,
        bool? overdue,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        CancellationToken cancellationToken)
    {
        var errors = MaintenanceBacklogReportValidator.Validate(
            asOf, pageNumber: 1, pageSize: 1, priority, status, dueFrom, dueTo);

        if (errors.Count > 0)
        {
            throw new MaintenanceBacklogReportValidationException(errors);
        }

        var result = await maintenanceBacklogQuery.SearchAsync(
            new ReportsMaintenanceBacklogRequest
            {
                AsOf = asOf!.Value,
                VesselId = vesselId,
                Priority = priority,
                Status = status,
                Overdue = overdue,
                DueFrom = dueFrom,
                DueTo = dueTo,
                PageNumber = 1,
                PageSize = MaintenanceBacklogReportValidator.MaxExportRows,
            },
            cancellationToken);

        return (asOf.Value, result.Items.Select(Compose).ToArray());
    }

    private static MaintenanceBacklogReportRow Compose(ReportsMaintenanceBacklogRow row) => new(
        row.WorkOrderId,
        row.VesselId,
        row.Title,
        row.Priority,
        row.Status,
        row.DueUtc,
        row.IsOverdue,
        row.AssignedEngineerId,
        row.CreatedUtc,
        row.CompletedUtc);
}
