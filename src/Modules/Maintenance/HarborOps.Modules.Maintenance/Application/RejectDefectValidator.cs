using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RejectDefectValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(RejectDefectRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[ReasonField] = ["Reason is required."];
        }

        return errors;
    }
}
