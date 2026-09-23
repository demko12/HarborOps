using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class EndVesselManningPlanValidator
{
    internal const string NewEndDateField = "newEndDate";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(EndVesselManningPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.NewEndDate is null)
        {
            errors[NewEndDateField] = ["New end date is required."];
        }

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
