using HarborOps.Modules.Compliance.Contracts;

namespace HarborOps.Modules.Compliance.Application;

internal static class RescheduleAuditPlanValidator
{
    internal const string PlannedStartDateField = "plannedStartDate";
    internal const string DueDateField = "dueDate";
    internal const string ReasonField = "reason";

    internal static IReadOnlyDictionary<string, string[]> Validate(RescheduleAuditPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AuditPlanFieldValidation.ValidateDates(
            request.PlannedStartDate, request.DueDate, PlannedStartDateField, DueDateField, errors);
        AuditPlanFieldValidation.ValidateReason(request.Reason, ReasonField, errors);

        return errors;
    }
}
