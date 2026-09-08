using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentLifecycleServiceReviewTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    // ---- SubmitForReviewAsync ----

    [Fact]
    public async Task SubmitForReviewAsync_moves_document_to_pending_review_and_records_event()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentWithFileAsync(dbContext, documentTypeId);
        var actorId = Guid.NewGuid();
        var service = CreateService(dbContext);

        await service.SubmitForReviewAsync(document.Id, actorId, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.PendingReviewReviewStatus, persisted.ReviewStatus);

        var reviewEvent = await dbContext.Set<DocumentReviewEvent>().SingleAsync(CancellationToken.None);
        Assert.Equal(document.Id, reviewEvent.DocumentId);
        Assert.Equal(DocumentReviewEvent.SubmittedAction, reviewEvent.Action);
        Assert.Equal(actorId, reviewEvent.ActorUserId);
        Assert.Equal(FixedUtcNow, reviewEvent.OccurredAtUtc);
        Assert.Null(reviewEvent.Notes);
    }

    [Fact]
    public async Task SubmitForReviewAsync_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.SubmitForReviewAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SubmitForReviewAsync_propagates_domain_guard_when_document_has_no_file()
    {
        // The service does not pre-check state before calling the Domain method (see the
        // Presentation-layer report's documented deviation #3) - Document.SubmitForReview's
        // own InvalidOperationException is expected to reach the caller unwrapped.
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitForReviewAsync(document.Id, Guid.NewGuid(), CancellationToken.None));
    }

    // ---- ReviewAsync ----

    [Fact]
    public async Task ReviewAsync_approves_when_reviewer_differs_from_submitter()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var submitterId = Guid.NewGuid();
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId, submitterId);
        var reviewerId = Guid.NewGuid();
        var service = CreateService(dbContext);

        await service.ReviewAsync(document.Id, reviewerId, "Approved", null, CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.ApprovedReviewStatus, persisted.ReviewStatus);

        var reviewEvent = await dbContext.Set<DocumentReviewEvent>()
            .SingleAsync(e => e.Action == DocumentReviewEvent.ApprovedAction, CancellationToken.None);
        Assert.Equal(reviewerId, reviewEvent.ActorUserId);
        Assert.Null(reviewEvent.Notes);
    }

    [Fact]
    public async Task ReviewAsync_rejects_when_reviewer_differs_from_submitter_and_records_notes()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var submitterId = Guid.NewGuid();
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId, submitterId);
        var reviewerId = Guid.NewGuid();
        var service = CreateService(dbContext);

        await service.ReviewAsync(document.Id, reviewerId, "Rejected", "Missing signature page.", CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.RejectedReviewStatus, persisted.ReviewStatus);

        var reviewEvent = await dbContext.Set<DocumentReviewEvent>()
            .SingleAsync(e => e.Action == DocumentReviewEvent.RejectedAction, CancellationToken.None);
        Assert.Equal(reviewerId, reviewEvent.ActorUserId);
        Assert.Equal("Missing signature page.", reviewEvent.Notes);
    }

    [Fact]
    public async Task ReviewAsync_rejects_self_review()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var submitterId = Guid.NewGuid();
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId, submitterId);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<DocumentLifecycleConflictException>(
            () => service.ReviewAsync(document.Id, submitterId, "Approved", null, CancellationToken.None));

        Assert.Equal("The reviewer must be different from the submitter.", exception.Message);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.PendingReviewReviewStatus, persisted.ReviewStatus);
    }

    [Fact]
    public async Task ReviewAsync_propagates_domain_guard_when_document_not_pending_review()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReviewAsync(document.Id, Guid.NewGuid(), "Approved", null, CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_rejects_unknown_decision()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId, Guid.NewGuid());
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ReviewAsync(document.Id, Guid.NewGuid(), "Maybe", null, CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.ReviewAsync(Guid.NewGuid(), Guid.NewGuid(), "Approved", null, CancellationToken.None));
    }

    // ---- RevokeAsync ----

    [Fact]
    public async Task RevokeAsync_revokes_active_approved_document_and_records_event()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedApprovedActiveDocumentAsync(dbContext, documentTypeId);
        var actorId = Guid.NewGuid();
        var service = CreateService(dbContext);

        await service.RevokeAsync(document.Id, actorId, "No longer valid.", CancellationToken.None);

        var persisted = await dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id, CancellationToken.None);
        Assert.Equal(Document.RevokedStatus, persisted.Status);
        Assert.Equal(Document.ApprovedReviewStatus, persisted.ReviewStatus);

        var reviewEvent = await dbContext.Set<DocumentReviewEvent>()
            .SingleAsync(e => e.Action == DocumentReviewEvent.RevokedAction, CancellationToken.None);
        Assert.Equal(actorId, reviewEvent.ActorUserId);
        Assert.Equal("No longer valid.", reviewEvent.Notes);
    }

    [Fact]
    public async Task RevokeAsync_propagates_domain_guard_when_not_active_and_approved()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RevokeAsync(document.Id, Guid.NewGuid(), "Reason.", CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), "Reason.", CancellationToken.None));
    }

    // ---- GetReviewHistoryAsync ----

    [Fact]
    public async Task GetReviewHistoryAsync_returns_empty_list_when_no_events_exist()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var service = CreateService(dbContext);

        var history = await service.GetReviewHistoryAsync(document.Id, CancellationToken.None);

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetReviewHistoryAsync_returns_only_events_for_the_requested_document()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        var otherDocument = await SeedDraftDocumentAsync(dbContext, documentTypeId, number: "CERT-9002");

        await dbContext.Set<DocumentReviewEvent>().AddRangeAsync(
            new DocumentReviewEvent(document.Id, DocumentReviewEvent.SubmittedAction, Guid.NewGuid(), FixedUtcNow, null),
            new DocumentReviewEvent(otherDocument.Id, DocumentReviewEvent.SubmittedAction, Guid.NewGuid(), FixedUtcNow, null));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var history = await service.GetReviewHistoryAsync(document.Id, CancellationToken.None);

        var item = Assert.Single(history);
        Assert.Equal(document.Id, item.DocumentId);
    }

    [Fact]
    public async Task GetReviewHistoryAsync_orders_ties_by_id_when_occurred_at_utc_is_identical()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);

        // All three share the exact same OccurredAtUtc, forcing GetReviewHistoryAsync's
        // OrderBy(OccurredAtUtc).ThenBy(Id) to fall back to the Id tie-breaker.
        var events = new[]
        {
            new DocumentReviewEvent(document.Id, DocumentReviewEvent.SubmittedAction, Guid.NewGuid(), FixedUtcNow, null),
            new DocumentReviewEvent(document.Id, DocumentReviewEvent.ApprovedAction, Guid.NewGuid(), FixedUtcNow, null),
            new DocumentReviewEvent(document.Id, DocumentReviewEvent.RejectedAction, Guid.NewGuid(), FixedUtcNow, "Notes."),
        };
        await dbContext.Set<DocumentReviewEvent>().AddRangeAsync(events);
        await dbContext.SaveChangesAsync();

        var expectedOrder = events.Select(e => e.Id).OrderBy(id => id).ToList();

        var service = CreateService(dbContext);
        var history = await service.GetReviewHistoryAsync(document.Id, CancellationToken.None);

        Assert.Equal(expectedOrder, history.Select(item => item.Id).ToList());
    }

    [Fact]
    public async Task GetReviewHistoryAsync_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.GetReviewHistoryAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Concurrency ----
    //
    // Investigated before writing: EF Core's InMemory provider DOES evaluate a plain
    // (non-rowversion, application-assigned) IsConcurrencyToken() property by comparing
    // the original tracked value against the current store value at SaveChanges time -
    // unlike the rowversion/store-generated case, no database-side auto-increment is
    // needed for this check, so InMemory's documented limitations around store-generated
    // values do not apply here. Verified empirically: this test passes against the actual
    // DocumentConfiguration (ConcurrencyToken is IsConcurrencyToken() but not IsRowVersion()).
    [Fact]
    public async Task Concurrent_saves_against_the_same_document_throw_DbUpdateConcurrencyException()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid documentId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var documentTypeId = await SeedDocumentTypeAsync(seedContext);
            var document = await SeedPendingReviewDocumentAsync(seedContext, documentTypeId, Guid.NewGuid());
            documentId = document.Id;
        }

        await using var firstContext = CreateDbContext(databaseName);
        await using var secondContext = CreateDbContext(databaseName);

        var firstDocument = await firstContext.Set<Document>().SingleAsync(d => d.Id == documentId);
        var secondDocument = await secondContext.Set<Document>().SingleAsync(d => d.Id == documentId);

        firstDocument.Approve(Guid.NewGuid(), FixedUtcNow);
        await firstContext.SaveChangesAsync();

        secondDocument.Reject(Guid.NewGuid(), "Notes.", FixedUtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private static DocumentLifecycleService CreateService(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new Modules.Documents.Domain.DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<Modules.Documents.Domain.DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task<Document> SeedDraftDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId, string number = "CERT-9001")
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            number,
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedDraftDocumentWithFileAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedDraftDocumentAsync(dbContext, documentTypeId);
        document.AttachFile("file.pdf", "application/pdf", "storage-key", 100, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedPendingReviewDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId, Guid submitterId)
    {
        var document = await SeedDraftDocumentWithFileAsync(dbContext, documentTypeId);
        document.SubmitForReview(submitterId, FixedUtcNow);

        // The self-review check in ReviewAsync reads the Submitted DocumentReviewEvent to
        // find the submitter, so seeding must recreate it too, not just the Document's own
        // state transition - otherwise self-review tests exercise a submitter-less document
        // that can never trigger the check.
        await dbContext.Set<DocumentReviewEvent>().AddAsync(
            new DocumentReviewEvent(document.Id, DocumentReviewEvent.SubmittedAction, submitterId, FixedUtcNow, null));
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static async Task<Document> SeedApprovedActiveDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = await SeedPendingReviewDocumentAsync(dbContext, documentTypeId, Guid.NewGuid());
        document.Approve(Guid.NewGuid(), FixedUtcNow);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
