using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record WorkOrderPartUsageListItem(
    Guid PartId,
    string Name,
    string PartNumber,
    decimal QuantityUsed,
    decimal RemainingQuantity);
