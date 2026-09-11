namespace HarborOps.Modules.Documents.Application;

internal sealed class GetRetentionQueueValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get retention queue validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
