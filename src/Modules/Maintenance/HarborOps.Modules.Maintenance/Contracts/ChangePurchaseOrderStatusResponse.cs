using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ChangePurchaseOrderStatusResponse(
    Guid PurchaseOrderId,
    string PreviousStatus,
    string Status,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderStockAdjustment> StockAdjustments);
