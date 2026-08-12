namespace HarborOps.Modules.Voyages.Application;

internal sealed class GetVoyagesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get voyages validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
