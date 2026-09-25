using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class CreateQualificationPolicyValidator
{
    internal const string CrewPositionIdField = "crewPositionId";
    internal const string EnabledField = "enabled";

    internal static IReadOnlyDictionary<string, string[]> Validate(CreateQualificationPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CrewPositionId is null || request.CrewPositionId.Value == Guid.Empty)
        {
            errors[CrewPositionIdField] = ["Crew position ID is required."];
        }

        if (request.Enabled is null)
        {
            errors[EnabledField] = ["Enabled is required."];
        }

        QualificationRequirementValidation.Validate(request.Requirements, errors);

        return errors;
    }
}
