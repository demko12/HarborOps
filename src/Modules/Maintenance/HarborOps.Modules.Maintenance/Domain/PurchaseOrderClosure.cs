using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

// Immutable evidence that the undelivered balance of a purchase order was closed short; never edited or reversed.
internal sealed class PurchaseOrderClosure
{
    internal const int ReasonMinLength = 10;
    internal const int ReasonMaxLength = 500;
    internal const int RequestHashLength = 64;

    private PurchaseOrderClosure()
    {
    }

    private PurchaseOrderClosure(
        Guid id,
        Guid purchaseOrderId,
        Guid clientRequestId,
        string requestHash,
        string reason,
        Guid closedByUserId,
        DateTime closedUtc,
        PurchaseOrderStatus orderStatusBefore,
        PurchaseOrderStatus orderStatusAfter)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        ClientRequestId = clientRequestId;
        RequestHash = requestHash;
        Reason = reason;
        ClosedByUserId = closedByUserId;
        ClosedUtc = closedUtc;
        OrderStatusBefore = orderStatusBefore;
        OrderStatusAfter = orderStatusAfter;
    }

    public Guid Id { get; private set; }

    // An order is closed at most once.
    public Guid PurchaseOrderId { get; private set; }

    // Chosen by the client so a retried close returns this closure instead of failing.
    public Guid ClientRequestId { get; private set; }

    // Fingerprint of the request content; a reused client request id must carry the same content.
    public string RequestHash { get; private set; } = null!;

    public string Reason { get; private set; } = null!;

    public Guid ClosedByUserId { get; private set; }

    public DateTime ClosedUtc { get; private set; }

    public PurchaseOrderStatus OrderStatusBefore { get; private set; }

    public PurchaseOrderStatus OrderStatusAfter { get; private set; }

    // Called after the purchase order has recorded the closure, so its status is the resulting one.
    internal static PurchaseOrderClosure Create(
        PurchaseOrder purchaseOrder,
        PurchaseOrderStatus orderStatusBefore,
        Guid clientRequestId,
        string requestHash,
        string reason,
        Guid closedByUserId,
        DateTime utcNow)
    {
        if (clientRequestId == Guid.Empty)
        {
            throw new PurchaseOrderClosureInvariantException("A client request id is required.");
        }

        if (requestHash.Length != RequestHashLength)
        {
            throw new PurchaseOrderClosureInvariantException("The closure request fingerprint is invalid.");
        }

        if (closedByUserId == Guid.Empty)
        {
            throw new PurchaseOrderClosureInvariantException("The closing user is required.");
        }

        if (purchaseOrder.Status != PurchaseOrderStatus.ClosedShort)
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order '{purchaseOrder.Id}' must be closed short before its closure is recorded.");
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length is < ReasonMinLength or > ReasonMaxLength)
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Closure reason must be between {ReasonMinLength} and {ReasonMaxLength} characters.");
        }

        return new PurchaseOrderClosure(
            Guid.NewGuid(),
            purchaseOrder.Id,
            clientRequestId,
            requestHash,
            normalizedReason,
            closedByUserId,
            utcNow,
            orderStatusBefore,
            purchaseOrder.Status);
    }
}
