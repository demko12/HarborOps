namespace HarborOps.Modules.Documents.Application;

internal interface IDocumentFileStorage
{
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort delete: implementations must not throw when
    /// <paramref name="storageKey"/> does not exist in storage.
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
