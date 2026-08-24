namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

public static class DocumentsExpiryReportValidator
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 5000;

    public const string AsOfField = "asOf";
    public const string PageNumberField = "pageNumber";
    public const string PageSizeField = "pageSize";
    public const string DateRangeField = "to";

    public static IReadOnlyDictionary<string, string[]> Validate(
        DateOnly? asOf,
        int pageNumber,
        int pageSize,
        DateOnly? from,
        DateOnly? to)
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

        if (from is { } fromDate && to is { } toDate && toDate < fromDate)
        {
            errors[DateRangeField] = ["To date must not be earlier than the from date."];
        }

        return errors;
    }
}
