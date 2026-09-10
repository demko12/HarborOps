using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class LegalHoldServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlaceAsync_creates_active_hold()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var actorId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var response = await service.PlaceAsync(documentId, new PlaceLegalHoldRequest("Litigation pending.", "CASE-1"), actorId, CancellationToken.None);

        var persisted = await dbContext.Set<LegalHold>().SingleAsync(h => h.Id == response.Id);
        Assert.Equal(documentId, persisted.DocumentId);
        Assert.True(persisted.IsActive);
        Assert.Equal(actorId, persisted.PlacedByUserId);
    }

    [Fact]
    public async Task PlaceAsync_rejects_unknown_document()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => service.PlaceAsync(Guid.NewGuid(), new PlaceLegalHoldRequest("Reason.", null), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task PlaceAsync_propagates_domain_guard_when_reason_blank()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PlaceAsync(documentId, new PlaceLegalHoldRequest(null, null), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseAsync_deactivates_hold()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);
        var placed = await service.PlaceAsync(documentId, new PlaceLegalHoldRequest("Litigation pending.", null), Guid.NewGuid(), CancellationToken.None);
        var actorId = Guid.NewGuid();

        var response = await service.ReleaseAsync(documentId, placed.Id, new ReleaseLegalHoldRequest("Resolved."), actorId, CancellationToken.None);

        Assert.False(response.IsActive);
        var persisted = await dbContext.Set<LegalHold>().SingleAsync(h => h.Id == placed.Id);
        Assert.False(persisted.IsActive);
        Assert.Equal(actorId, persisted.ReleasedByUserId);
    }

    [Fact]
    public async Task ReleaseAsync_rejects_unknown_hold()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<LegalHoldNotFoundException>(
            () => service.ReleaseAsync(documentId, Guid.NewGuid(), new ReleaseLegalHoldRequest("Resolved."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseAsync_rejects_hold_belonging_to_a_different_document()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var otherDocumentId = await SeedActiveDocumentAsync(dbContext, "CERT-9002");
        var service = CreateService(dbContext);
        var placed = await service.PlaceAsync(documentId, new PlaceLegalHoldRequest("Litigation pending.", null), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<LegalHoldNotFoundException>(
            () => service.ReleaseAsync(otherDocumentId, placed.Id, new ReleaseLegalHoldRequest("Resolved."), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseAsync_when_already_released_propagates_domain_guard()
    {
        await using var dbContext = CreateDbContext();
        var documentId = await SeedActiveDocumentAsync(dbContext);
        var service = CreateService(dbContext);
        var placed = await service.PlaceAsync(documentId, new PlaceLegalHoldRequest("Litigation pending.", null), Guid.NewGuid(), CancellationToken.None);
        await service.ReleaseAsync(documentId, placed.Id, new ReleaseLegalHoldRequest("Resolved."), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReleaseAsync(documentId, placed.Id, new ReleaseLegalHoldRequest("Resolved again."), Guid.NewGuid(), CancellationToken.None));
    }

    private static LegalHoldService CreateService(DbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedUtcNow));

    private static async Task<Guid> SeedActiveDocumentAsync(HarborOpsDbContext dbContext, string number = "CERT-9001")
    {
        var documentType = new DocumentType("Safety Management Certificate", 60, "Flag State Authority", requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        var document = new Document(
            Guid.NewGuid(),
            documentType.Id,
            number,
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
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
