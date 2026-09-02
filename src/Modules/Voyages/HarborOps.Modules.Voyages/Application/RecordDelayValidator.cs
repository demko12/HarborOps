using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.Modules.Voyages.Application;

internal static class RecordDelayValidator
{
    internal const string ReasonField = "reason";
    internal const string DurationField = "duration";

    internal static IReadOnlyDictionary<string, string[]> Validate(RecordDelayRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Delay reason is required."];
        }
        else if (request.Reason.Trim().Length > Delay.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Delay reason must not exceed {Delay.ReasonMaxLength} characters."];
        }

        if (request.Duration is null)
        {
            errors[DurationField] = ["Delay duration is required."];
        }
        else if (request.Duration.Value <= 0)
        {
            errors[DurationField] = ["Delay duration must be greater than zero."];
        }

        return errors;
    }
}
