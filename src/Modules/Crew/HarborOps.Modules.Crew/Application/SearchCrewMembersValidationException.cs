namespace HarborOps.Modules.Crew.Application;

internal sealed class SearchCrewMembersValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
