using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ReplaceWorkOrderEquipmentResponse(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<WorkOrderEquipmentListItem> Equipment,
    DateTime UpdatedUtc);
