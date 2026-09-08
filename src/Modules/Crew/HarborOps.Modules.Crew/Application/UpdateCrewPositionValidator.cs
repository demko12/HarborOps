using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class UpdateCrewPositionValidator
{
    internal static IReadOnlyDictionary<string, string[]> Validate(UpdateCrewPositionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        CrewPositionFieldValidation.ValidateRankName(request.RankName, errors);
        CrewPositionFieldValidation.ValidateDepartment(request.Department, errors);
        CrewPositionFieldValidation.ValidateSeniorityLevel(request.SeniorityLevel, errors);

        return errors;
    }
}
