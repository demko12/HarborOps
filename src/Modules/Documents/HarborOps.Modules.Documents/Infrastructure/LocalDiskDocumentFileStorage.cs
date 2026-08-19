using HarborOps.Modules.Documents.Application;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class LocalDiskDocumentFileStorage(
    IOptions<LocalDiskDocumentFileStorageOptions> options)
    : IDocumentFileStorage
{
    private readonly string _rootPath = ResolveRootPath(options.Value.RootPath);

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var storageKey = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_rootPath, storageKey);

        try
        {
            Directory.CreateDirectory(_rootPath);

            await using var fileStream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                });

            await content.CopyToAsync(fileStream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DocumentFileStorageException("Failed to save the document file to storage.", exception);
        }

        return storageKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        ValidateStorageKey(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(_rootPath, storageKey);

        if (!File.Exists(path))
        {
            throw new DocumentFileStorageKeyNotFoundException(storageKey);
        }

        try
        {
            Stream stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous,
                });

            return Task.FromResult(stream);
        }
        catch (Exception exception)
        {
            throw new DocumentFileStorageException("Failed to open the document file from storage.", exception);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        ValidateStorageKey(storageKey);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(_rootPath, storageKey);

        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception exception)
        {
            throw new DocumentFileStorageException("Failed to delete the document file from storage.", exception);
        }

        return Task.CompletedTask;
    }

    private static void ValidateStorageKey(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey) || !Guid.TryParseExact(storageKey, "N", out _))
        {
            throw new ArgumentException("Storage key is not a valid opaque key.", nameof(storageKey));
        }
    }

    private static string ResolveRootPath(string configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return configuredRootPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HarborOps",
            "DocumentFiles");
    }
}
