using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class DispositionRequestServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    // ---- CreateAsync ----

    [Fact]
    public async Task CreateAsync_succeeds_when_document_is_past_its_due_date()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 100);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var requesterId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var response = await service.CreateAsync(documentId, new CreateDispositionRequest("Retention period elapsed."), requesterId, CancellationToken.None);

        Assert.Equal(DispositionRequest.PendingStatus, response.Status);
        var state = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(DocumentRetentionState.PendingDispositionState, state.State);
    }

    [Fact]
    public async Task CreateAsync_at_exact_due_date_boundary_succeeds()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        var supersededAtUtc = FixedUtcNow.UtcDateTime.AddDays(-30);
        var documentId = await SeedSupersededDocumentAsync(dbContext, documentTypeId, supersededAtUtc);
        var service = CreateService(dbContext);

        var response = await service.CreateAsync(documentId, new CreateDispositionRequest("Due today."), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DispositionRequest.PendingStatus, response.Status);
    }

    [Fact]
    public async Task CreateAsync_rejects_when_not_yet_due()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 1000);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RetentionConflictException>(
            () => service.CreateAsync(documentId, new CreateDispositionRequest("Too early."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_when_document_has_no_lifecycle_trigger()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 1);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2027, 1, 1));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RetentionConflictException>(
            () => service.CreateAsync(documentId, new CreateDispositionRequest("Not eligible."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_when_no_retention_policy_exists()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RetentionConflictException>(
            () => service.CreateAsync(documentId, new CreateDispositionRequest("No policy."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_when_an_active_legal_hold_exists()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 1);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        await dbContext.Set<LegalHold>().AddAsync(LegalHold.Place(documentId, "Litigation.", null, Guid.NewGuid(), FixedUtcNow.UtcDateTime));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<RetentionConflictException>(
            () => service.CreateAsync(documentId, new CreateDispositionRequest("Blocked."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_allows_request_when_a_released_hold_exists()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 1);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var hold = LegalHold.Place(documentId, "Litigation.", null, Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        hold.Release(Guid.NewGuid(), "Resolved.", FixedUtcNow.UtcDateTime);
        await dbContext.Set<LegalHold>().AddAsync(hold);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.CreateAsync(documentId, new CreateDispositionRequest("Now eligible."), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DispositionRequest.PendingStatus, response.Status);
    }

    [Fact]
    public async Task CreateAsync_captures_policy_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 100);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var service = CreateService(dbContext);

        var response = await service.CreateAsync(documentId, new CreateDispositionRequest("Reason."), Guid.NewGuid(), CancellationToken.None);

        var persisted = await dbContext.Set<DispositionRequest>().SingleAsync(r => r.Id == response.Id);
        Assert.Equal(100, persisted.SnapshotRetentionPeriodDays);
        Assert.Equal(new DateTime(2026, 1, 1), persisted.SnapshotLifecycleTriggerAtUtc);
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.CreateAsync(Guid.NewGuid(), new CreateDispositionRequest("Reason."), Guid.NewGuid(), CancellationToken.None));
    }

    // ---- ApproveAsync ----

    [Fact]
    public async Task ApproveAsync_by_different_user_marks_document_disposed()
    {
        await using var dbContext = CreateDbContext();
        var (documentId, requestId, requesterId) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);

        var response = await service.ApproveAsync(requestId, new ApproveDispositionRequestRequest("Approved."), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DispositionRequest.ApprovedStatus, response.Status);
        var state = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(DocumentRetentionState.DisposedState, state.State);
        var persistedRequest = await dbContext.Set<DispositionRequest>().AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(requesterId, persistedRequest.RequestedByUserId);
    }

    [Fact]
    public async Task ApproveAsync_by_requester_throws_and_does_not_persist()
    {
        await using var dbContext = CreateDbContext();
        var (documentId, requestId, requesterId) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(requestId, new ApproveDispositionRequestRequest("Approved."), requesterId, CancellationToken.None));

        var persistedRequest = await dbContext.Set<DispositionRequest>().AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(DispositionRequest.PendingStatus, persistedRequest.Status);
        var state = await dbContext.Set<DocumentRetentionState>().AsNoTracking().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(DocumentRetentionState.PendingDispositionState, state.State);
    }

    [Fact]
    public async Task ApproveAsync_when_already_decided_throws()
    {
        await using var dbContext = CreateDbContext();
        var (_, requestId, _) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);
        await service.ApproveAsync(requestId, new ApproveDispositionRequestRequest("Approved."), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(requestId, new ApproveDispositionRequestRequest("Approved again."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_rejects_unknown_request()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DispositionRequestNotFoundException>(
            () => service.ApproveAsync(Guid.NewGuid(), new ApproveDispositionRequestRequest("Approved."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_does_not_persist_when_retention_state_is_inconsistent()
    {
        // Force the retention state back out of PendingDisposition while the request stays
        // Pending, to prove Approve+MarkDisposed commit atomically: MarkDisposed's failure
        // must leave the already-mutated in-memory DispositionRequest.Approve() unpersisted.
        await using var dbContext = CreateDbContext();
        var (documentId, requestId, _) = await SeedPendingDispositionAsync(dbContext);
        var state = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        state.RevertPendingDisposition(FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(requestId, new ApproveDispositionRequestRequest("Approved."), Guid.NewGuid(), CancellationToken.None));

        var persistedRequest = await dbContext.Set<DispositionRequest>().AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(DispositionRequest.PendingStatus, persistedRequest.Status);
    }

    [Fact]
    public async Task ApproveAsync_ignores_a_later_policy_change_and_keeps_the_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 100);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var service = CreateService(dbContext);
        var created = await service.CreateAsync(documentId, new CreateDispositionRequest("Reason."), Guid.NewGuid(), CancellationToken.None);

        var policy = await dbContext.Set<RetentionPolicy>().SingleAsync(p => p.DocumentTypeId == documentTypeId);
        policy.UpdateRetentionPeriod(9999);
        await dbContext.SaveChangesAsync();

        await service.ApproveAsync(created.Id, new ApproveDispositionRequestRequest(null), Guid.NewGuid(), CancellationToken.None);

        var persisted = await dbContext.Set<DispositionRequest>().AsNoTracking().SingleAsync(r => r.Id == created.Id);
        Assert.Equal(100, persisted.SnapshotRetentionPeriodDays);
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_by_different_user_reverts_to_prior_state()
    {
        await using var dbContext = CreateDbContext();
        var (documentId, requestId, _) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);

        var response = await service.RejectAsync(requestId, new RejectDispositionRequestRequest("Missing approval chain."), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DispositionRequest.RejectedStatus, response.Status);
        var state = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(DocumentRetentionState.ActiveState, state.State);
    }

    [Fact]
    public async Task RejectAsync_by_requester_throws()
    {
        await using var dbContext = CreateDbContext();
        var (_, requestId, requesterId) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RejectAsync(requestId, new RejectDispositionRequestRequest("Reason."), requesterId, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_when_already_decided_throws()
    {
        await using var dbContext = CreateDbContext();
        var (_, requestId, _) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);
        await service.RejectAsync(requestId, new RejectDispositionRequestRequest("Reason."), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RejectAsync(requestId, new RejectDispositionRequestRequest("Reason again."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_requires_non_blank_decision_reason()
    {
        await using var dbContext = CreateDbContext();
        var (_, requestId, _) = await SeedPendingDispositionAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RejectAsync(requestId, new RejectDispositionRequestRequest(null), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_rejects_unknown_request()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DispositionRequestNotFoundException>(
            () => service.RejectAsync(Guid.NewGuid(), new RejectDispositionRequestRequest("Reason."), Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Concurrency ----

    [Fact]
    public async Task Concurrent_approve_saves_against_the_same_request_throw_DbUpdateConcurrencyException()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid requestId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            (_, requestId, _) = await SeedPendingDispositionAsync(seedContext);
        }

        await using var firstContext = CreateDbContext(databaseName);
        await using var secondContext = CreateDbContext(databaseName);

        var firstRequest = await firstContext.Set<DispositionRequest>().SingleAsync(r => r.Id == requestId);
        var secondRequest = await secondContext.Set<DispositionRequest>().SingleAsync(r => r.Id == requestId);

        firstRequest.Approve(Guid.NewGuid(), "First decision.", FixedUtcNow.UtcDateTime);
        await firstContext.SaveChangesAsync();

        secondRequest.Reject(Guid.NewGuid(), "Second decision.", FixedUtcNow.UtcDateTime);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private static DispositionRequestService CreateService(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

    private static async Task<(Guid DocumentId, Guid RequestId, Guid RequesterId)> SeedPendingDispositionAsync(HarborOpsDbContext dbContext)
    {
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 100);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var requesterId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var response = await service.CreateAsync(documentId, new CreateDispositionRequest("Retention period elapsed."), requesterId, CancellationToken.None);

        return (documentId, response.Id, requesterId);
    }

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType("Safety Management Certificate", 60, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task SeedRetentionPolicyAsync(HarborOpsDbContext dbContext, int documentTypeId, int retentionPeriodDays)
    {
        await dbContext.Set<RetentionPolicy>().AddAsync(new RetentionPolicy(documentTypeId, retentionPeriodDays));
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> SeedExpiredActiveDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId, DateOnly expiryDate)
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            $"CERT-{Guid.NewGuid():N}",
            expiryDate.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private static async Task<Guid> SeedSupersededDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId, DateTime supersededAtUtc)
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            $"CERT-{Guid.NewGuid():N}",
            new DateOnly(2020, 1, 1),
            new DateOnly(2021, 1, 1),
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        document.Supersede(Guid.NewGuid(), supersededAtUtc);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
