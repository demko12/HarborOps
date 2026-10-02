using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetPurchaseOrdersValidator
{
    internal const string StatusField = "status";
    internal const string OrderDateToField = "orderDateTo";
    internal const string ExpectedDeliveryToField = "expectedDeliveryTo";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetPurchaseOrdersRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParseStatus(request.Status, out _))
        {
            errors[StatusField] = ["Status must be Draft, Ordered, PartiallyReceived, Received, ClosedShort, or Cancelled."];
        }

        if (request.OrderDateFrom is { } orderDateFrom
            && request.OrderDateTo is { } orderDateTo
            && orderDateTo < orderDateFrom)
        {
            errors[OrderDateToField] = ["Order date range end must not be earlier than its start."];
        }

        if (request.ExpectedDeliveryFrom is { } deliveryFrom
            && request.ExpectedDeliveryTo is { } deliveryTo
            && deliveryTo < deliveryFrom)
        {
            errors[ExpectedDeliveryToField] = ["Expected delivery range end must not be earlier than its start."];
        }

        if (request.PageNumber.HasValue && request.PageNumber.Value < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize.HasValue && request.PageSize.Value < 1)
        {
            errors[PageSizeField] = ["Page size must be 1 or greater."];
        }
        else if (request.PageSize.HasValue && request.PageSize.Value > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
        }

        return errors;
    }

    internal static bool TryParseStatus(string value, out PurchaseOrderStatus status)
    {
        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }
}
