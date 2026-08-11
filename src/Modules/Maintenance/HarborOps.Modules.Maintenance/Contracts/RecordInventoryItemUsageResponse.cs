using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RecordInventoryItemUsageResponse(
    Guid WorkOrderId,
    Guid PartId,
    decimal QuantityUsed,
    decimal RemainingQuantity);
