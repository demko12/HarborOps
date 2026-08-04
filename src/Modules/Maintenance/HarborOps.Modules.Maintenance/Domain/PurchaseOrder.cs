using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrder
{
    private PurchaseOrder()
    {
    }

    private PurchaseOrder(
        Guid id,
        Guid supplierId,
        DateOnly orderDate,
        DateOnly? expectedDelivery,
        PurchaseOrderStatus status)
    {
        Id = id;
        SupplierId = supplierId;
        OrderDate = orderDate;
        ExpectedDelivery = expectedDelivery;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    public DateOnly OrderDate { get; private set; }

    public DateOnly? ExpectedDelivery { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    internal static PurchaseOrder Create(
        Guid supplierId,
        DateOnly orderDate,
        DateOnly? expectedDelivery)
    {
        return new PurchaseOrder(
            Guid.NewGuid(),
            supplierId,
            orderDate,
            expectedDelivery,
            PurchaseOrderStatus.Draft);
    }
}
