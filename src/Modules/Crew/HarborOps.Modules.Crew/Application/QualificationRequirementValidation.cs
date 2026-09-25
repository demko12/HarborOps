using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class QualificationRequirementValidation
{
    internal const string RequirementsField = "requirements";

    internal static void Validate(
        IReadOnlyList<QualificationRequirementInput>? requirements,
        IDictionary<string, string[]> errors)
    {
        if (requirements is null)
        {
            errors[RequirementsField] = ["Required."];
            return;
        }

        if (requirements.Any(requirement => string.IsNullOrWhiteSpace(requirement.RequiredType)))
        {
            errors[RequirementsField] = ["Each requirement must specify a non-empty required type."];
            return;
        }

        if (requirements.Any(requirement => requirement.RequiredType!.Trim().Length > CrewQualificationRequirement.RequiredTypeMaxLength))
        {
            errors[RequirementsField] = [$"Each required type must not exceed {CrewQualificationRequirement.RequiredTypeMaxLength} characters."];
            return;
        }

        var distinctCount = requirements
            .Select(requirement => requirement.RequiredType!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctCount != requirements.Count)
        {
            errors[RequirementsField] = ["Must not contain duplicate required types."];
        }
    }
}
