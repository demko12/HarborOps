using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class CompleteAuditPlanValidator
{
    internal const string ResultField = "result";
    internal const string FindingsField = "findings";
    internal const string ActualAuditDateField = "actualAuditDate";

    internal static IReadOnlyDictionary<string, string[]> Validate(CompleteAuditPlanRequest request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Result) || !SurveyAudit.SupportedResults.Contains(request.Result.Trim()))
        {
            errors[ResultField] = ["Select a supported result."];
        }

        if (string.IsNullOrWhiteSpace(request.Findings))
        {
            errors[FindingsField] = ["Findings are required."];
        }
        else if (request.Findings.Trim().Length > SurveyAudit.FindingsMaxLength)
        {
            errors[FindingsField] = [$"Findings must not exceed {SurveyAudit.FindingsMaxLength} characters."];
        }

        if (request.ActualAuditDate is null)
        {
            errors[ActualAuditDateField] = ["Actual audit date is required."];
        }
        else if (request.ActualAuditDate > today)
        {
            errors[ActualAuditDateField] = ["Actual audit date must not be in the future."];
        }

        return errors;
    }
}
