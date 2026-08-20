namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentFileNotFoundException(Guid documentId)
    : Exception($"Document '{documentId}' does not have an attached file.")
{
    internal Guid DocumentId { get; } = documentId;
}
