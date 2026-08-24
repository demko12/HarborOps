namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewRotationsValidationException(
    IReadOnlyDictionary<string, string[]> errors) : Exception
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
