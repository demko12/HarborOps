using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class GetPreventiveWorkForecastValidator
{
    internal const string FromField = "from";
    internal const string ToField = "to";
    internal const string StateField = "state";
    internal const string PageNumberField = "pageNumber";
    internal const string PageSizeField = "pageSize";

    internal const int DefaultPageNumber = 1;
    internal const int DefaultPageSize = 25;
    internal const int MaxPageSize = 100;
    internal const int MaxWindowDays = 366;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetPreventiveWorkForecastRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.From is null)
        {
            errors[FromField] = ["From date is required."];
        }

        if (request.To is null)
        {
            errors[ToField] = ["To date is required."];
        }

        if (request.From is { } from && request.To is { } to)
        {
            if (to < from)
            {
                errors[ToField] = ["Must be on or after the from date."];
            }
            else if (to.DayNumber - from.DayNumber > MaxWindowDays)
            {
                errors[ToField] = [$"The forecast window must not exceed {MaxWindowDays} days."];
            }
        }

        if (!string.IsNullOrWhiteSpace(request.State) && !TryParseState(request.State, out _))
        {
            errors[StateField] = ["Must be one of Pending, Generated, Completed or Cancelled."];
        }

        if (request.PageNumber.HasValue && request.PageNumber.Value < 1)
        {
            errors[PageNumberField] = ["Page number must be 1 or greater."];
        }

        if (request.PageSize.HasValue && request.PageSize.Value < 1)
        {
            errors[PageSizeField] = ["Page size must be 1 or greater."];
        }
        else if (request.PageSize.HasValue && request.PageSize.Value > MaxPageSize)
        {
            errors[PageSizeField] = [$"Page size must not exceed {MaxPageSize}."];
        }

        return errors;
    }

    internal static bool TryParseState(string? state, out ScheduleOccurrenceState parsedState)
    {
        return Enum.TryParse(state, ignoreCase: true, out parsedState) && Enum.IsDefined(parsedState);
    }
}
