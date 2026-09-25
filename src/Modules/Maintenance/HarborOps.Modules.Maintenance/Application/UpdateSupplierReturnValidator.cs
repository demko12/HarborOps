using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class UpdateSupplierReturnValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";

    internal static IReadOnlyDictionary<string, string[]> Validate(UpdateSupplierReturnRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        SupplierReturnLinesValidator.ValidateNotes(request.Notes, errors);
        SupplierReturnLinesValidator.ValidateLines(request.Lines, errors);

        return errors;
    }
}
