using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CreatePurchaseOrderValidator
{
    internal const string SupplierIdField = "supplierId";
    internal const string OrderDateField = "orderDate";
    internal const string ExpectedDeliveryField = "expectedDelivery";
    internal const string LinesField = "lines";

    internal const int MaxLines = 200;

    internal static IReadOnlyDictionary<string, string[]> Validate(CreatePurchaseOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.SupplierId is null || request.SupplierId.Value == Guid.Empty)
        {
            errors[SupplierIdField] = ["Select a supplier."];
        }

        if (request.OrderDate is null)
        {
            errors[OrderDateField] = ["Required."];
        }

        if (request.OrderDate is { } orderDate
            && request.ExpectedDelivery is { } expectedDelivery
            && expectedDelivery < orderDate)
        {
            errors[ExpectedDeliveryField] = ["Expected delivery must not be earlier than the order date."];
        }

        if (request.Lines is not { Count: > 0 })
        {
            errors[LinesField] = ["At least one order line is required."];

            return errors;
        }

        if (request.Lines.Count > MaxLines)
        {
            errors[LinesField] = [$"An order must not exceed {MaxLines} lines."];
        }

        var seenItemIds = new HashSet<Guid>();

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];

            if (line.InventoryItemId is null || line.InventoryItemId.Value == Guid.Empty)
            {
                errors[$"{LinesField}[{index}].inventoryItemId"] = ["Select an inventory item."];
            }
            else if (!seenItemIds.Add(line.InventoryItemId.Value))
            {
                errors[$"{LinesField}[{index}].inventoryItemId"] =
                    ["The same inventory item must not appear on more than one line."];
            }

            if (line.QuantityOrdered is null || line.QuantityOrdered.Value <= 0)
            {
                errors[$"{LinesField}[{index}].quantityOrdered"] =
                    ["Ordered quantity must be greater than zero."];
            }

            if (line.UnitCost is null || line.UnitCost.Value < 0)
            {
                errors[$"{LinesField}[{index}].unitCost"] = ["Unit cost must not be negative."];
            }
        }

        return errors;
    }
}
