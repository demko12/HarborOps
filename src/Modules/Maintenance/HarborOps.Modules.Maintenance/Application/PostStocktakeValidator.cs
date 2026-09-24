using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PostStocktakeValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string PreviewTokenField = "previewToken";

    internal const int PreviewTokenMaxLength = 128;

    internal static IReadOnlyDictionary<string, string[]> Validate(PostStocktakeRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

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

        return errors;
    }
}
