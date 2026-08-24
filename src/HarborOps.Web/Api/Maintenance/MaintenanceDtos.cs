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

internal sealed record EquipmentListItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);

internal sealed record EquipmentPage(
    IReadOnlyList<EquipmentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record EquipmentFilter(
    Guid? VesselId,
    string? Category,
    string? Manufacturer,
    string? Search,
    int PageNumber,
    int PageSize);

internal sealed record RegisterEquipmentRequest
{
    public Guid? VesselId { get; init; }

    public string? Name { get; init; }

    public string? Category { get; init; }

    public string? Manufacturer { get; init; }

    public string? SerialNumber { get; init; }
}

internal sealed record RegisteredEquipment(Guid Id);

internal sealed record MaintenanceScheduleListItem(
    Guid ScheduleId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours,
    bool IsOverdue,
    int? DaysUntilDue,
    int EquipmentCount);

internal sealed record MaintenanceSchedulesPage(
    IReadOnlyList<MaintenanceScheduleListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record MaintenanceScheduleFilter(
    Guid? VesselId,
    Guid? EquipmentId,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    bool? Overdue,
    int PageNumber,
    int PageSize);

internal sealed record RegisterMaintenanceScheduleRequest
{
    public string? MaintenanceType { get; init; }

    public string? Frequency { get; init; }

    public DateOnly? NextDueDate { get; init; }

    public int? NextDueHours { get; init; }

    public IReadOnlyList<Guid>? EquipmentIds { get; init; }
}

internal sealed record RegisteredMaintenanceSchedule(Guid Id);

internal sealed record MaintenanceScheduleEquipmentItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);

internal sealed record MaintenanceScheduleDetails(
    Guid ScheduleId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours,
    bool IsOverdue,
    int? DaysUntilDue,
    IReadOnlyList<MaintenanceScheduleEquipmentItem> Equipment);

internal sealed record UpdateScheduleNextDueRequest
{
    public DateOnly? CompletedOn { get; init; }

    public int? NextDueHours { get; init; }
}

internal sealed record UpdateScheduleNextDueResult(
    Guid ScheduleId,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours);

internal sealed record SupplierListItem(
    Guid SupplierId,
    string Name,
    string? ContactPerson,
    string? PhoneEmail,
    string? Address,
    string? PaymentTerms);

internal sealed record SuppliersPage(
    IReadOnlyList<SupplierListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record SupplierFilter(
    string? Search,
    int PageNumber,
    int PageSize);

internal sealed record RegisterSupplierRequest
{
    public string? Name { get; init; }

    public string? ContactPerson { get; init; }

    public string? PhoneEmail { get; init; }

    public string? Address { get; init; }

    public string? PaymentTerms { get; init; }
}

internal sealed record RegisteredSupplier(Guid SupplierId);

internal sealed record PurchaseOrderListItem(
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    DateOnly OrderDate,
    DateOnly? ExpectedDelivery,
    string Status,
    int LineCount,
    decimal TotalCost,
    bool IsOverdue,
    int? DaysUntilDelivery);

internal sealed record PurchaseOrdersPage(
    IReadOnlyList<PurchaseOrderListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record PurchaseOrderFilter(
    Guid? SupplierId,
    string? Status,
    DateOnly? OrderDateFrom,
    DateOnly? OrderDateTo,
    DateOnly? ExpectedDeliveryFrom,
    DateOnly? ExpectedDeliveryTo,
    bool? Overdue,
    int PageNumber,
    int PageSize);

internal sealed record CreatePurchaseOrderLineRequest
{
    public Guid? InventoryItemId { get; init; }

    public decimal? QuantityOrdered { get; init; }

    public decimal? UnitCost { get; init; }
}

internal sealed record CreatePurchaseOrderRequest
{
    public Guid? SupplierId { get; init; }

    public DateOnly? OrderDate { get; init; }

    public DateOnly? ExpectedDelivery { get; init; }

    public IReadOnlyList<CreatePurchaseOrderLineRequest>? Lines { get; init; }
}

internal sealed record CreatedPurchaseOrder(
    Guid PurchaseOrderId,
    string Status,
    int LineCount,
    decimal TotalCost);

internal sealed record PurchaseOrderLineItem(
    Guid LineId,
    Guid InventoryItemId,
    string InventoryItemName,
    string PartNumber,
    decimal QuantityOrdered,
    decimal UnitCost,
    decimal LineTotal);

internal sealed record PurchaseOrderDetails(
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    string? SupplierContactPerson,
    string? SupplierPhoneEmail,
    DateOnly OrderDate,
    DateOnly? ExpectedDelivery,
    string Status,
    bool IsOverdue,
    int? DaysUntilDelivery,
    decimal TotalCost,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderLineItem> Lines);

internal sealed record PurchaseOrderStockAdjustment(
    Guid InventoryItemId,
    decimal QuantityReceived,
    decimal QuantityOnHand);

internal sealed record ChangePurchaseOrderStatusResult(
    Guid PurchaseOrderId,
    string PreviousStatus,
    string Status,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderStockAdjustment> StockAdjustments);
