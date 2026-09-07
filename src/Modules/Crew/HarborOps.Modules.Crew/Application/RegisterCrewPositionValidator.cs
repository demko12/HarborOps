using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class RegisterCrewPositionValidator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterCrewPositionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        CrewPositionFieldValidation.ValidateRankName(request.RankName, errors);
        CrewPositionFieldValidation.ValidateDepartment(request.Department, errors);
        CrewPositionFieldValidation.ValidateSeniorityLevel(request.SeniorityLevel, errors);

        return errors;
    }
}
