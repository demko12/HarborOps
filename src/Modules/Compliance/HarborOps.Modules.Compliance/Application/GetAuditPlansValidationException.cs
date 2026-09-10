namespace HarborOps.Modules.Compliance.Application;

internal sealed class GetAuditPlansValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get audit plans validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
