using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarborOps.UnitTests.Documents;

public sealed class ReplaceDocumentFileServiceTests
{
    private const string ExistingStorageKey = "existing-storage-key";

    private static readonly DateTime FixedUtcNow = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Replace_saves_new_file_updates_document_and_deletes_old_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var uploadedBy = Guid.NewGuid();
        var request = new UploadDocumentFileRequest(
            document.Id,
            "replacement.pdf",
            "application/pdf",
            4096,
            new MemoryStream(new byte[4096]),
            uploadedBy);

        var response = await service.ReplaceAsync(request, CancellationToken.None);

        Assert.Equal("replacement.pdf", response.FileName);
        Assert.Equal(4096, response.SizeBytes);
        Assert.Equal(uploadedBy, response.UploadedBy);
        Assert.Equal(FixedUtcNow, response.UploadedAtUtc);

        var persisted = await dbContext.Set<Document>().SingleAsync(CancellationToken.None);
        Assert.Equal(storage.LastSavedStorageKey, persisted.StorageKey);
        Assert.NotEqual(ExistingStorageKey, persisted.StorageKey);
        Assert.Equal("replacement.pdf", persisted.FileName);
        Assert.Equal(4096, persisted.SizeBytes);

        Assert.Equal(1, storage.DeleteCallCount);
        Assert.Equal(ExistingStorageKey, Assert.Single(storage.DeletedStorageKeys));
    }

    [Fact]
    public async Task Replace_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(Guid.NewGuid());

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.ReplaceAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_rejects_document_without_an_existing_file()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await Assert.ThrowsAsync<DocumentFileNotFoundException>(
            () => service.ReplaceAsync(request, CancellationToken.None));

        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task Replace_propagates_storage_failure_and_leaves_old_file_reference_untouched()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage { ThrowOnSave = new IOException("disk full") };
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await Assert.ThrowsAsync<DocumentFileStorageException>(
            () => service.ReplaceAsync(request, CancellationToken.None));

        var persisted = await dbContext.Set<Document>().SingleAsync(CancellationToken.None);
        Assert.Equal(ExistingStorageKey, persisted.StorageKey);
        Assert.Equal(0, storage.DeleteCallCount);
    }

    [Fact]
    public async Task Replace_succeeds_even_when_best_effort_delete_of_old_file_fails()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage { ThrowOnDelete = new IOException("locked") };
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        var response = await service.ReplaceAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        var persisted = await dbContext.Set<Document>().SingleAsync(CancellationToken.None);
        Assert.Equal(storage.LastSavedStorageKey, persisted.StorageKey);
        Assert.Equal(1, storage.DeleteCallCount);
    }

    [Fact]
    public async Task Replace_returns_rejected_document_to_draft()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedRejectedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await service.ReplaceAsync(request, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.DraftReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task Replace_does_not_change_review_status_when_draft()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await service.ReplaceAsync(request, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.DraftReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task Replace_does_not_change_review_status_when_pending_review()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedPendingReviewDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await service.ReplaceAsync(request, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.PendingReviewReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task Replace_does_not_change_review_status_when_approved()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedApprovedDocumentWithFileAsync(dbContext, documentTypeId);
        var storage = new FakeDocumentFileStorage();
        var service = CreateService(dbContext, storage);
        var request = CreateValidRequest(document.Id);

        await service.ReplaceAsync(request, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.ApprovedReviewStatus, persisted.ReviewStatus);
    }

    private static UploadDocumentFileRequest CreateValidRequest(Guid documentId)
    {
        return new UploadDocumentFileRequest(
            documentId,
            "replacement.pdf",
            "application/pdf",
            4096,
            new MemoryStream(new byte[4096]),
            Guid.NewGuid());
    }

    private static ReplaceDocumentFileService CreateService(DbContext dbContext, IDocumentFileStorage storage)
    {
        return new ReplaceDocumentFileService(
            dbContext,
            storage,
            Options.Create(new DocumentFileValidationOptions()),
            new FakeDateTimeProvider(FixedUtcNow),
            NullLogger<ReplaceDocumentFileService>.Instance);
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
            ExistingStorageKey,
            512,
            Guid.NewGuid(),
            FixedUtcNow.AddDays(-1));
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedPendingReviewDocumentWithFileAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedDocumentWithFileAsync(dbContext, documentTypeId);
        document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedApprovedDocumentWithFileAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedPendingReviewDocumentWithFileAsync(dbContext, documentTypeId);
        document.Approve(Guid.NewGuid(), FixedUtcNow);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedRejectedDocumentWithFileAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedPendingReviewDocumentWithFileAsync(dbContext, documentTypeId);
        document.Reject(Guid.NewGuid(), "Missing signature page.", FixedUtcNow);
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
