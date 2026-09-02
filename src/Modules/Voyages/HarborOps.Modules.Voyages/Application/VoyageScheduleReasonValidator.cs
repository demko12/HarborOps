using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class VoyageScheduleReasonValidator
{
    internal static void Validate(
        string? reason,
        string field,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[field] = ["Reason is required."];
            return;
        }

        if (reason.Trim().Length > VoyageScheduleChange.ReasonMaxLength)
        {
            errors[field] =
                [$"Reason must not exceed {VoyageScheduleChange.ReasonMaxLength} characters."];
        }
    }
}
