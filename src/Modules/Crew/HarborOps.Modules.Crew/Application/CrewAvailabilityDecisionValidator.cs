using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class CrewAvailabilityDecisionValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> ValidateReason(string? reason)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }
        else if (reason.Trim().Length > CrewAvailabilityRequest.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {CrewAvailabilityRequest.ReasonMaxLength} characters."];
        }

        return errors;
    }
}
