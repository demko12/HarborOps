namespace HarborOps.Modules.Documents.Application;

internal sealed class GetDocumentTypesValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get document types validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
