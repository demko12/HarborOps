namespace HarborOps.Modules.Documents.Application;

internal sealed class GetExpiringDocumentsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get expiring documents validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
