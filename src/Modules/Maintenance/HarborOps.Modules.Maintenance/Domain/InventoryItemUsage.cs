using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class InventoryItemUsage
{
    private InventoryItemUsage()
    {
    }

    private InventoryItemUsage(
        Guid workOrderId,
        Guid partId,
        decimal quantityUsed)
    {
        WorkOrderId = workOrderId;
        PartId = partId;
        QuantityUsed = quantityUsed;
    }

    public Guid WorkOrderId { get; private set; }

    public Guid PartId { get; private set; }

    public decimal QuantityUsed { get; private set; }

    internal static InventoryItemUsage Create(
        Guid workOrderId,
        Guid partId,
        decimal quantityUsed)
    {
        return new InventoryItemUsage(
            workOrderId,
            partId,
            quantityUsed);
    }
}
