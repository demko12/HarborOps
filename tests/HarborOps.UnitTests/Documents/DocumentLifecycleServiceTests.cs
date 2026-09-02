using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

// Scoped to the correction/resubmission hook added to CorrectMetadataAsync as part of the
// review workflow (Rejected -> Draft on a successful metadata correction). No test file
// existed for DocumentLifecycleService before this - broader coverage of
// CorrectMetadataAsync/RenewAsync/GetHistoryAsync is out of scope here.
public sealed class DocumentLifecycleServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CorrectMetadataAsync_returns_rejected_document_to_draft()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedRejectedDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await service.CorrectMetadataAsync(document.Id, ValidRequest(documentTypeId), CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.DraftReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task CorrectMetadataAsync_does_not_change_review_status_when_draft()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await service.CorrectMetadataAsync(document.Id, ValidRequest(documentTypeId), CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.DraftReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task CorrectMetadataAsync_does_not_change_review_status_when_pending_review()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await service.CorrectMetadataAsync(document.Id, ValidRequest(documentTypeId), CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.PendingReviewReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task CorrectMetadataAsync_does_not_change_review_status_when_approved()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedApprovedDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await service.CorrectMetadataAsync(document.Id, ValidRequest(documentTypeId), CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.ApprovedReviewStatus, persisted.ReviewStatus);
    }

    private static CorrectDocumentMetadataRequest ValidRequest(int documentTypeId) => new(
        documentTypeId,
        "CERT-9001-CORRECTED",
        new DateOnly(2026, 2, 1),
        new DateOnly(2027, 2, 1),
        "Flag State Authority");

    private static DocumentLifecycleService CreateService(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

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

    private static async Task<Document> SeedDraftDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            "CERT-9001",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            Document.ActiveStatus);
        document.AttachFile("file.pdf", "application/pdf", "storage-key", 100, Guid.NewGuid(), FixedUtcNow.UtcDateTime);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedPendingReviewDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        document.SubmitForReview(Guid.NewGuid(), FixedUtcNow);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedRejectedDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId);
        document.Reject(Guid.NewGuid(), "Missing signature page.", FixedUtcNow);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedApprovedDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId);
        document.Approve(Guid.NewGuid(), FixedUtcNow);
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
