namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentTypeNotFoundException(int documentTypeId)
    : Exception($"Document type '{documentTypeId}' was not found.")
{
    internal int DocumentTypeId { get; } = documentTypeId;
}
