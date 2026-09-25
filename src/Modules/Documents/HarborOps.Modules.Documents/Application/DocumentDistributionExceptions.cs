namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentDistributionValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("Document distribution validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
internal sealed class DocumentDistributionConflictException(string message) : Exception(message);
internal sealed class DocumentDistributionIssueNotFoundException(Guid id) : Exception($"Document distribution issue '{id}' was not found.");
