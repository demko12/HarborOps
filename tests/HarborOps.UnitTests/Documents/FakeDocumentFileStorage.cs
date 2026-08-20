using HarborOps.Modules.Documents.Application;

namespace HarborOps.UnitTests.Documents;

internal sealed class FakeDocumentFileStorage : IDocumentFileStorage
{
    internal Dictionary<string, byte[]> SavedContents { get; } = new(StringComparer.Ordinal);

    internal List<string> DeletedStorageKeys { get; } = [];

    internal int SaveCallCount { get; private set; }

    internal int OpenCallCount { get; private set; }

    internal int DeleteCallCount { get; private set; }

    internal string? LastSavedStorageKey { get; private set; }

    internal string? LastOpenedStorageKey { get; private set; }

    internal Exception? ThrowOnSave { get; set; }

    internal Exception? ThrowOnOpen { get; set; }

    internal Exception? ThrowOnDelete { get; set; }

    public Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        SaveCallCount++;

        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        var storageKey = Guid.NewGuid().ToString("N");
        SavedContents[storageKey] = buffer.ToArray();
        LastSavedStorageKey = storageKey;

        return Task.FromResult(storageKey);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        OpenCallCount++;
        LastOpenedStorageKey = storageKey;

        if (ThrowOnOpen is not null)
        {
            throw ThrowOnOpen;
        }

        var content = SavedContents.TryGetValue(storageKey, out var bytes) ? bytes : [];

        return Task.FromResult<Stream>(new MemoryStream(content));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        DeleteCallCount++;
        DeletedStorageKeys.Add(storageKey);

        if (ThrowOnDelete is not null)
        {
            throw ThrowOnDelete;
        }

        SavedContents.Remove(storageKey);

        return Task.CompletedTask;
    }
}
