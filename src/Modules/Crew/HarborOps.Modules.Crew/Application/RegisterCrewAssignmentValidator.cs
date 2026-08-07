namespace HarborOps.Modules.Crew.Application;

using HarborOps.Modules.Crew.Contracts;

internal static class RegisterCrewAssignmentValidator
{
    internal const string CrewMemberIdField = "crewMemberId";
    internal const string PlannedSignOffDateField = "plannedSignOffDate";
    internal const string RankIdField = "rankId";
    internal const string SignOnDateField = "signOnDate";
    internal const string SignOnPortIdField = "signOnPortId";
    internal const string VesselIdField = "vesselId";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterCrewAssignmentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredId(request.CrewMemberId, CrewMemberIdField, "Crew member ID", errors);
        ValidateRequiredId(request.VesselId, VesselIdField, "Vessel ID", errors);
        ValidateRequiredId(request.RankId, RankIdField, "Rank ID", errors);
        ValidateOptionalId(request.SignOnPortId, SignOnPortIdField, "Sign-on port ID", errors);

        if (request.SignOnDate is null)
        {
            errors[SignOnDateField] = ["Sign-on date is required."];
        }

        ValidatePlannedSignOffDate(request.SignOnDate, request.PlannedSignOffDate, errors);

        return errors;
    }

    private static void ValidatePlannedSignOffDate(
        DateOnly? signOnDate,
        DateOnly? plannedSignOffDate,
        IDictionary<string, string[]> errors)
    {
        if (plannedSignOffDate is null)
        {
            errors[PlannedSignOffDateField] = ["Planned sign-off date is required."];
            return;
        }

        if (signOnDate is not null && plannedSignOffDate.Value <= signOnDate.Value)
        {
            errors[PlannedSignOffDateField] = ["Planned sign-off date must be later than the sign-on date."];
        }
    }

    private static void ValidateRequiredId(
        Guid? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null || value.Value == Guid.Empty)
        {
            errors[field] = [$"{displayName} is required."];
        }
    }

    private static void ValidateOptionalId(
        Guid? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is not null && value.Value == Guid.Empty)
        {
            errors[field] = [$"{displayName} must not be empty."];
        }
    }
}
