using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class CompleteDryDockValidator
{
    internal const string EndDateField = "endDate";
    internal const string ActualCostField = "actualCost";

    internal static IReadOnlyDictionary<string, string[]> Validate(CompleteDryDockRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.EndDate is null)
        {
            errors[EndDateField] = ["Required."];
        }

        if (request.ActualCost is null)
        {
            errors[ActualCostField] = ["Required."];
        }
        else if (request.ActualCost.Value < 0m)
        {
            errors[ActualCostField] = ["Must not be negative."];
        }

        return errors;
    }
}
