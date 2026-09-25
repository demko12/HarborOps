using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class UpdateQualificationPolicyValidator
{
    internal const string EnabledField = "enabled";

    internal static IReadOnlyDictionary<string, string[]> Validate(UpdateQualificationPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Enabled is null)
        {
            errors[EnabledField] = ["Enabled is required."];
        }

        QualificationRequirementValidation.Validate(request.Requirements, errors);

        return errors;
    }
}
