using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class ReplaceVesselManningPlanLinesValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(ReplaceVesselManningPlanLinesRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ManningPlanLineValidation.Validate(request.Lines, errors);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }
        else if (request.Reason.Trim().Length > VesselManningPlanHistory.ReasonMaxLength)
        {
            errors[ReasonField] = [$"Reason must not exceed {VesselManningPlanHistory.ReasonMaxLength} characters."];
        }

        return errors;
    }
}
