using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentFileQueryTests
{
    [Fact]
    public async Task GetFile_returns_content_from_storage_for_existing_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        document.AttachFile(
            "certificate.pdf", "application/pdf", "storage-key-1", 5, Guid.NewGuid(), DateTime.UtcNow);
        await dbContext.SaveChangesAsync();

        var storage = new FakeDocumentFileStorage();
        var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
        storage.SavedContents["storage-key-1"] = expectedBytes;
        var query = new GetDocumentFileQuery(dbContext, storage, NullLogger<GetDocumentFileQuery>.Instance);

        var result = await query.GetFileAsync(document.Id, CancellationToken.None);

        Assert.Equal("certificate.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(5, result.SizeBytes);
        Assert.Equal("storage-key-1", storage.LastOpenedStorageKey);

        using var reader = new MemoryStream();
        await result.Content.CopyToAsync(reader);
        Assert.Equal(expectedBytes, reader.ToArray());
    }

    [Fact]
    public async Task GetFile_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeDocumentFileStorage();
        var query = new GetDocumentFileQuery(dbContext, storage, NullLogger<GetDocumentFileQuery>.Instance);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => query.GetFileAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetFile_rejects_document_without_a_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var query = new GetDocumentFileQuery(dbContext, storage, NullLogger<GetDocumentFileQuery>.Instance);

        await Assert.ThrowsAsync<DocumentFileNotFoundException>(
            () => query.GetFileAsync(document.Id, CancellationToken.None));

        Assert.Equal(0, storage.OpenCallCount);
    }

    [Fact]
    public async Task GetFile_propagates_storage_failure_as_document_file_storage_exception()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        document.AttachFile(
            "certificate.pdf", "application/pdf", "storage-key-1", 5, Guid.NewGuid(), DateTime.UtcNow);
        await dbContext.SaveChangesAsync();

        var storage = new FakeDocumentFileStorage { ThrowOnOpen = new IOException("missing on disk") };
        var query = new GetDocumentFileQuery(dbContext, storage, NullLogger<GetDocumentFileQuery>.Instance);

        await Assert.ThrowsAsync<DocumentFileStorageException>(
            () => query.GetFileAsync(document.Id, CancellationToken.None));
    }

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task<Document> SeedDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            "CERT-1001",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            "Active");

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
