using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record MaintenanceWorkOrderTransitionResponse(
    Guid WorkOrderId,
    string Status,
    IReadOnlyList<string> AllowedActions,
    DateTime? CompletedUtc,
    DateTime TransitionedUtc);
