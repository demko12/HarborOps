namespace HarborOps.Api.Composition.Reports.ComplianceExposure;

public static class ComplianceExposureReportValidator
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 5000;

    public const string AsOfField = "asOf";
    public const string PageNumberField = "pageNumber";
    public const string PageSizeField = "pageSize";
    public const string CategoryField = "category";

    public static readonly string[] ValidCategories =
        ["Certificate", "Insurance", "Detention", "NonConformity"];

    public static IReadOnlyDictionary<string, string[]> Validate(
        DateOnly? asOf,
        int pageNumber,
        int pageSize,
        string? category)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (asOf is null)
        {
            errors[AsOfField] = ["The asOf date is required."];
        }

        if (pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be greater than zero."];
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        if (!string.IsNullOrWhiteSpace(category)
            && !ValidCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            errors[CategoryField] = [$"Category must be one of: {string.Join(", ", ValidCategories)}."];
        }

        return errors;
    }
}
