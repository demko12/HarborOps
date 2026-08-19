namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get documents validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
