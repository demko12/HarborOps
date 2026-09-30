using System.Globalization;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class SupplierReturnLinesValidator
{
    internal const string NotesField = "notes";
    internal const string LinesField = "lines";

    internal static void ValidateNotes(string? notes, Dictionary<string, string[]> errors)
    {
        if (notes is { Length: > SupplierReturn.NotesMaxLength })
        {
            errors[NotesField] = [$"Notes must not exceed {SupplierReturn.NotesMaxLength} characters."];
        }
    }

    internal static void ValidateLines(
        IReadOnlyList<SupplierReturnLineEntry>? lines,
        Dictionary<string, string[]> errors)
    {
        if (lines is null || lines.Count == 0)
        {
            errors[LinesField] = ["At least one return line is required."];
            return;
        }

        if (lines.Count > SupplierReturn.MaxLines)
        {
            errors[LinesField] = [$"At most {SupplierReturn.MaxLines} return lines are allowed."];
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
                errors[$"{prefix}.purchaseOrderLineId"] = ["Each purchase order line can be returned only once per return."];
            }

            if (line.Quantity is not { } quantity)
            {
                errors[$"{prefix}.quantity"] = ["Return quantity is required."];
            }
            else if (quantity <= 0 || quantity > SupplierReturnLine.MaxQuantity)
            {
                errors[$"{prefix}.quantity"] = ["Return quantity must be greater than zero."];
            }
            else if (!SupplierReturnLine.HasSupportedPrecision(quantity))
            {
                errors[$"{prefix}.quantity"] =
                    [$"Return quantity supports at most {SupplierReturnLine.QuantityScale} decimal places."];
            }

            if (string.IsNullOrWhiteSpace(line.Reason))
            {
                errors[$"{prefix}.reason"] = ["A return reason is required."];
            }
            else if (line.Reason.Trim().Length > SupplierReturn.ReasonMaxLength)
            {
                errors[$"{prefix}.reason"] = [$"Reason must not exceed {SupplierReturn.ReasonMaxLength} characters."];
            }
        }
    }

    // Checks the entries against the purchase order: foreign lines and quantities beyond what is still
    // returnable are rejected. Only posted returns consume the allowance, so drafts never block each other.
    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstOrder(
        IReadOnlyList<SupplierReturnLineEntry> lines,
        IReadOnlyDictionary<Guid, PurchaseOrderLine> purchaseOrderLines,
        IReadOnlyDictionary<Guid, decimal> postedQuantities)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Count; index++)
        {
            var entry = lines[index];
            var prefix = $"{LinesField}[{index}]";

            if (!purchaseOrderLines.TryGetValue(entry.PurchaseOrderLineId!.Value, out var purchaseOrderLine))
            {
                errors[$"{prefix}.purchaseOrderLineId"] = ["Purchase order line is not part of this purchase order."];
                continue;
            }

            // The ceiling is what was actually received (including any legacy balance), not what was ordered.
            var remaining = purchaseOrderLine.ReceivedQuantity
                - postedQuantities.GetValueOrDefault(purchaseOrderLine.Id);

            if (entry.Quantity!.Value > remaining)
            {
                errors[$"{prefix}.quantity"] =
                    [$"Return quantity exceeds the remaining returnable quantity "
                        + $"{remaining.ToString("0.###", CultureInfo.InvariantCulture)}."];
            }
        }

        return errors;
    }
}
