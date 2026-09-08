using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record WorkOrderPartReservationListItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal QuantityRequested,
    decimal QuantityReserved,
    decimal QuantityConsumed,
    decimal QuantityRemaining,
    decimal ShortageQuantity,
    bool HasShortage,
    decimal StockOnHand,
    decimal AvailableQuantity);
