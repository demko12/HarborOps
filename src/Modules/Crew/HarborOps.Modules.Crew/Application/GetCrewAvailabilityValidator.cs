using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class GetCrewAvailabilityValidator
{
    internal const string FromField = "from";
    internal const string ToField = "to";
    internal const string TypeField = "type";
    internal const string StatusField = "status";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;

    internal static readonly string[] Statuses =
    [
        Domain.CrewAvailabilityRequest.RequestedStatus,
        Domain.CrewAvailabilityRequest.ApprovedStatus,
        Domain.CrewAvailabilityRequest.RejectedStatus,
        Domain.CrewAvailabilityRequest.CancelledStatus,
    ];

    internal static IReadOnlyDictionary<string, string[]> Validate(GetCrewAvailabilityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.From is not null && request.To is not null && request.To.Value < request.From.Value)
        {
            errors[ToField] = ["To date cannot precede the from date."];
        }

        if (!string.IsNullOrWhiteSpace(request.Type) && !CrewAvailabilityTypes.All.Contains(request.Type.Trim()))
        {
            errors[TypeField] = ["Unavailability type is not supported."];
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && !Statuses.Contains(request.Status.Trim()))
        {
            errors[StatusField] = ["Status is not supported."];
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
