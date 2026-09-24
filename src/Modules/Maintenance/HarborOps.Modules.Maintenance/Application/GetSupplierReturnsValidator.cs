using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetSupplierReturnsValidator
{
    internal const string StatusField = "status";
    internal const string PurchaseOrderIdField = "purchaseOrderId";
    internal const string SupplierIdField = "supplierId";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetSupplierReturnsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(request.Status) && !TryParseStatus(request.Status, out _))
        {
            errors[StatusField] = ["Status must be Draft, Posted, or Cancelled."];
        }

        if (request.PurchaseOrderId == Guid.Empty)
        {
            errors[PurchaseOrderIdField] = ["Purchase order id must not be empty."];
        }

        if (request.SupplierId == Guid.Empty)
        {
            errors[SupplierIdField] = ["Supplier id must not be empty."];
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

    internal static bool TryParseStatus(string value, out SupplierReturnStatus status)
    {
        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }
}
