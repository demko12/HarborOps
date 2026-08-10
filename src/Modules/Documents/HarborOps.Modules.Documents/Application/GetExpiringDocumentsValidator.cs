using HarborOps.Modules.Documents.Contracts;

namespace HarborOps.Modules.Documents.Application;

internal static class GetExpiringDocumentsValidator
{
    internal const string FromField = "from";
    internal const string ToField = "to";
    internal const string VesselIdField = "vesselId";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;
    internal const int MaxRangeDays = 366;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetExpiringDocumentsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var fromIsEmpty = request.From == default;
        var toIsEmpty = request.To == default;

        if (fromIsEmpty)
        {
            errors[FromField] = ["From date is required."];
        }

        if (toIsEmpty)
        {
            errors[ToField] = ["To date is required."];
        }

        if (!fromIsEmpty && !toIsEmpty)
        {
            if (request.To < request.From)
            {
                errors[ToField] = ["To date must not be earlier than the from date."];
            }
            else if (request.To.DayNumber - request.From.DayNumber > MaxRangeDays)
            {
                errors[ToField] = [$"Date range must not exceed {MaxRangeDays} days."];
            }
        }

        if (request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required when supplied."];
        }

        if (request.PageNumber is { } pageNumber && pageNumber < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize is { } pageSize)
        {
            if (pageSize < 1)
            {
                errors[PageSizeField] = ["Page size must be 1 or greater."];
            }
            else if (pageSize > MaxPageSize)
            {
                errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
            }
        }

        return errors;
    }
}
