using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.Modules.Compliance.Application;

internal static class AssignAuditPlanInspectorValidator
{
    internal const string InspectorUserIdField = "inspectorUserId";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(AssignAuditPlanInspectorRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.InspectorUserId is null || request.InspectorUserId == Guid.Empty)
        {
            errors[InspectorUserIdField] = ["Inspector is required."];
        }

        AuditPlanFieldValidation.ValidateOptionalReason(request.Reason, ReasonField, errors);

        return errors;
    }
}
