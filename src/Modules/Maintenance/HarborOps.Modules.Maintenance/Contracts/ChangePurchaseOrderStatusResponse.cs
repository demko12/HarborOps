using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ChangePurchaseOrderStatusResponse(
    Guid PurchaseOrderId,
    string PreviousStatus,
    string Status,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderStockAdjustment> StockAdjustments,
    // Set only when the change received goods; a replayed client request id reports AlreadyPosted.
    PurchaseOrderReceiptDetails? Receipt,
    bool AlreadyPosted);
