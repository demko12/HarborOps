using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PostPurchaseOrderReceiptValidator
{
    internal const string ClientRequestIdField = "clientRequestId";
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string PreviewTokenField = "previewToken";

    internal const int PreviewTokenMaxLength = 128;

    internal static IReadOnlyDictionary<string, string[]> Validate(PostPurchaseOrderReceiptRequest request, DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ClientRequestId is null || request.ClientRequestId == Guid.Empty)
        {
            errors[ClientRequestIdField] = ["Client request id is required."];
        }

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        if (string.IsNullOrWhiteSpace(request.PreviewToken))
        {
            errors[PreviewTokenField] = ["A posting preview token is required."];
        }
        else if (request.PreviewToken.Length > PreviewTokenMaxLength)
        {
            errors[PreviewTokenField] = ["Preview token is invalid."];
        }

        PurchaseOrderReceiptLinesValidator.ValidateReceivedUtc(request.ReceivedUtc, required: true, utcNow, errors);
        PurchaseOrderReceiptLinesValidator.ValidateDeliveryReference(request.DeliveryReference, errors);
        PurchaseOrderReceiptLinesValidator.ValidateNotes(request.Notes, errors);
        PurchaseOrderReceiptLinesValidator.ValidateLines(request.Lines, errors);

        return errors;
    }
}
