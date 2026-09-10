using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class TriageDefectValidator
{
    internal const string ConfirmedSeverityField = "confirmedSeverity";
    internal const string PriorityField = "priority";

    internal static IReadOnlyDictionary<string, string[]> Validate(TriageDefectRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateConfirmedSeverity(request.ConfirmedSeverity, errors);
        ValidatePriority(request.Priority, errors);

        return errors;
    }

    private static void ValidateConfirmedSeverity(string? confirmedSeverity, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(confirmedSeverity)
            || !Enum.TryParse(confirmedSeverity, ignoreCase: true, out DefectSeverity _))
        {
            errors[ConfirmedSeverityField] = ["Confirmed severity must be one of: Low, Medium, High, Critical."];
        }
    }

    private static void ValidatePriority(string? priority, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(priority)
            || !Enum.TryParse(priority, ignoreCase: true, out WorkOrderPriority _))
        {
            errors[PriorityField] = ["Priority must be one of: Low, Medium, High, Critical."];
        }
    }
}
