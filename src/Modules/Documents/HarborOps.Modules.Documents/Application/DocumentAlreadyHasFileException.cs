namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentAlreadyHasFileException(Guid documentId)
    : Exception($"Document '{documentId}' already has an attached file.")
{
    internal Guid DocumentId { get; } = documentId;
}
