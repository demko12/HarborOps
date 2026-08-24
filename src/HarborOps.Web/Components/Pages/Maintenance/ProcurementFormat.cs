using System.Globalization;

namespace HarborOps.Web.Components.Pages.Maintenance;

internal static class ProcurementFormat
{
    internal const string DraftStatus = "Draft";
    internal const string OrderedStatus = "Ordered";
    internal const string ReceivedStatus = "Received";
    internal const string CancelledStatus = "Cancelled";
    internal const string MissingValue = "—";

    internal static readonly IReadOnlyList<string> Statuses =
        [DraftStatus, OrderedStatus, ReceivedStatus, CancelledStatus];

    internal static string Money(decimal value)
    {
        return value.ToString("N2", CultureInfo.InvariantCulture);
    }

    internal static string Quantity(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static string OrderDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string DeliveryDate(DateOnly? value)
    {
        return value is { } deliveryDate
            ? deliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string StatusModifier(string status)
    {
        return status.ToLowerInvariant();
    }

    internal static string DeliveryState(bool isOverdue, int? daysUntilDelivery)
    {
        if (isOverdue)
        {
            return "Overdue";
        }

        return daysUntilDelivery switch
        {
            null => "No delivery date",
            < 0 => "Delivery date passed",
            0 => "Due today",
            1 => "Due in 1 day",
            _ => $"Due in {daysUntilDelivery.Value.ToString(CultureInfo.InvariantCulture)} days",
        };
    }

    internal static string DeliveryStateModifier(bool isOverdue, int? daysUntilDelivery)
    {
        if (isOverdue)
        {
            return "overdue";
        }

        return daysUntilDelivery is null ? "none" : "scheduled";
    }

    internal static string TransitionLabel(string transition)
    {
        return transition switch
        {
            OrderedStatus => "Mark as ordered",
            ReceivedStatus => "Receive stock",
            CancelledStatus => "Cancel order",
            _ => transition,
        };
    }

    internal static string TransitionConfirmation(string transition)
    {
        return transition switch
        {
            OrderedStatus => "Mark this purchase order as ordered? Lines can no longer be created for a draft.",
            ReceivedStatus => "Receive this purchase order? Inventory quantities will be increased and the order becomes final.",
            CancelledStatus => "Cancel this purchase order? Inventory quantities stay unchanged.",
            _ => "Apply this status change?",
        };
    }

    internal static string TransitionModifier(string transition)
    {
        return transition switch
        {
            CancelledStatus => "danger",
            ReceivedStatus => "primary",
            _ => "default",
        };
    }

    internal static decimal LineTotal(decimal? quantityOrdered, decimal? unitCost)
    {
        return (quantityOrdered ?? 0m) * (unitCost ?? 0m);
    }

    internal static string FieldLabel(string field)
    {
        if (field.StartsWith("lines[", StringComparison.Ordinal))
        {
            var closingBracket = field.IndexOf(']', StringComparison.Ordinal);
            var separator = field.IndexOf('.', StringComparison.Ordinal);

            if (closingBracket > 6 && separator > closingBracket
                && int.TryParse(
                    field[6..closingBracket],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                return $"Line {(index + 1).ToString(CultureInfo.InvariantCulture)} {LineFieldLabel(field[(separator + 1)..])}";
            }
        }

        return field switch
        {
            "supplierId" => "Supplier",
            "orderDate" => "Order date",
            "expectedDelivery" => "Expected delivery",
            "lines" => "Order lines",
            "name" => "Name",
            "contactPerson" => "Contact person",
            "phoneEmail" => "Phone or e-mail",
            "address" => "Address",
            "paymentTerms" => "Payment terms",
            "status" => "Status",
            "orderDateTo" => "Order date to",
            "expectedDeliveryTo" => "Expected delivery to",
            "search" => "Search",
            "pageNumber" => "Page number",
            "pageSize" => "Page size",
            _ => field,
        };
    }

    private static string LineFieldLabel(string field)
    {
        return field switch
        {
            "inventoryItemId" => "item",
            "quantityOrdered" => "quantity",
            "unitCost" => "unit cost",
            _ => field,
        };
    }
}
