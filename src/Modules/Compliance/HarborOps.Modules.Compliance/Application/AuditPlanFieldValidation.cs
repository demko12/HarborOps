using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class AuditPlanFieldValidation
{
    internal static void ValidateDates(
        DateOnly? plannedStartDate,
        DateOnly? dueDate,
        string plannedStartDateField,
        string dueDateField,
        IDictionary<string, string[]> errors)
    {
        if (plannedStartDate is null)
        {
            errors[plannedStartDateField] = ["Planned start date is required."];
        }

        if (dueDate is null)
        {
            errors[dueDateField] = ["Due date is required."];
        }

        if (plannedStartDate is { } start && dueDate is { } due && due < start)
        {
            errors[dueDateField] = ["Due date must be on or after the planned start date."];
        }
    }

    internal static void ValidateScope(string? scope, string field, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            errors[field] = ["Scope is required."];
            return;
        }

        if (scope.Trim().Length > AuditPlan.ScopeMaxLength)
        {
            errors[field] = [$"Scope must not exceed {AuditPlan.ScopeMaxLength} characters."];
        }
    }

    internal static void ValidateNotes(string? notes, string field, IDictionary<string, string[]> errors)
    {
        if (notes is not null && notes.Trim().Length > AuditPlan.NotesMaxLength)
        {
            errors[field] = [$"Notes must not exceed {AuditPlan.NotesMaxLength} characters."];
        }
    }

    internal static void ValidateReason(string? reason, string field, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors[field] = ["Reason is required."];
            return;
        }

        if (reason.Trim().Length > AuditPlanHistory.ReasonMaxLength)
        {
            errors[field] = [$"Reason must not exceed {AuditPlanHistory.ReasonMaxLength} characters."];
        }
    }

    internal static void ValidateOptionalReason(string? reason, string field, IDictionary<string, string[]> errors)
    {
        if (reason is not null && reason.Trim().Length > AuditPlanHistory.ReasonMaxLength)
        {
            errors[field] = [$"Reason must not exceed {AuditPlanHistory.ReasonMaxLength} characters."];
        }
    }
}
