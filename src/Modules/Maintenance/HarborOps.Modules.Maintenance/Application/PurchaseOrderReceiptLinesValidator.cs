using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PurchaseOrderReceiptLinesValidator
{
    internal const string ReceivedUtcField = "receivedUtc";
    internal const string DeliveryReferenceField = "deliveryReference";
    internal const string NotesField = "notes";
    internal const string LinesField = "lines";

    internal static void ValidateReceivedUtc(
        DateTime? receivedUtc,
        bool required,
        DateTime utcNow,
        Dictionary<string, string[]> errors)
    {
        if (receivedUtc is not { } value)
        {
            if (required)
            {
                errors[ReceivedUtcField] = ["Received UTC is required."];
            }

            return;
        }

        if (value.Kind != DateTimeKind.Utc)
        {
            errors[ReceivedUtcField] = ["Received UTC must be in UTC format."];
        }
        else if (value > utcNow)
        {
            errors[ReceivedUtcField] = ["Received UTC cannot be in the future."];
        }
    }

    internal static void ValidateDeliveryReference(string? deliveryReference, Dictionary<string, string[]> errors)
    {
        if (PurchaseOrderReceipt.Normalize(deliveryReference) is { Length: > PurchaseOrderReceipt.DeliveryReferenceMaxLength })
        {
            errors[DeliveryReferenceField] =
                [$"Delivery reference must not exceed {PurchaseOrderReceipt.DeliveryReferenceMaxLength} characters."];
        }
    }

    internal static void ValidateNotes(string? notes, Dictionary<string, string[]> errors)
    {
        if (PurchaseOrderReceipt.Normalize(notes) is { Length: > PurchaseOrderReceipt.NotesMaxLength })
        {
            errors[NotesField] = [$"Notes must not exceed {PurchaseOrderReceipt.NotesMaxLength} characters."];
        }
    }

    internal static void ValidateLines(
        IReadOnlyList<PurchaseOrderReceiptLineEntry>? lines,
        Dictionary<string, string[]> errors)
    {
        if (lines is null || lines.Count == 0)
        {
            errors[LinesField] = ["At least one receipt line is required."];
            return;
        }

        if (lines.Count > PurchaseOrderReceipt.MaxLines)
        {
            errors[LinesField] = [$"At most {PurchaseOrderReceipt.MaxLines} receipt lines are allowed."];
            return;
        }

        var seenLineIds = new HashSet<Guid>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var prefix = $"{LinesField}[{index}]";

            if (line is null)
            {
                errors[prefix] = ["Line is required."];
                continue;
            }

            if (line.PurchaseOrderLineId is not { } lineId || lineId == Guid.Empty)
            {
                errors[$"{prefix}.purchaseOrderLineId"] = ["Purchase order line id is required."];
            }
            else if (!seenLineIds.Add(lineId))
            {
                errors[$"{prefix}.purchaseOrderLineId"] = ["Each purchase order line can be received only once per receipt."];
            }

            if (line.Quantity is not { } quantity)
            {
                errors[$"{prefix}.quantity"] = ["Received quantity is required."];
            }
            else if (quantity <= 0 || quantity > PurchaseOrderReceiptLine.MaxQuantity)
            {
                errors[$"{prefix}.quantity"] = ["Received quantity must be greater than zero."];
            }
            else if (!PurchaseOrderReceiptLine.HasSupportedPrecision(quantity))
            {
                errors[$"{prefix}.quantity"] =
                    [$"Received quantity supports at most {PurchaseOrderReceiptLine.QuantityScale} decimal places."];
            }
        }
    }

    // Lines must belong to the purchase order being received; anything else is a malformed request.
    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstOrder(
        IReadOnlyList<PurchaseOrderReceiptLineEntry> lines,
        IReadOnlyDictionary<Guid, PurchaseOrderLine> purchaseOrderLines)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Count; index++)
        {
            if (!purchaseOrderLines.ContainsKey(lines[index].PurchaseOrderLineId!.Value))
            {
                errors[$"{LinesField}[{index}].purchaseOrderLineId"] =
                    ["Purchase order line is not part of this purchase order."];
            }
        }

        return errors;
    }
}
