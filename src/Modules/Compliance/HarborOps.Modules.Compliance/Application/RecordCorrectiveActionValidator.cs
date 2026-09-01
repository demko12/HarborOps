using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;

namespace HarborOps.Modules.Compliance.Application;

internal static class RecordCorrectiveActionValidator
{
    internal const string ActionTextField = "actionText";

    internal static IReadOnlyDictionary<string, string[]> Validate(RecordCorrectiveActionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.ActionText))
        {
            errors[ActionTextField] = ["Action text is required."];
        }
        else if (request.ActionText.Trim().Length > CorrectiveAction.ActionTextMaxLength)
        {
            errors[ActionTextField] =
                [$"Action text must not exceed {CorrectiveAction.ActionTextMaxLength} characters."];
        }

        return errors;
    }
}
