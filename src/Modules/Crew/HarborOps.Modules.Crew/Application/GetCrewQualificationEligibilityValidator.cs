using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class GetCrewQualificationEligibilityValidator
{
    internal const string CrewMemberIdField = "crewMemberId";
    internal const string RankIdField = "rankId";
    internal const string FromField = "from";
    internal const string ToField = "to";

    internal const int MaxWindowDays = 366;

    internal static IReadOnlyDictionary<string, string[]> Validate(GetCrewQualificationEligibilityRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CrewMemberId is null || request.CrewMemberId.Value == Guid.Empty)
        {
            errors[CrewMemberIdField] = ["Crew member ID is required."];
        }

        if (request.RankId is null || request.RankId.Value == Guid.Empty)
        {
            errors[RankIdField] = ["Rank ID is required."];
        }

        if (request.From is null)
        {
            errors[FromField] = ["From date is required."];
        }

        if (request.To is null)
        {
            errors[ToField] = ["To date is required."];
        }
        else if (request.From is not null)
        {
            if (request.To.Value < request.From.Value)
            {
                errors[ToField] = ["To date cannot precede the from date."];
            }
            else if (request.To.Value.DayNumber - request.From.Value.DayNumber + 1 > MaxWindowDays)
            {
                errors[ToField] = [$"Date range must not exceed {MaxWindowDays} days."];
            }
        }

        return errors;
    }
}
