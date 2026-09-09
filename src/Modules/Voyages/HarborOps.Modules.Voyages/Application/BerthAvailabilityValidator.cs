using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class BerthAvailabilityValidator
{
    internal const int MaxSearchDays = 31;

    internal static IReadOnlyDictionary<string, string[]> Validate(BerthAvailabilityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.FromUtc is null) errors[nameof(request.FromUtc)] = ["From UTC is required."];
        if (request.ToUtc is null) errors[nameof(request.ToUtc)] = ["To UTC is required."];
        if (request.FromUtc is { } from && request.ToUtc is { } to)
        {
            if (to <= from) errors[nameof(request.ToUtc)] = ["To UTC must be after From UTC."];
            else if (to - from > TimeSpan.FromDays(MaxSearchDays)) errors[nameof(request.ToUtc)] = [$"The requested window must not exceed {MaxSearchDays} days."];
        }
        if (request.Draft is <= 0) errors[nameof(request.Draft)] = ["Draft must be greater than zero when supplied."];
        if (request.Length is <= 0) errors[nameof(request.Length)] = ["Length must be greater than zero when supplied."];
        return errors;
    }
}
