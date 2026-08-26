using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class CrewLifecycleValidator
{
    internal const string ActualSignOffDateField = "actualSignOffDate";
    internal const string SignOffPortIdField = "signOffPortId";
    internal const string NotesField = "notes";

    internal static IReadOnlyDictionary<string, string[]> ValidateCompletion(
        DateOnly? actualSignOffDate,
        Guid? signOffPortId,
        DateOnly signOnDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (actualSignOffDate is null)
        {
            errors[ActualSignOffDateField] = ["Actual sign-off date is required."];
        }
        else if (actualSignOffDate < signOnDate)
        {
            errors[ActualSignOffDateField] = ["Actual sign-off date cannot precede the assignment sign-on date."];
        }

        if (signOffPortId == Guid.Empty)
        {
            errors[SignOffPortIdField] = ["Sign-off port ID must not be empty."];
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string[]> ValidateNotes(string? notes, int maximumLength)
    {
        if (notes?.Trim().Length > maximumLength)
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [NotesField] = [$"Notes must not exceed {maximumLength} characters."],
            };
        }

        return new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    internal static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
