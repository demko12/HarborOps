using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class InventoryItem
{
    internal const int NameMaxLength = 200;
    internal const int PartNumberMaxLength = 100;

    private InventoryItem()
    {
    }

    private InventoryItem(
        Guid id,
        Guid categoryId,
        Guid unitId,
        Guid supplierId,
        string name,
        string partNumber,
        decimal quantity,
        decimal minimumQuantity,
        decimal unitCost,
        Guid stockConcurrencyToken,
        Guid reservationConcurrencyToken)
    {
        Id = id;
        CategoryId = categoryId;
        UnitId = unitId;
        SupplierId = supplierId;
        Name = name;
        PartNumber = partNumber;
        Quantity = quantity;
        MinimumQuantity = minimumQuantity;
        UnitCost = unitCost;
        StockConcurrencyToken = stockConcurrencyToken;
        ReservationConcurrencyToken = reservationConcurrencyToken;
    }

    public Guid Id { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string Name { get; private set; } = null!;

    public string PartNumber { get; private set; } = null!;

    public decimal Quantity { get; private set; }

    public decimal MinimumQuantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public Guid StockConcurrencyToken { get; private set; }

    public Guid ReservationConcurrencyToken { get; private set; }

    internal static InventoryItem Create(
        Guid categoryId,
        Guid unitId,
        Guid supplierId,
        string name,
        string partNumber,
        decimal quantity,
        decimal minimumQuantity,
        decimal unitCost)
    {
        return new InventoryItem(
            Guid.NewGuid(),
            categoryId,
            unitId,
            supplierId,
            name,
            partNumber,
            quantity,
            minimumQuantity,
            unitCost,
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    internal void ReduceStock(decimal quantityUsed)
    {
        if (quantityUsed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityUsed), "Quantity used must be greater than zero.");
        }
        if (quantityUsed > Quantity)
        {
            throw new InvalidOperationException("Insufficient stock to reduce by the specified quantity.");
        }
        Quantity -= quantityUsed;
        StockConcurrencyToken = Guid.NewGuid();
    }

    internal void IncreaseStock(decimal quantityReceived)
    {
        if (quantityReceived <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityReceived),
                "Quantity received must be greater than zero.");
        }

        Quantity += quantityReceived;
        StockConcurrencyToken = Guid.NewGuid();
    }

    internal void ApplyStocktakeCount(decimal countedQuantity)
    {
        if (countedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(countedQuantity),
                "Counted quantity must not be negative.");
        }

        Quantity = countedQuantity;
        StockConcurrencyToken = Guid.NewGuid();
    }

    internal void MarkReservationsChanged()
    {
        ReservationConcurrencyToken = Guid.NewGuid();
    }
}
