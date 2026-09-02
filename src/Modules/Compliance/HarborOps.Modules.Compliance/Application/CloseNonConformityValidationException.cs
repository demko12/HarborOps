namespace HarborOps.Modules.Compliance.Application;

internal sealed class CloseNonConformityValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Close non-conformity validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
