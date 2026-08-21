using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderStockAdjustment(
    Guid InventoryItemId,
    decimal QuantityReceived,
    decimal QuantityOnHand);
