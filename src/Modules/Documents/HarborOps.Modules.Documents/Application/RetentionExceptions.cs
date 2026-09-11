namespace HarborOps.Modules.Documents.Application;

internal sealed class RetentionPolicyValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Retention policy validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class RetentionConflictException(string message) : Exception(message);
