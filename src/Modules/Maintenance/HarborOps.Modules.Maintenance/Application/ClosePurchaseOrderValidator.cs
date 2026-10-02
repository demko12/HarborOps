using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class ClosePurchaseOrderValidator
{
    internal const string ClientRequestIdField = "clientRequestId";
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string PreviewTokenField = "previewToken";
    internal const string ReasonField = "reason";

    internal const int PreviewTokenMaxLength = 128;

    internal static IReadOnlyDictionary<string, string[]> Validate(ClosePurchaseOrderRequest request)
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
            errors[PreviewTokenField] = ["A closure preview token is required."];
        }
        else if (request.PreviewToken.Length > PreviewTokenMaxLength)
        {
            errors[PreviewTokenField] = ["Preview token is invalid."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A closure reason is required."];
        }
        else if (request.Reason.Trim().Length < PurchaseOrderClosure.ReasonMinLength)
        {
            errors[ReasonField] =
                [$"Reason must be at least {PurchaseOrderClosure.ReasonMinLength} characters and explain why the "
                    + "remaining goods will not arrive."];
        }
        else if (request.Reason.Trim().Length > PurchaseOrderClosure.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {PurchaseOrderClosure.ReasonMaxLength} characters."];
        }

        return errors;
    }
}
