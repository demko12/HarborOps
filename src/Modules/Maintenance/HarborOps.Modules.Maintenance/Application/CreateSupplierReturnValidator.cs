using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CreateSupplierReturnValidator
{
    internal const string PurchaseOrderIdField = "purchaseOrderId";

    internal static IReadOnlyDictionary<string, string[]> Validate(CreateSupplierReturnRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.PurchaseOrderId is null || request.PurchaseOrderId == Guid.Empty)
        {
            errors[PurchaseOrderIdField] = ["Purchase order id is required."];
        }

        SupplierReturnLinesValidator.ValidateNotes(request.Notes, errors);
        SupplierReturnLinesValidator.ValidateLines(request.Lines, errors);

        return errors;
    }
}
