using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.Modules.Compliance.Application;

internal static class CancelAuditPlanValidator
{
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(CancelAuditPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AuditPlanFieldValidation.ValidateReason(request.Reason, ReasonField, errors);

        return errors;
    }
}
