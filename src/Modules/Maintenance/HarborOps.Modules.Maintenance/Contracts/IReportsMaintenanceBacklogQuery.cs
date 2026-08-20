namespace HarborOps.Modules.Maintenance.Contracts;

public interface IReportsMaintenanceBacklogQuery
{
    Task<ReportsMaintenanceBacklogResult> SearchAsync(
        ReportsMaintenanceBacklogRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReportsMaintenanceBacklogRequest
{
    public required DateOnly AsOf { get; init; }
    public Guid? VesselId { get; init; }
    public string? Priority { get; init; }
    public string? Status { get; init; }
    public bool? Overdue { get; init; }
    public DateOnly? DueFrom { get; init; }
    public DateOnly? DueTo { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record ReportsMaintenanceBacklogResult(
    IReadOnlyList<ReportsMaintenanceBacklogRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record ReportsMaintenanceBacklogRow(
    Guid WorkOrderId,
    Guid VesselId,
    string Title,
    string Priority,
    string Status,
    DateTime DueUtc,
    bool IsOverdue,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);
