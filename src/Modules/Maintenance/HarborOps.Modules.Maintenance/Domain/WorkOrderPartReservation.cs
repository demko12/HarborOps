using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderPartReservation
{
    private WorkOrderPartReservation()
    {
    }

    private WorkOrderPartReservation(
        Guid id,
        Guid workOrderId,
        Guid inventoryItemId,
        decimal quantityRequested,
        decimal quantityReserved,
        decimal quantityConsumed,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        Id = id;
        WorkOrderId = workOrderId;
        InventoryItemId = inventoryItemId;
        QuantityRequested = quantityRequested;
        QuantityReserved = quantityReserved;
        QuantityConsumed = quantityConsumed;
        CreatedUtc = createdUtc;
        UpdatedUtc = updatedUtc;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public decimal QuantityRequested { get; private set; }

    public decimal QuantityReserved { get; private set; }

    public decimal QuantityConsumed { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    internal decimal QuantityRemaining => QuantityReserved - QuantityConsumed;

    internal decimal ShortageQuantity => QuantityRequested - QuantityReserved;

    internal bool HasShortage => QuantityRequested > QuantityReserved;

    internal static WorkOrderPartReservation Create(
        Guid workOrderId,
        Guid inventoryItemId,
        decimal quantityRequested,
        decimal availableQuantity,
        DateTime utcNow)
    {
        EnsureRequestedQuantityIsPositive(quantityRequested);

        var reserved = Math.Min(quantityRequested, Math.Max(availableQuantity, 0m));

        return new WorkOrderPartReservation(
            Guid.NewGuid(),
            workOrderId,
            inventoryItemId,
            quantityRequested,
            reserved,
            0m,
            utcNow,
            utcNow);
    }

    internal void ChangeRequestedQuantity(
        decimal quantityRequested,
        decimal availableQuantity,
        DateTime utcNow)
    {
        EnsureRequestedQuantityIsPositive(quantityRequested);

        if (quantityRequested < QuantityConsumed)
        {
            throw new WorkOrderPartReservationInvariantException(
                $"Reservation for item '{InventoryItemId}' cannot request less than the consumed quantity {QuantityConsumed}.");
        }

        var additionalHold = Math.Min(
            quantityRequested - QuantityConsumed,
            Math.Max(availableQuantity, 0m));

        QuantityRequested = quantityRequested;
        QuantityReserved = QuantityConsumed + additionalHold;
        UpdatedUtc = utcNow;
    }

    internal void Consume(decimal quantity, DateTime utcNow)
    {
        if (quantity <= 0)
        {
            throw new WorkOrderPartReservationInvariantException(
                "Consumed quantity must be greater than zero.");
        }

        if (quantity > QuantityRemaining)
        {
            throw new WorkOrderPartReservationInvariantException(
                $"Quantity {quantity} exceeds the remaining reserved quantity {QuantityRemaining} for item '{InventoryItemId}'.");
        }

        QuantityConsumed += quantity;
        UpdatedUtc = utcNow;
    }

    internal void ReleaseRemaining(DateTime utcNow)
    {
        QuantityRequested = QuantityConsumed;
        QuantityReserved = QuantityConsumed;
        UpdatedUtc = utcNow;
    }

    private static void EnsureRequestedQuantityIsPositive(decimal quantityRequested)
    {
        if (quantityRequested <= 0)
        {
            throw new WorkOrderPartReservationInvariantException(
                "Reserved quantity must be greater than zero.");
        }
    }
}
