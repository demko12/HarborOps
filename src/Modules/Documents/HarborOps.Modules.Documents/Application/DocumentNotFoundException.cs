namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentNotFoundException(Guid documentId)
    : Exception($"Document '{documentId}' was not found.")
{
    internal Guid DocumentId { get; } = documentId;
}
