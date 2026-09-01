namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetNonConformitiesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get non-conformities validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
