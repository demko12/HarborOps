namespace HarborOps.Modules.Compliance.Application;

internal sealed class RegisterNonConformityValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register non-conformity validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
