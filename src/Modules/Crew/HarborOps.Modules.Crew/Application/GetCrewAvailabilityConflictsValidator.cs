using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.Modules.Crew.Application;

internal static class GetCrewAvailabilityConflictsValidator
{
    internal const string CrewMemberIdField = "crewMemberId";
    internal const string FromField = "from";
    internal const string ToField = "to";

    internal static IReadOnlyDictionary<string, string[]> Validate(GetCrewAvailabilityConflictsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.CrewMemberId is null || request.CrewMemberId.Value == Guid.Empty)
        {
            errors[CrewMemberIdField] = ["Crew member ID is required."];
        }

        if (request.From is null)
        {
            errors[FromField] = ["From date is required."];
        }

        if (request.To is null)
        {
            errors[ToField] = ["To date is required."];
        }
        else if (request.From is not null && request.To.Value < request.From.Value)
        {
            errors[ToField] = ["To date cannot precede the from date."];
        }

        return errors;
    }
}
