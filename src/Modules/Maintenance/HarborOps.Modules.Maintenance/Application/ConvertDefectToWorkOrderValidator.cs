using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class ConvertDefectToWorkOrderValidator
{
    internal const string DueUtcField = "dueUtc";

    internal static IReadOnlyDictionary<string, string[]> Validate(ConvertDefectToWorkOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.DueUtc is null)
        {
            errors[DueUtcField] = ["Due date is required."];
        }

        return errors;
    }
}
