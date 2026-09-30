using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PreviewPurchaseOrderReceiptValidator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(PreviewPurchaseOrderReceiptRequest request, DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(request.ReceivedUtc, required: true, utcNow, errors);
        PurchaseOrderReceiptLinesValidator.ValidateLines(request.Lines, errors);

        return errors;
    }
}
