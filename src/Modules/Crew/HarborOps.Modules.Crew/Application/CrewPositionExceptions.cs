namespace HarborOps.Modules.Crew.Application;

internal sealed class RegisterCrewPositionValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register crew position validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateCrewPositionValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update crew position validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DuplicateCrewPositionException(string rankName, string department)
    : Exception($"A position with rank '{rankName}' in department '{department}' already exists.")
{
    internal string RankName { get; } = rankName;
    internal string Department { get; } = department;
}
