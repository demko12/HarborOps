using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptDetails(
    Guid ReceiptId,
    Guid PurchaseOrderId,
    Guid ClientRequestId,
    DateTime ReceivedUtc,
    string? DeliveryReference,
    string? Notes,
    Guid RecordedByUserId,
    DateTime RecordedUtc,
    string OrderStatusBefore,
    string OrderStatusAfter,
    decimal TotalQuantity,
    IReadOnlyList<PurchaseOrderReceiptLineItem> Lines,
    IReadOnlyList<PurchaseOrderReceiptMovementItem> Movements);
