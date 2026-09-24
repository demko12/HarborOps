using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CancelSupplierReturnValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(CancelSupplierReturnRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["A cancellation reason is required."];
        }
        else if (request.Reason.Trim().Length > SupplierReturn.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {SupplierReturn.ReasonMaxLength} characters."];
        }

        return errors;
    }
}
