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

    internal static IReadOnlyList<PurchaseOrderStatus> AllowedTransitions(PurchaseOrderStatus status)
    {
        return status switch
        {
            PurchaseOrderStatus.Draft =>
                [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Cancelled],
            PurchaseOrderStatus.Ordered =>
                [PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled],
            _ => [],
        };
    }

    internal static bool CanTransition(PurchaseOrderStatus from, PurchaseOrderStatus to)
    {
        return AllowedTransitions(from).Contains(to);
    }

    internal static bool IsOverdue(
        DateOnly? expectedDelivery,
        PurchaseOrderStatus status,
        DateOnly today)
    {
        return expectedDelivery is { } dueDate
            && dueDate < today
            && status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered;
    }

    internal void ChangeStatusTo(PurchaseOrderStatus target)
    {
        if (!CanTransition(Status, target))
        {
            throw new PurchaseOrderTransitionException(Id, Status, target);
        }

        Status = target;
    }
}
