namespace HarborOps.Modules.Crew.Application;

using HarborOps.Modules.Crew.Domain;

internal static class CrewRotationValidator
{
    internal const string RelievingCrewMemberIdField = "relievingCrewMemberId";
    internal const string RotationPatternField = "rotationPattern";
    internal const string PlannedReliefDateField = "plannedReliefDate";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        Guid? relievingCrewMemberId,
        string? rotationPattern,
        DateOnly? plannedReliefDate,
        DateOnly assignmentSignOnDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (relievingCrewMemberId is not null && relievingCrewMemberId.Value == Guid.Empty)
        {
            errors[RelievingCrewMemberIdField] = ["Relieving crew member ID must not be empty."];
        }

        ValidateRotationPattern(rotationPattern, errors);
        ValidatePlannedReliefDate(plannedReliefDate, assignmentSignOnDate, errors);

        return errors;
    }

    private static void ValidateRotationPattern(string? rotationPattern, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(rotationPattern))
        {
            errors[RotationPatternField] = ["Rotation pattern is required."];
            return;
        }

        if (rotationPattern.Trim().Length > CrewRotation.RotationPatternMaxLength)
        {
            errors[RotationPatternField] = [$"Rotation pattern must not exceed {CrewRotation.RotationPatternMaxLength} characters."];
        }
    }

    private static void ValidatePlannedReliefDate(
        DateOnly? plannedReliefDate,
        DateOnly assignmentSignOnDate,
        IDictionary<string, string[]> errors)
    {
        if (plannedReliefDate is null)
        {
            errors[PlannedReliefDateField] = ["Planned relief date is required."];
            return;
        }

        if (plannedReliefDate.Value < assignmentSignOnDate)
        {
            errors[PlannedReliefDateField] = ["Planned relief date cannot precede the assignment sign-on date."];
        }
    }
}
