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
        PurchaseOrderStatus status,
        Guid concurrencyToken)
    {
        Id = id;
        SupplierId = supplierId;
        OrderDate = orderDate;
        ExpectedDelivery = expectedDelivery;
        Status = status;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    public DateOnly OrderDate { get; private set; }

    public DateOnly? ExpectedDelivery { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    // Replaced on every status change, receipt, and closure, so competing order mutations conflict.
    public Guid ConcurrencyToken { get; private set; }

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
            PurchaseOrderStatus.Draft,
            Guid.NewGuid());
    }

    // "Received" stands for receiving the remaining outstanding quantity; PartiallyReceived is only ever
    // reached through a receipt, never requested as a transition.
    internal static IReadOnlyList<PurchaseOrderStatus> AllowedTransitions(PurchaseOrderStatus status)
    {
        return status switch
        {
            PurchaseOrderStatus.Draft =>
                [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Cancelled],
            PurchaseOrderStatus.Ordered =>
                [PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled],
            PurchaseOrderStatus.PartiallyReceived =>
                [PurchaseOrderStatus.Received],
            _ => [],
        };
    }

    internal static bool CanTransition(PurchaseOrderStatus from, PurchaseOrderStatus to)
    {
        return AllowedTransitions(from).Contains(to);
    }

    internal static bool CanReceive(PurchaseOrderStatus status)
    {
        return status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived;
    }

    // Goods can only be returned once something has actually been received. Closing the undelivered
    // balance short keeps the received goods returnable.
    internal static bool CanReturn(PurchaseOrderStatus status)
    {
        return status is PurchaseOrderStatus.PartiallyReceived
            or PurchaseOrderStatus.Received
            or PurchaseOrderStatus.ClosedShort;
    }

    // Only a partially received order closes short; an order with nothing received is cancelled instead.
    internal static bool CanClose(PurchaseOrderStatus status)
    {
        return status is PurchaseOrderStatus.PartiallyReceived;
    }

    internal static bool IsOverdue(
        DateOnly? expectedDelivery,
        PurchaseOrderStatus status,
        DateOnly today)
    {
        return expectedDelivery is { } dueDate
            && dueDate < today
            && status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived;
    }

    internal void ChangeStatusTo(PurchaseOrderStatus target)
    {
        // Receiving moves stock, so it must go through RecordReceipt with the posted quantities; closing short
        // must go through RecordClosure with the closed quantities.
        if (target is PurchaseOrderStatus.Received
                or PurchaseOrderStatus.PartiallyReceived
                or PurchaseOrderStatus.ClosedShort
            || !CanTransition(Status, target))
        {
            throw new PurchaseOrderTransitionException(Id, Status, target);
        }

        Status = target;
        ConcurrencyToken = Guid.NewGuid();
    }

    // Derives the status from the cumulative line quantities after a receipt has been applied to them.
    internal void RecordReceipt(IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!CanReceive(Status))
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Purchase order '{Id}' is {Status}; only ordered or partially received orders can be received.");
        }

        if (lines.Count == 0 || lines.Any(line => line.PurchaseOrderId != Id))
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Purchase order '{Id}' can only be received against its own lines.");
        }

        Status = lines.All(line => line.IsFulfilled)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        ConcurrencyToken = Guid.NewGuid();
    }

    // Called after every line has closed its outstanding quantity; ClosedShort is terminal.
    internal void RecordClosure(IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (!CanClose(Status))
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order '{Id}' is {Status}; only partially received orders can be closed short.");
        }

        if (lines.Count == 0 || lines.Any(line => line.PurchaseOrderId != Id))
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order '{Id}' can only be closed against its own lines.");
        }

        if (lines.Any(line => line.OutstandingQuantity != 0))
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order '{Id}' still has open outstanding quantity after closing.");
        }

        Status = PurchaseOrderStatus.ClosedShort;
        ConcurrencyToken = Guid.NewGuid();
    }
}
