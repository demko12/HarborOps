using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CancelStocktakeValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(CancelStocktakeRequest request)
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
        else if (request.Reason.Trim().Length > Stocktake.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {Stocktake.ReasonMaxLength} characters."];
        }

        return errors;
    }
}
