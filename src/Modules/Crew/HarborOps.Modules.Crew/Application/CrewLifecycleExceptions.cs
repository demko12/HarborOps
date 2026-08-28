namespace HarborOps.Modules.Crew.Application;

internal sealed class CrewLifecycleValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Crew lifecycle validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
