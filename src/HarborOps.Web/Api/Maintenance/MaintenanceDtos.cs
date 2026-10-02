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

internal sealed record DryDockFilter
{
    internal Guid? VesselId { get; init; }

    internal Guid? PortId { get; init; }

    internal string? Status { get; init; }

    internal DateOnly? StartFrom { get; init; }

    internal DateOnly? StartTo { get; init; }

    internal bool? Overdue { get; init; }

    internal string? Search { get; init; }

    internal int PageNumber { get; init; } = 1;

    internal int PageSize { get; init; } = 25;
}

internal sealed record DryDocksPage(
    IReadOnlyList<DryDockRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record DryDockRow(
    Guid DryDockId,
    Guid VesselId,
    Guid PortId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PlannedTasks,
    decimal? ActualCost,
    string Status,
    bool IsOverdue,
    int? DaysUntilStart);

internal sealed record DryDockDetailsModel(
    Guid DryDockId,
    Guid VesselId,
    Guid PortId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string PlannedTasks,
    decimal? ActualCost,
    string Status,
    IReadOnlyList<string> AllowedNextStatuses,
    bool IsOverdue,
    int? DaysUntilStart);

internal sealed record ScheduleDryDockWebRequest(
    Guid VesselId,
    Guid PortId,
    DateOnly StartDate,
    string PlannedTasks);

internal sealed record ScheduledDryDock(Guid Id);

internal sealed record CompleteDryDockWebRequest(DateOnly EndDate, decimal ActualCost);

internal sealed record DryDockTransition(
    Guid DryDockId,
    string Status,
    IReadOnlyList<string> AllowedNextStatuses,
    DateOnly? EndDate,
    decimal? ActualCost);

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
    int? DaysUntilDelivery,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal QuantityClosed,
    decimal QuantityOutstanding);

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
    decimal LineTotal,
    decimal ReceivedQuantity,
    decimal LegacyReceivedQuantity,
    decimal ReturnedQuantity,
    decimal ClosedQuantity,
    decimal OutstandingQuantity,
    decimal ReturnableQuantity);

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
    IReadOnlyList<PurchaseOrderLineItem> Lines,
    Guid ConcurrencyToken,
    decimal TotalQuantityOrdered,
    decimal TotalQuantityReceived,
    decimal TotalQuantityReturned,
    decimal TotalQuantityClosed,
    decimal TotalQuantityOutstanding,
    int ReceiptCount,
    IReadOnlyList<string> AllowedActions);

internal sealed record PurchaseOrderStockAdjustment(
    Guid InventoryItemId,
    decimal QuantityReceived,
    decimal QuantityOnHand);

internal sealed record ChangePurchaseOrderStatusResult(
    Guid PurchaseOrderId,
    string PreviousStatus,
    string Status,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderStockAdjustment> StockAdjustments,
    PurchaseOrderReceiptModel? Receipt,
    bool AlreadyPosted);

internal sealed record PurchaseOrderReceiptLineEntry
{
    public Guid? PurchaseOrderLineId { get; init; }

    public decimal? Quantity { get; init; }
}

internal sealed record PreviewPurchaseOrderReceiptRequest
{
    public DateTime? ReceivedUtc { get; init; }

    public IReadOnlyList<PurchaseOrderReceiptLineEntry>? Lines { get; init; }
}

internal sealed record PostPurchaseOrderReceiptRequest
{
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public DateTime? ReceivedUtc { get; init; }

    public string? DeliveryReference { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<PurchaseOrderReceiptLineEntry>? Lines { get; init; }
}

internal sealed record ReceiveRemainingPurchaseOrderRequest
{
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public DateTime? ReceivedUtc { get; init; }

    public string? DeliveryReference { get; init; }

    public string? Notes { get; init; }
}

internal sealed record PurchaseOrderReceiptPreviewLineRow(
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity,
    decimal QuantityToReceive,
    decimal OutstandingQuantityAfter,
    decimal OnHandQuantity,
    decimal OnHandQuantityAfter);

internal sealed record PurchaseOrderReceiptPostingBlockerRow(
    Guid? PurchaseOrderLineId,
    string Code,
    string Message);

internal sealed record PurchaseOrderReceiptPostingPreviewModel(
    Guid PurchaseOrderId,
    string OrderStatus,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    string ResultingOrderStatus,
    IReadOnlyList<PurchaseOrderReceiptPreviewLineRow> Lines,
    IReadOnlyList<PurchaseOrderReceiptPostingBlockerRow> Blockers,
    DateTime GeneratedUtc);

internal sealed record PurchaseOrderReceiptLineRow(
    Guid ReceiptLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal Quantity,
    decimal LineReceivedQuantityAfter,
    decimal OutstandingQuantityAfter);

internal sealed record PurchaseOrderReceiptMovementRow(
    Guid MovementId,
    Guid InventoryItemId,
    Guid PurchaseOrderReceiptId,
    Guid PurchaseOrderReceiptLineId,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime MovedUtc);

internal sealed record PurchaseOrderReceiptModel(
    Guid ReceiptId,
    Guid PurchaseOrderId,
    Guid ClientRequestId,
    DateTime ReceivedUtc,
    string? DeliveryReference,
    string? Notes,
    Guid RecordedByUserId,
    DateTime RecordedUtc,
    string OrderStatusBefore,
    string OrderStatusAfter,
    decimal TotalQuantity,
    IReadOnlyList<PurchaseOrderReceiptLineRow> Lines,
    IReadOnlyList<PurchaseOrderReceiptMovementRow> Movements);

internal sealed record PostPurchaseOrderReceiptResult(
    PurchaseOrderReceiptModel Receipt,
    bool AlreadyPosted,
    string OrderStatus,
    Guid ConcurrencyToken,
    IReadOnlyList<string> AllowedActions);

internal sealed record PurchaseOrderReceiptsPage(
    IReadOnlyList<PurchaseOrderReceiptModel> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record ClosePurchaseOrderRequest
{
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public string? Reason { get; init; }
}

internal sealed record PurchaseOrderClosurePreviewLineRow(
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal ReturnedQuantity,
    decimal QuantityToClose);

internal sealed record PurchaseOrderClosureBlockerRow(
    Guid? PurchaseOrderLineId,
    string Code,
    string Message);

internal sealed record PurchaseOrderClosurePreviewModel(
    Guid PurchaseOrderId,
    string OrderStatus,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanClose,
    string ResultingOrderStatus,
    decimal TotalQuantityReceived,
    decimal TotalQuantityToClose,
    IReadOnlyList<PurchaseOrderClosurePreviewLineRow> Lines,
    IReadOnlyList<PurchaseOrderClosureBlockerRow> Blockers,
    DateTime GeneratedUtc);

internal sealed record PurchaseOrderClosureLineRow(
    Guid ClosureLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal ReturnedQuantity,
    decimal ClosedQuantity);

internal sealed record PurchaseOrderClosureModel(
    Guid ClosureId,
    Guid PurchaseOrderId,
    Guid ClientRequestId,
    string Reason,
    Guid ClosedByUserId,
    DateTime ClosedUtc,
    string OrderStatusBefore,
    string OrderStatusAfter,
    decimal TotalQuantityReceived,
    decimal TotalQuantityClosed,
    IReadOnlyList<PurchaseOrderClosureLineRow> Lines);

internal sealed record ClosePurchaseOrderResult(
    PurchaseOrderClosureModel Closure,
    bool AlreadyClosed,
    string OrderStatus,
    Guid ConcurrencyToken,
    IReadOnlyList<string> AllowedActions);

// A close conflict carries the server's refreshed preview, so the operator can confirm the current quantities.
internal sealed record ClosePurchaseOrderApiResult(
    ClosePurchaseOrderResult? Value,
    MaintenanceApiFailure? Failure,
    PurchaseOrderClosurePreviewModel? RefreshedPreview)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record WorkOrderEquipmentItem(
    Guid EquipmentId,
    Guid VesselId,
    string Name,
    string Category,
    string? Manufacturer,
    string? SerialNumber);

internal sealed record WorkOrderCompletionRecord(
    Guid MaintenanceRecordId,
    Guid TechnicianId,
    DateOnly CompletionDate,
    string? Notes,
    decimal? Cost,
    decimal? LaborHours);

internal sealed record MaintenanceWorkOrderDetailsModel(
    Guid WorkOrderId,
    Guid VesselId,
    Guid? DryDockId,
    Guid? AssignedEngineerId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime DueUtc,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    bool IsOverdue,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<WorkOrderEquipmentItem> Equipment,
    IReadOnlyList<WorkOrderPartUsageListItem> PartUsages,
    WorkOrderCompletionRecord? Completion);

internal sealed record ReplaceWorkOrderEquipmentWebRequest(IReadOnlyList<Guid> EquipmentIds);

internal sealed record ReplaceWorkOrderEquipmentResult(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<WorkOrderEquipmentItem> Equipment,
    DateTime UpdatedUtc);

internal sealed record MaintenanceWorkOrderTransition(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    DateTime? CompletedUtc,
    DateTime TransitionedUtc);

internal sealed record WorkOrderPartReservationItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal QuantityRequested,
    decimal QuantityReserved,
    decimal QuantityConsumed,
    decimal QuantityRemaining,
    decimal ShortageQuantity,
    bool HasShortage,
    decimal StockOnHand,
    decimal AvailableQuantity);

internal sealed record WorkOrderPartReservationsResult(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<WorkOrderPartReservationItem> Items,
    bool HasShortages,
    DateTime RetrievedUtc);

internal sealed record WorkOrderPartReservationEntry(Guid InventoryItemId, decimal Quantity);

internal sealed record ReplaceWorkOrderPartReservationsWebRequest(
    IReadOnlyList<WorkOrderPartReservationEntry> Reservations);

internal sealed record ReplaceWorkOrderPartReservationsResult(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<WorkOrderPartReservationItem> Items,
    bool HasShortages,
    DateTime UpdatedUtc);

internal sealed record ReleasedWorkOrderPartReservations(Guid WorkOrderId);

internal sealed record InventoryReplenishmentRow(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    Guid SupplierId,
    string SupplierName,
    decimal StockOnHand,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ReorderLevel,
    decimal ShortageQuantity,
    decimal OpenPurchaseOrderQuantity,
    string Classification);

internal sealed record InventoryReplenishmentPage(
    IReadOnlyList<InventoryReplenishmentRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record InventoryReplenishmentFilter(int PageNumber, int PageSize);

internal sealed record RegisterDefectRequest
{
    public Guid? VesselId { get; init; }

    public Guid? EquipmentId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? ReportedSeverity { get; init; }

    public DateTime? ObservedAtUtc { get; init; }
}

internal sealed record DefectListItem(
    Guid Id,
    Guid VesselId,
    Guid? EquipmentId,
    string Title,
    string ReportedSeverity,
    string? ConfirmedSeverity,
    string? Priority,
    string Status,
    DateTime ObservedAtUtc,
    DateTime ReportedAtUtc);

internal sealed record DefectsPage(
    IReadOnlyList<DefectListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record DefectFilter(
    Guid? VesselId,
    string? Status,
    string? Severity,
    int PageNumber,
    int PageSize);

internal sealed record DefectDetailsModel(
    Guid Id,
    Guid VesselId,
    Guid? EquipmentId,
    string Title,
    string? Description,
    string ReportedSeverity,
    DateTime ObservedAtUtc,
    string Status,
    Guid ReportedByUserId,
    DateTime ReportedAtUtc,
    string? ConfirmedSeverity,
    string? Priority,
    string? TriageNotes,
    Guid? TriagedByUserId,
    DateTime? TriagedAtUtc,
    string? RejectionReason,
    Guid? RejectedByUserId,
    DateTime? RejectedAtUtc,
    Guid? WorkOrderId,
    Guid? ConvertedByUserId,
    DateTime? ConvertedAtUtc);

internal sealed record TriageDefectRequest
{
    public string? ConfirmedSeverity { get; init; }

    public string? Priority { get; init; }

    public string? Notes { get; init; }
}

internal sealed record RejectDefectRequest
{
    public string? Reason { get; init; }
}

internal sealed record ConvertDefectToWorkOrderRequest
{
    public DateTime? DueUtc { get; init; }
}

internal sealed record ConvertDefectToWorkOrderResponse(Guid DefectId, Guid WorkOrderId);

internal sealed record PreventiveWorkForecastFilter(
    DateOnly From,
    DateOnly To,
    Guid? VesselId,
    string? State,
    int PageNumber,
    int PageSize);

internal sealed record PreventiveWorkOccurrenceItem(
    Guid ScheduleId,
    Guid? OccurrenceId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    bool IsOverdue,
    Guid? WorkOrderId,
    IReadOnlyList<Guid> EquipmentIds);

internal sealed record PreventiveWorkForecastPage(
    IReadOnlyList<PreventiveWorkOccurrenceItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int PendingCount,
    int GeneratedCount,
    int OverdueCount);

internal sealed record GeneratePreventiveWorkWebRequest
{
    public DateOnly? Through { get; init; }

    public Guid? VesselId { get; init; }

    public string? Priority { get; init; }
}

internal sealed record PreventiveWorkPreview(
    IReadOnlyList<PreventiveWorkOccurrenceItem> Items,
    DateOnly Through,
    int EligibleCount,
    int SkippedCount,
    DateTime PreviewedUtc);

internal sealed record PreventiveWorkGenerationItem(
    Guid ScheduleId,
    Guid OccurrenceId,
    Guid VesselId,
    string MaintenanceType,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    string Outcome,
    Guid WorkOrderId,
    IReadOnlyList<Guid> EquipmentIds);

internal sealed record PreventiveWorkGenerationResult(
    IReadOnlyList<PreventiveWorkGenerationItem> Results,
    DateOnly Through,
    int CreatedCount,
    int SkippedCount,
    DateTime GeneratedUtc);

internal sealed record PreventiveWorkReplacement(
    Guid OccurrenceId,
    Guid ScheduleId,
    Guid VesselId,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    Guid WorkOrderId,
    Guid? ReplacedWorkOrderId,
    IReadOnlyList<Guid> EquipmentIds,
    DateTime GeneratedUtc);

internal sealed record ScheduleOccurrenceItem(
    Guid OccurrenceId,
    Guid ScheduleId,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    Guid WorkOrderId,
    Guid? ReplacedWorkOrderId,
    DateTime GeneratedUtc,
    DateTime? ResolvedUtc);

internal sealed record ScheduleOccurrencesResult(
    Guid ScheduleId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    IReadOnlyList<ScheduleOccurrenceItem> Items);

internal sealed record ScheduleWorkOrderItem(
    Guid WorkOrderId,
    Guid OccurrenceId,
    Guid VesselId,
    DateOnly OccurrenceDueDate,
    string Title,
    string Status,
    string Priority,
    string OccurrenceState,
    DateTime DueUtc,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);

internal sealed record ScheduleWorkOrdersResult(
    Guid ScheduleId,
    IReadOnlyList<ScheduleWorkOrderItem> Items);

internal sealed record StocktakeSummary(
    Guid StocktakeId,
    string Status,
    int LineCount,
    int CountedLineCount,
    int VarianceLineCount,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    DateTime? PostedUtc,
    DateTime? CancelledUtc,
    Guid ConcurrencyToken);

internal sealed record StocktakesPage(
    IReadOnlyList<StocktakeSummary> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record StocktakeFilter(string? Status, int PageNumber, int PageSize);

internal sealed record StocktakeDetailsModel(
    Guid StocktakeId,
    string Status,
    string? Notes,
    Guid ConcurrencyToken,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    Guid? LastModifiedByUserId,
    DateTime? LastModifiedUtc,
    Guid? PostedByUserId,
    DateTime? PostedUtc,
    Guid? CancelledByUserId,
    DateTime? CancelledUtc,
    string? CancellationReason,
    IReadOnlyList<StocktakeLineRow> Lines,
    IReadOnlyList<InventoryAdjustmentRow> Adjustments);

internal sealed record StocktakeLineRow(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal BaselineQuantity,
    DateTime BaselineCapturedUtc,
    decimal? CountedQuantity,
    decimal? Variance,
    string? VarianceReason,
    Guid? CountedByUserId,
    DateTime? CountedUtc);

internal sealed record InventoryAdjustmentRow(
    Guid AdjustmentId,
    Guid InventoryItemId,
    Guid StocktakeId,
    decimal QuantityBefore,
    decimal CountedQuantity,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime AdjustedUtc);

internal sealed record StocktakePostingPreviewModel(
    Guid StocktakeId,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    IReadOnlyList<StocktakePreviewLineRow> Lines,
    IReadOnlyList<StocktakePostingBlockerRow> Blockers,
    DateTime GeneratedUtc);

internal sealed record StocktakePreviewLineRow(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal BaselineQuantity,
    decimal CurrentQuantity,
    decimal? CountedQuantity,
    decimal? Variance,
    decimal OutstandingReservedQuantity,
    bool BaselineIsStale);

internal sealed record StocktakePostingBlockerRow(
    Guid InventoryItemId,
    string Code,
    string Message,
    IReadOnlyList<Guid> ReservingWorkOrderIds);

internal sealed record PostStocktakeResult(
    Guid StocktakeId,
    string Status,
    Guid ConcurrencyToken,
    Guid PostedByUserId,
    DateTime PostedUtc,
    IReadOnlyList<InventoryAdjustmentRow> Adjustments);

internal sealed record CreateStocktakeRequest
{
    public IReadOnlyList<Guid>? InventoryItemIds { get; init; }

    public string? Notes { get; init; }
}

internal sealed record StocktakeCountEntry
{
    public Guid? InventoryItemId { get; init; }

    public decimal? CountedQuantity { get; init; }

    public string? Reason { get; init; }
}

internal sealed record UpdateStocktakeCountsRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public IReadOnlyList<StocktakeCountEntry>? Lines { get; init; }
}

internal sealed record RefreshStocktakeLinesRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public IReadOnlyList<Guid>? InventoryItemIds { get; init; }
}

internal sealed record PostStocktakeRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }
}

internal sealed record CancelStocktakeRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Reason { get; init; }
}

internal sealed record SupplierReturnSummary(
    Guid SupplierReturnId,
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    string Status,
    int LineCount,
    decimal TotalQuantity,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    DateTime? PostedUtc,
    DateTime? CancelledUtc,
    Guid ConcurrencyToken);

internal sealed record SupplierReturnsPage(
    IReadOnlyList<SupplierReturnSummary> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record SupplierReturnFilter(string? Status, Guid? PurchaseOrderId, int PageNumber, int PageSize);

internal sealed record SupplierReturnDetailsModel(
    Guid SupplierReturnId,
    Guid PurchaseOrderId,
    string PurchaseOrderStatus,
    Guid SupplierId,
    string SupplierName,
    string Status,
    string? Notes,
    Guid ConcurrencyToken,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    Guid? LastModifiedByUserId,
    DateTime? LastModifiedUtc,
    Guid? PostedByUserId,
    DateTime? PostedUtc,
    Guid? CancelledByUserId,
    DateTime? CancelledUtc,
    string? CancellationReason,
    IReadOnlyList<SupplierReturnLineRow> Lines,
    IReadOnlyList<SupplierReturnPostingBlockerRow> Blockers,
    IReadOnlyList<SupplierReturnMovementRow> Movements);

internal sealed record SupplierReturnLineRow(
    Guid SupplierReturnLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal ReceivedQuantity,
    decimal PreviouslyReturnedQuantity,
    decimal RemainingReturnableQuantity,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal Quantity,
    string Reason);

internal sealed record SupplierReturnMovementRow(
    Guid MovementId,
    Guid InventoryItemId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime MovedUtc);

internal sealed record SupplierReturnPostingBlockerRow(
    Guid? PurchaseOrderLineId,
    Guid? InventoryItemId,
    string Code,
    string Message,
    IReadOnlyList<Guid> ReservingWorkOrderIds);

internal sealed record SupplierReturnPreviewItemRow(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ReturnQuantity,
    decimal QuantityAfter);

internal sealed record SupplierReturnPostingPreviewModel(
    Guid SupplierReturnId,
    Guid ConcurrencyToken,
    string PreviewToken,
    bool CanPost,
    IReadOnlyList<SupplierReturnLineRow> Lines,
    IReadOnlyList<SupplierReturnPreviewItemRow> Items,
    IReadOnlyList<SupplierReturnPostingBlockerRow> Blockers,
    DateTime GeneratedUtc);

internal sealed record PostSupplierReturnResult(
    Guid SupplierReturnId,
    string Status,
    Guid ConcurrencyToken,
    Guid PostedByUserId,
    DateTime PostedUtc,
    bool AlreadyPosted,
    IReadOnlyList<SupplierReturnMovementRow> Movements);

internal sealed record SupplierReturnLineEntry
{
    public Guid? PurchaseOrderLineId { get; init; }

    public decimal? Quantity { get; init; }

    public string? Reason { get; init; }
}

internal sealed record CreateSupplierReturnRequest
{
    public Guid? PurchaseOrderId { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<SupplierReturnLineEntry>? Lines { get; init; }
}

internal sealed record UpdateSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<SupplierReturnLineEntry>? Lines { get; init; }
}

internal sealed record PostSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }
}

internal sealed record CancelSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Reason { get; init; }
}
