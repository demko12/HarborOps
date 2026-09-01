using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class CloseNonConformityValidator
{
    internal const string ClosureNotesField = "closureNotes";

    internal static IReadOnlyDictionary<string, string[]> Validate(CloseNonConformityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.ClosureNotes))
        {
            errors[ClosureNotesField] = ["Closure notes is required."];
        }
        else if (request.ClosureNotes.Trim().Length > NonConformity.ClosureNotesMaxLength)
        {
            errors[ClosureNotesField] =
                [$"Closure notes must not exceed {NonConformity.ClosureNotesMaxLength} characters."];
        }

        return errors;
    }
}
