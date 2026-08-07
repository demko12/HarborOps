namespace HarborOps.Modules.Compliance.Application;

internal sealed class RegisterPscInspectionValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register PSC inspection validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
