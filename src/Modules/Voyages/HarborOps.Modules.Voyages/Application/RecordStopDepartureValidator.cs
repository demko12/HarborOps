using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class RecordStopDepartureValidator
{
    internal const string ActualDepartureUtcField = "actualDepartureUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RecordStopDepartureRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ActualDepartureUtc is not { } actualDepartureUtc)
        {
            errors[ActualDepartureUtcField] = ["Actual departure is required."];
            return errors;
        }

        if (actualDepartureUtc.Kind != DateTimeKind.Utc)
        {
            errors[ActualDepartureUtcField] = ["Actual departure must be a UTC timestamp."];
            return errors;
        }

        if (actualDepartureUtc > utcNow)
        {
            errors[ActualDepartureUtcField] = ["Actual departure must not be in the future."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstActualArrival(
        DateTime actualDepartureUtc,
        DateTime actualArrivalUtc)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (actualDepartureUtc <= actualArrivalUtc)
        {
            errors[ActualDepartureUtcField] =
                ["Actual departure must be later than the stop's actual arrival."];
        }

        return errors;
    }
}
