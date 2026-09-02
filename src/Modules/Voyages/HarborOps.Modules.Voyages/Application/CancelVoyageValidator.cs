using HarborOps.Modules.Voyages.Contracts;

namespace HarborOps.Modules.Voyages.Application;

internal static class CancelVoyageValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(CancelVoyageRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        VoyageScheduleReasonValidator.Validate(request.Reason, ReasonField, errors);

        return errors;
    }
}
