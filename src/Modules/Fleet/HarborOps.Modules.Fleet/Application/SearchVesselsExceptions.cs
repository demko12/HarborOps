namespace HarborOps.Modules.Fleet.Application;

internal sealed class SearchVesselsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search vessels validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
