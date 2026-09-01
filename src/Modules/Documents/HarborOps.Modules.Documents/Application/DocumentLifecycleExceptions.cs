namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentLifecycleValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Document metadata validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DocumentLifecycleConflictException(string message) : Exception(message);
