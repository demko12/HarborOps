namespace HarborOps.Modules.Documents.Application;

internal sealed class UploadDocumentFileValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Upload document file validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
