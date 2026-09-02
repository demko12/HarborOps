namespace HarborOps.Modules.Compliance.Application;

internal sealed class RecordCorrectiveActionValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record corrective action validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
