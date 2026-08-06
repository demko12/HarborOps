namespace HarborOps.Modules.Documents.Application;

internal sealed class RegisterDocumentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register document validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
