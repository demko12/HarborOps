using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class RecordVoyageArrivalValidator
{
    internal const string ActualArrivalUtcField = "actualArrivalUtc";
    internal static IReadOnlyDictionary<string, string[]> Validate(
        RecordVoyageArrivalRequest request,
        DateTime utcNow)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ActualArrivalUtc is not { } actualArrivalUtc)
        {
            errors[ActualArrivalUtcField] = ["Actual arrival is required."];
            return errors;
        }

        if (actualArrivalUtc.Kind != DateTimeKind.Utc)
        {
            errors[ActualArrivalUtcField] = ["Actual arrival must be a UTC timestamp."];
            return errors;
        }

        if (actualArrivalUtc > utcNow)
        {
            errors[ActualArrivalUtcField] = ["Actual arrival must not be in the future."];
        }

        return errors;
    }
    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstActualDeparture(
        DateTime actualArrivalUtc,
        DateTime actualDeparture)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (actualArrivalUtc <= actualDeparture)
        {
            errors[ActualArrivalUtcField] =
                ["Actual arrival must be later than the actual departure."];
        }

        return errors;
    }
}
