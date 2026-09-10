using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RegisterAuditPlanValidator
{
    internal const string VesselIdField = "vesselId";
    internal const string TypeField = "type";
    internal const string PlannedStartDateField = "plannedStartDate";
    internal const string DueDateField = "dueDate";
    internal const string AssignedInspectorUserIdField = "assignedInspectorUserId";
    internal const string ScopeField = "scope";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterAuditPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.VesselId is null || request.VesselId == Guid.Empty)
        {
            errors[VesselIdField] = ["Vessel is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Type) || !SurveyAudit.SupportedTypes.Contains(request.Type.Trim()))
        {
            errors[TypeField] = ["Select a supported audit type."];
        }

        AuditPlanFieldValidation.ValidateDates(
            request.PlannedStartDate, request.DueDate, PlannedStartDateField, DueDateField, errors);

        if (request.AssignedInspectorUserId is { } inspectorId && inspectorId == Guid.Empty)
        {
            errors[AssignedInspectorUserIdField] = ["Assigned inspector must be a valid user ID."];
        }

        AuditPlanFieldValidation.ValidateScope(request.Scope, ScopeField, errors);
        AuditPlanFieldValidation.ValidateNotes(request.Notes, NotesField, errors);

        return errors;
    }
}
