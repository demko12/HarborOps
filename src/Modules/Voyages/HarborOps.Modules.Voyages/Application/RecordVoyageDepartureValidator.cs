using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class RecordVoyageDepartureValidator
{
    internal const string ActualDepartureUtcField = "actualDepartureUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        RecordVoyageDepartureRequest request,
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

    internal static IReadOnlyDictionary<string, string[]> ValidateAgainstCreatedUtc(
        DateTime actualDepartureUtc,
        DateTime createdUtc)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (actualDepartureUtc < createdUtc)
        {
            errors[ActualDepartureUtcField] =
                ["Actual departure must not be earlier than the voyage creation time."];
        }

        return errors;
    }
}
