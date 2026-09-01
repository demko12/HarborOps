using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceWorkOrderDetailsResponse(
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
    IReadOnlyList<WorkOrderEquipmentListItem> Equipment,
    IReadOnlyList<WorkOrderPartUsageListItem> PartUsages,
    WorkOrderCompletionRecordItem? Completion);
