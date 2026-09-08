using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class CrewPositionFieldValidation
{
    internal const string RankNameField = "rankName";
    internal const string DepartmentField = "department";
    internal const string SeniorityLevelField = "seniorityLevel";

    internal static void ValidateRankName(string? rankName, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(rankName))
        {
            errors[RankNameField] = ["Rank name is required."];
            return;
        }

        if (rankName.Trim().Length > CrewPosition.RankNameMaxLength)
        {
            errors[RankNameField] = [$"Rank name must not exceed {CrewPosition.RankNameMaxLength} characters."];
        }
    }

    internal static void ValidateDepartment(string? department, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(department))
        {
            errors[DepartmentField] = ["Department is required."];
            return;
        }

        if (!CrewPositionDepartments.All.Contains(department.Trim()))
        {
            errors[DepartmentField] = ["Department is not supported."];
        }
    }

    internal static void ValidateSeniorityLevel(string? seniorityLevel, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(seniorityLevel))
        {
            errors[SeniorityLevelField] = ["Seniority level is required."];
            return;
        }

        if (!CrewPositionSeniorityLevels.All.Contains(seniorityLevel.Trim()))
        {
            errors[SeniorityLevelField] = ["Seniority level is not supported."];
        }
    }
}
