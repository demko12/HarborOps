using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class ChangeCrewMemberEmploymentStatusValidator
{
    internal const string StatusField = "status";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> Validate(ChangeCrewMemberEmploymentStatusRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateStatus(request.Status, errors);
        ValidateNotes(request.Notes, errors);

        return errors;
    }

    private static void ValidateStatus(string? status, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            errors[StatusField] = ["Status is required."];
            return;
        }

        if (!CrewEmploymentStatuses.All.Contains(status.Trim()))
        {
            errors[StatusField] = ["Status is not supported."];
        }
    }

    private static void ValidateNotes(string? notes, IDictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > CrewEmploymentStatusHistory.NotesMaxLength)
        {
            errors[NotesField] = [$"Notes must not exceed {CrewEmploymentStatusHistory.NotesMaxLength} characters."];
        }
    }
}
