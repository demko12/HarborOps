namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentFileStorageException(string message, Exception innerException)
    : Exception(message, innerException)
{
}
