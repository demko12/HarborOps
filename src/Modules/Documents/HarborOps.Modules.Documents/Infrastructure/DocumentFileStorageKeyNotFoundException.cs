namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class DocumentFileStorageKeyNotFoundException(string storageKey)
    : Exception($"No file exists for storage key '{storageKey}'.")
{
    internal string StorageKey { get; } = storageKey;
}
