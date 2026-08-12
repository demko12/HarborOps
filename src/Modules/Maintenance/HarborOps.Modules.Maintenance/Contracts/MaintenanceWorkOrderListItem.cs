using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record MaintenanceWorkOrderListItem(
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
