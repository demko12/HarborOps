using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class DocumentRetentionServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ArchiveAsync_creates_state_implicitly_and_archives_it()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var actorId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var response = await service.ArchiveAsync(documentId, actorId, CancellationToken.None);

        Assert.Equal(DocumentRetentionState.ArchivedState, response.State);
        var persisted = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(DocumentRetentionState.ArchivedState, persisted.State);
        Assert.Equal(actorId, persisted.ArchivedByUserId);
    }

    [Fact]
    public async Task ArchiveAsync_rejects_unknown_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.ArchiveAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ArchiveAsync_when_already_archived_propagates_domain_guard()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);
        await service.ArchiveAsync(documentId, Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ArchiveAsync(documentId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_moves_archived_document_back_to_active()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);
        await service.ArchiveAsync(documentId, Guid.NewGuid(), CancellationToken.None);
        var actorId = Guid.NewGuid();

        var response = await service.RestoreAsync(documentId, actorId, CancellationToken.None);

        Assert.Equal(DocumentRetentionState.ActiveState, response.State);
        var persisted = await dbContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        Assert.Equal(actorId, persisted.RestoredByUserId);
    }

    [Fact]
    public async Task RestoreAsync_when_no_state_row_exists_propagates_domain_guard()
    {
        // No retention row means implicitly Active; Restore is Archived-only, so the
        // implicit-create path must still surface the same InvalidOperationException.
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreAsync(documentId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_rejects_unknown_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.RestoreAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Concurrency ----

    [Fact]
    public async Task Concurrent_archive_saves_against_the_same_state_throw_DbUpdateConcurrencyException()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid documentId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            documentId = await SeedActiveDocumentAsync(seedContext);
            await seedContext.Set<DocumentRetentionState>().AddAsync(new DocumentRetentionState(documentId));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = CreateDbContext(databaseName);
        await using var secondContext = CreateDbContext(databaseName);

        var firstState = await firstContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);
        var secondState = await secondContext.Set<DocumentRetentionState>().SingleAsync(s => s.DocumentId == documentId);

        firstState.Archive(Guid.NewGuid(), FixedUtcNow.UtcDateTime);
        await firstContext.SaveChangesAsync();

        secondState.Archive(Guid.NewGuid(), FixedUtcNow.UtcDateTime);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private static DocumentRetentionService CreateService(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

    private static async Task<Guid> SeedActiveDocumentAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType("Safety Management Certificate", 60, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        var document = new Document(
            Guid.NewGuid(),
            documentType.Id,
            "CERT-9001",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            Document.ActiveStatus);

        await dbContext.Set<Document>().AddAsync(document);
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
