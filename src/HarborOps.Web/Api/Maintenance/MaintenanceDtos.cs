namespace HarborOps.Web.Api.Maintenance;

internal sealed record MaintenanceWorkOrderListItem(
    Guid WorkOrderId,
    Guid VesselId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime DueUtc,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);

internal sealed record MaintenanceWorkOrdersPage(
    IReadOnlyList<MaintenanceWorkOrderListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record MaintenanceWorkOrderFilter(
    Guid? VesselId,
    string? Status,
    string? Priority,
    bool? Overdue,
    int PageNumber,
    int PageSize);

internal sealed record CreateMaintenanceWorkOrderRequest
{
    public Guid? VesselId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Priority { get; init; }

    public DateTime? DueUtc { get; init; }
}

internal sealed record CompleteMaintenanceWorkOrderRequest
{
    public Guid? TechnicianId { get; init; }

    public DateTime? CompletedUtc { get; init; }

    public string? Notes { get; init; }

    public decimal? Cost { get; init; }

    public decimal? LaborHours { get; init; }
}

internal sealed record CompleteMaintenanceWorkOrderApiResult(
    Guid WorkOrderId,
    Guid MaintenanceRecordId,
    DateTime CompletedUtc);

internal sealed record MaintenanceApiResult<T>(T? Value, MaintenanceApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record MaintenanceApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;

    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;

    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;

    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
}

internal sealed record CreatedMaintenanceWorkOrder(Guid Id);

internal sealed record InventoryItemListItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal Quantity,
    decimal MinimumQuantity,
    decimal UnitCost,
    Guid CategoryId,
    Guid UnitId,
    Guid SupplierId);

internal sealed record InventoryItemsPage(
    IReadOnlyList<InventoryItemListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record InventoryItemFilter(
    string? Search,
    bool? LowStock,
    int PageNumber,
    int PageSize);

internal sealed record RecordPartUsageRequest
{
    public decimal? QuantityUsed { get; init; }
}

internal sealed record RecordPartUsageResult(
    Guid WorkOrderId,
    Guid PartId,
    decimal QuantityUsed,
    decimal RemainingQuantity);

internal sealed record WorkOrderPartUsageListItem(
    Guid PartId,
    string Name,
    string PartNumber,
    decimal QuantityUsed,
    decimal RemainingQuantity);

internal sealed record WorkOrderPartUsagesResult(
    IReadOnlyList<WorkOrderPartUsageListItem> Items);
