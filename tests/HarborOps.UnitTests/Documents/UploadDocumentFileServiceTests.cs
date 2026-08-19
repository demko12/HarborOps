using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarborOps.UnitTests.Documents;

public sealed class UploadDocumentFileServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Upload_saves_file_and_updates_document()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var uploadedBy = Guid.NewGuid();
        var request = new UploadDocumentFileRequest(
            document.Id,
            "certificate.pdf",
            "application/pdf",
            2048,
            new MemoryStream(new byte[2048]),
            uploadedBy);

        var response = await service.UploadAsync(request, CancellationToken.None);

        Assert.Equal(1, storage.SaveCallCount);
        Assert.Equal("certificate.pdf", response.FileName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Equal(2048, response.SizeBytes);
        Assert.Equal(uploadedBy, response.UploadedBy);
        Assert.Equal(FixedUtcNow, response.UploadedAtUtc);

        var persisted = await dbContext.Set<Document>().SingleAsync(CancellationToken.None);
        Assert.True(persisted.HasFile);
        Assert.Equal(storage.LastSavedStorageKey, persisted.StorageKey);
        Assert.Equal("certificate.pdf", persisted.FileName);
        Assert.Equal("application/pdf", persisted.ContentType);
        Assert.Equal(2048, persisted.SizeBytes);
        Assert.Equal(uploadedBy, persisted.UploadedBy);
        Assert.Equal(FixedUtcNow, persisted.UploadedAtUtc);
    }

    [Fact]
    public async Task Upload_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(Guid.NewGuid());

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.UploadAsync(request, CancellationToken.None));

        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task Upload_rejects_document_that_already_has_a_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await Assert.ThrowsAsync<DocumentAlreadyHasFileException>(
            () => service.UploadAsync(request, CancellationToken.None));

        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task Upload_rejects_invalid_request_before_calling_storage()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id) with { ContentType = " " };

        await Assert.ThrowsAsync<UploadDocumentFileValidationException>(
            () => service.UploadAsync(request, CancellationToken.None));

        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task Upload_propagates_storage_failure_and_leaves_document_without_a_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage { ThrowOnSave = new IOException("disk full") };
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await Assert.ThrowsAsync<DocumentFileStorageException>(
            () => service.UploadAsync(request, CancellationToken.None));

        var persisted = await dbContext.Set<Document>().SingleAsync(CancellationToken.None);
        Assert.False(persisted.HasFile);
        Assert.Null(persisted.StorageKey);
    }

    private static UploadDocumentFileRequest CreateValidRequest(Guid documentId)
    {
        return new UploadDocumentFileRequest(
            documentId,
            "certificate.pdf",
            "application/pdf",
            2048,
            new MemoryStream(new byte[2048]),
            Guid.NewGuid());
    }

    private static UploadDocumentFileService CreateService(DbContext dbContext, IDocumentFileStorage storage)
    {
        return new UploadDocumentFileService(
            dbContext,
            storage,
            Options.Create(new DocumentFileValidationOptions()),
            new FakeDateTimeProvider(FixedUtcNow),
            NullLogger<UploadDocumentFileService>.Instance);
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

    private static async Task<Document> SeedDocumentWithFileAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        document.AttachFile(
            "existing.pdf",
            "application/pdf",
            "existing-storage-key",
            512,
            Guid.NewGuid(),
            FixedUtcNow.AddDays(-1));
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
