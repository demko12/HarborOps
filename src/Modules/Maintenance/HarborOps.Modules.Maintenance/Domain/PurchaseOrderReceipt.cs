using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

// An immutable delivery record: once posted, a receipt is never edited or reversed.
internal sealed class PurchaseOrderReceipt
{
    internal const int MaxLines = 100;
    internal const int DeliveryReferenceMaxLength = 100;
    internal const int NotesMaxLength = 1000;
    internal const int RequestHashLength = 64;

    private PurchaseOrderReceipt()
    {
    }

    private PurchaseOrderReceipt(
        Guid id,
        Guid purchaseOrderId,
        Guid clientRequestId,
        string requestHash,
        DateTime receivedUtc,
        string? deliveryReference,
        string? notes,
        Guid recordedByUserId,
        DateTime recordedUtc,
        PurchaseOrderStatus orderStatusBefore,
        PurchaseOrderStatus orderStatusAfter)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        ClientRequestId = clientRequestId;
        RequestHash = requestHash;
        ReceivedUtc = receivedUtc;
        DeliveryReference = deliveryReference;
        Notes = notes;
        RecordedByUserId = recordedByUserId;
        RecordedUtc = recordedUtc;
        OrderStatusBefore = orderStatusBefore;
        OrderStatusAfter = orderStatusAfter;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    // Chosen by the client so a retried post returns this receipt instead of receiving twice.
    public Guid ClientRequestId { get; private set; }

    // Fingerprint of the request content; a reused client request id must carry the same content.
    public string RequestHash { get; private set; } = null!;

    public DateTime ReceivedUtc { get; private set; }

    public string? DeliveryReference { get; private set; }

    public string? Notes { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTime RecordedUtc { get; private set; }

    public PurchaseOrderStatus OrderStatusBefore { get; private set; }

    public PurchaseOrderStatus OrderStatusAfter { get; private set; }

    // Called after the purchase order has recorded the receipt, so its status is the resulting one.
    internal static PurchaseOrderReceipt Create(
        PurchaseOrder purchaseOrder,
        PurchaseOrderStatus orderStatusBefore,
        Guid clientRequestId,
        string requestHash,
        DateTime receivedUtc,
        string? deliveryReference,
        string? notes,
        Guid recordedByUserId,
        DateTime utcNow)
    {
        if (clientRequestId == Guid.Empty)
        {
            throw new PurchaseOrderReceiptInvariantException("A client request id is required.");
        }

        if (requestHash.Length != RequestHashLength)
        {
            throw new PurchaseOrderReceiptInvariantException("The receipt request fingerprint is invalid.");
        }

        if (receivedUtc.Kind != DateTimeKind.Utc)
        {
            throw new PurchaseOrderReceiptInvariantException("Received time must be in UTC.");
        }

        if (receivedUtc > utcNow)
        {
            throw new PurchaseOrderReceiptInvariantException("Received time cannot be in the future.");
        }

        var normalizedReference = Normalize(deliveryReference);
        if (normalizedReference is { Length: > DeliveryReferenceMaxLength })
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Delivery reference must not exceed {DeliveryReferenceMaxLength} characters.");
        }

        var normalizedNotes = Normalize(notes);
        if (normalizedNotes is { Length: > NotesMaxLength })
        {
            throw new PurchaseOrderReceiptInvariantException($"Notes must not exceed {NotesMaxLength} characters.");
        }

        return new PurchaseOrderReceipt(
            Guid.NewGuid(),
            purchaseOrder.Id,
            clientRequestId,
            requestHash,
            receivedUtc,
            normalizedReference,
            normalizedNotes,
            recordedByUserId,
            utcNow,
            orderStatusBefore,
            purchaseOrder.Status);
    }

    // Recorded on each stock movement so the inventory history names the delivery that caused it.
    internal string MovementReason()
    {
        return DeliveryReference is null
            ? "Received from purchase order"
            : $"Received from purchase order, delivery {DeliveryReference}";
    }

    internal static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
