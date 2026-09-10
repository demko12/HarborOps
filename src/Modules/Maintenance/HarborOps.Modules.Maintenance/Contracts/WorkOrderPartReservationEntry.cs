using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record WorkOrderPartReservationEntry
{
    public Guid? InventoryItemId { get; init; }

    public decimal? Quantity { get; init; }
}
