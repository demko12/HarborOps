using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

// Every field is optional so the original body-less call stays valid; supplied values are still checked.
internal static class ReceiveRemainingPurchaseOrderValidator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(
        ReceiveRemainingPurchaseOrderRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ClientRequestId == Guid.Empty)
        {
            errors[PostPurchaseOrderReceiptValidator.ClientRequestIdField] = ["Client request id must not be empty."];
        }

        if (request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[PostPurchaseOrderReceiptValidator.ExpectedConcurrencyTokenField] =
                ["Expected concurrency token must not be empty."];
        }

        if (request.PreviewToken is not null
            && (string.IsNullOrWhiteSpace(request.PreviewToken)
                || request.PreviewToken.Length > PostPurchaseOrderReceiptValidator.PreviewTokenMaxLength))
        {
            errors[PostPurchaseOrderReceiptValidator.PreviewTokenField] = ["Preview token is invalid."];
        }

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(request.ReceivedUtc, required: false, utcNow, errors);
        PurchaseOrderReceiptLinesValidator.ValidateDeliveryReference(request.DeliveryReference, errors);
        PurchaseOrderReceiptLinesValidator.ValidateNotes(request.Notes, errors);

        return errors;
    }
}
