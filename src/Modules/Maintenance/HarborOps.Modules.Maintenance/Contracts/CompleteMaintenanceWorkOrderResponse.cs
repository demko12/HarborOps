using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CompleteMaintenanceWorkOrderResponse(
    Guid WorkOrderId,
    Guid MaintenanceRecordId,
    DateTime CompletedUtc);
