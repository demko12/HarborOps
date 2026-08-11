namespace HarborOps.Modules.Documents.Application;

internal sealed class RegisterDocumentTypeValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register document type validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
