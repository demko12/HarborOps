namespace HarborOps.Modules.Voyages.Application;

internal sealed class RecordDelayValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record delay validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
