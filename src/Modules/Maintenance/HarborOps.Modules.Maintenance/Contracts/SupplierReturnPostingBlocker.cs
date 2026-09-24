using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnPostingBlocker(
    Guid? PurchaseOrderLineId,
    Guid? InventoryItemId,
    string Code,
    string Message,
    IReadOnlyList<Guid> ReservingWorkOrderIds);
