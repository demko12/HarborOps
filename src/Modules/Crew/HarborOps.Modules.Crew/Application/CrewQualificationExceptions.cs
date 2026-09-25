namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewQualificationPolicyNotFoundException(Guid policyId)
    : Exception($"Crew qualification policy with ID '{policyId}' was not found.")
{
    internal Guid PolicyId { get; } = policyId;
}

internal sealed class DuplicateCrewQualificationPolicyException(Guid crewPositionId)
    : Exception($"A qualification policy already exists for crew position '{crewPositionId}'.")
{
    internal Guid CrewPositionId { get; } = crewPositionId;
}

internal sealed class CreateQualificationPolicyValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create qualification policy validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateQualificationPolicyValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update qualification policy validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetQualificationPoliciesValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get qualification policies validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetCrewQualificationEligibilityValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get crew qualification eligibility validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CrewMemberNotQualifiedException(
    Guid crewMemberId,
    Guid rankId,
    IReadOnlyList<string> missingTypes,
    IReadOnlyList<CrewQualificationCoverage.UncoveredRange> uncoveredRanges)
    : Exception(
        $"Crew member '{crewMemberId}' is not qualified for rank '{rankId}': missing required certificate "
        + $"type(s) {string.Join(", ", missingTypes)} for the following period(s): "
        + $"{string.Join("; ", uncoveredRanges.Select(range => $"{range.RequiredType} {range.StartDate:yyyy-MM-dd} to {range.EndDate:yyyy-MM-dd}"))}.")
{
    internal Guid CrewMemberId { get; } = crewMemberId;
    internal Guid RankId { get; } = rankId;
    internal IReadOnlyList<string> MissingTypes { get; } = missingTypes;
    internal IReadOnlyList<CrewQualificationCoverage.UncoveredRange> UncoveredRanges { get; } = uncoveredRanges;
}
