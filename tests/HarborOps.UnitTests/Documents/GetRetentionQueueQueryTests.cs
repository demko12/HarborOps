using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetRetentionQueueQueryTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetRetentionQueueAsync_treats_document_with_no_retention_row_as_active()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(new GetRetentionQueueRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(DocumentRetentionState.ActiveState, item.RetentionState);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_excludes_document_with_no_matching_retention_policy()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(new GetRetentionQueueRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_excludes_active_document_not_yet_expired()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2099, 1, 1));
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(new GetRetentionQueueRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_filters_by_state()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        var activeDocumentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1), "CERT-A");
        var archivedDocumentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1), "CERT-B");
        await dbContext.Set<DocumentRetentionState>().AddAsync(ArchivedState(archivedDocumentId));
        await dbContext.SaveChangesAsync();
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { State = DocumentRetentionState.ArchivedState },
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(archivedDocumentId, item.DocumentId);
        Assert.NotEqual(activeDocumentId, item.DocumentId);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_filters_by_due_before()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        // Due 2026-01-31 (well in the past relative to FixedUtcNow).
        var dueEarlyId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1), "CERT-EARLY");
        // Due 2026-08-01, still before FixedUtcNow, but after the dueBefore cutoff used below.
        var dueLateId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 7, 2), "CERT-LATE");
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { DueBefore = new DateOnly(2026, 3, 1) },
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(dueEarlyId, item.DocumentId);
        Assert.NotEqual(dueLateId, item.DocumentId);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_pages_the_filtered_result_and_reports_total_count()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1), "CERT-1");
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 2), "CERT-2");
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 3), "CERT-3");
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Single(response.Items);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
    }

    // Guards against a regression to materialize-then-filter: the dataset mixes matching and
    // non-matching rows across every exclusion path (wrong state, not yet due, not yet
    // expired, no matching policy) and exceeds one page size, so TotalCount/paging would be
    // visibly wrong (inflated count, wrong items on page 2) if the State filter or the
    // eligibility WHERE were applied after Count/Skip/Take instead of before.
    [Fact]
    public async Task GetRetentionQueueAsync_computes_total_count_and_paging_correctly_over_a_mixed_larger_dataset()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);

        // Three eligible, due, implicitly-Active documents - these should be the only ones
        // counted and paged.
        var activeDueId1 = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1), "CERT-ACTIVE-1");
        var activeDueId2 = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 2), "CERT-ACTIVE-2");
        var activeDueId3 = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 3), "CERT-ACTIVE-3");

        // Two eligible, due, but Archived - excluded by the State=Active filter.
        var archivedDueId1 = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 4), "CERT-ARCHIVED-1");
        var archivedDueId2 = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 5), "CERT-ARCHIVED-2");
        await dbContext.Set<DocumentRetentionState>().AddRangeAsync(ArchivedState(archivedDueId1), ArchivedState(archivedDueId2));

        // Eligible type, but not yet expired - excluded entirely (no lifecycle trigger).
        await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2099, 1, 1), "CERT-NOT-EXPIRED");

        // Different type with no retention policy - excluded entirely (inner join).
        var otherTypeId = await SeedDocumentTypeAsync(dbContext, "Other Certificate");
        await SeedExpiredActiveDocumentAsync(dbContext, otherTypeId, new DateOnly(2026, 1, 1), "CERT-NO-POLICY");

        await dbContext.SaveChangesAsync();
        var query = CreateQuery(dbContext);

        var firstPage = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { State = DocumentRetentionState.ActiveState, PageNumber = 1, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.All(firstPage.Items, item => Assert.Contains(item.DocumentId, new[] { activeDueId1, activeDueId2, activeDueId3 }));

        var secondPage = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { State = DocumentRetentionState.ActiveState, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, secondPage.TotalCount);
        var remaining = Assert.Single(secondPage.Items);
        Assert.Equal(activeDueId3, remaining.DocumentId);

        // Sanity check the archived documents are reachable under their own filter, proving
        // they were excluded above by the filter and not by some other accident.
        var archivedPage = await query.GetRetentionQueueAsync(
            new GetRetentionQueueRequest { State = DocumentRetentionState.ArchivedState },
            CancellationToken.None);

        Assert.Equal(2, archivedPage.TotalCount);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_includes_active_holds_and_pending_disposition()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        await SeedRetentionPolicyAsync(dbContext, documentTypeId, 30);
        var documentId = await SeedExpiredActiveDocumentAsync(dbContext, documentTypeId, new DateOnly(2026, 1, 1));
        await dbContext.Set<LegalHold>().AddAsync(LegalHold.Place(documentId, "Litigation.", null, Guid.NewGuid(), FixedUtcNow.UtcDateTime));
        await dbContext.SaveChangesAsync();
        var query = CreateQuery(dbContext);

        var response = await query.GetRetentionQueueAsync(new GetRetentionQueueRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        var hold = Assert.Single(item.ActiveHolds);
        Assert.Equal("Litigation.", hold.Reason);
        Assert.Null(item.PendingDisposition);
    }

    [Fact]
    public async Task GetRetentionQueueAsync_rejects_unknown_state_filter()
    {
        await using var dbContext = CreateDbContext();
        var query = CreateQuery(dbContext);

        await Assert.ThrowsAsync<GetRetentionQueueValidationException>(
            () => query.GetRetentionQueueAsync(new GetRetentionQueueRequest { State = "NotARealState" }, CancellationToken.None));
    }

    private static DocumentRetentionState ArchivedState(Guid documentId)
    {
        var state = new DocumentRetentionState(documentId);
        state.Archive(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        return state;
    }

    private static GetRetentionQueueQuery CreateQuery(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext, string name = "Safety Management Certificate")
    {
        var documentType = new DocumentType(name, 60, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task SeedRetentionPolicyAsync(HarborOpsDbContext dbContext, int documentTypeId, int retentionPeriodDays)
    {
        await dbContext.Set<RetentionPolicy>().AddAsync(new RetentionPolicy(documentTypeId, retentionPeriodDays));
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> SeedExpiredActiveDocumentAsync(
        HarborOpsDbContext dbContext,
        int documentTypeId,
        DateOnly expiryDate,
        string number = "CERT-9001")
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            number,
            expiryDate.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
