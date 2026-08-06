namespace HarborOps.Modules.Documents.Application;

internal sealed class DuplicateDocumentNumberException(string number, Guid vesselId)
    : Exception($"A document with number '{number}' already exists for vessel '{vesselId}'.")
{
}
