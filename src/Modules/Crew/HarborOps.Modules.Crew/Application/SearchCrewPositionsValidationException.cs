namespace HarborOps.Modules.Crew.Application;

internal sealed class SearchCrewPositionsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search crew positions validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
