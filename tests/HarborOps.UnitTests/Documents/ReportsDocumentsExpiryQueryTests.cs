using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class ReportsDocumentsExpiryQueryTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);

    [Fact]
    public async Task SearchAsync_without_filters_returns_all_documents_through_paging()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "EARLY", new DateOnly(2026, 1, 1));
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "LATE", new DateOnly(2027, 1, 1));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("EARLY", result.Items[0].Number);
    }

    [Fact]
    public async Task SearchAsync_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();

        var target = await SeedDocumentAsync(dbContext, targetVesselId, documentTypeId, "TARGET", AsOf.AddDays(5));
        await SeedDocumentAsync(dbContext, otherVesselId, documentTypeId, "OTHER", AsOf.AddDays(5));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, VesselId = targetVesselId },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(target.Id, Assert.Single(result.Items).DocumentId);
    }

    [Fact]
    public async Task SearchAsync_filters_by_document_type_id()
    {
        await using var dbContext = CreateDbContext();
        var targetTypeId = await SeedDocumentTypeAsync(dbContext, "Safety Management Certificate");
        var otherTypeId = await SeedDocumentTypeAsync(dbContext, "International Load Line Certificate");
        var vesselId = Guid.NewGuid();

        var target = await SeedDocumentAsync(dbContext, vesselId, targetTypeId, "TARGET", AsOf.AddDays(5));
        await SeedDocumentAsync(dbContext, vesselId, otherTypeId, "OTHER", AsOf.AddDays(5));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, DocumentTypeId = targetTypeId },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(target.Id, Assert.Single(result.Items).DocumentId);
    }

    [Fact]
    public async Task SearchAsync_includes_documents_on_range_boundaries_and_excludes_outside()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        var onFrom = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ON-FROM", from);
        var onTo = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ON-TO", to);
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "BEFORE-FROM", from.AddDays(-1));
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "AFTER-TO", to.AddDays(1));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, From = from, To = to },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        var ids = result.Items.Select(item => item.DocumentId).ToArray();
        Assert.Contains(onFrom.Id, ids);
        Assert.Contains(onTo.Id, ids);
    }

    [Fact]
    public async Task SearchAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var active = await SeedDocumentAsync(
            dbContext, vesselId, documentTypeId, "ACTIVE", AsOf.AddDays(10), status: "Active");
        await SeedDocumentAsync(
            dbContext, vesselId, documentTypeId, "EXPIRED", AsOf.AddDays(10), status: "Expired");

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, Status = "Active" },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(active.Id, Assert.Single(result.Items).DocumentId);
    }

    [Fact]
    public async Task SearchAsync_orders_by_expiry_date_then_document_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var sharedExpiryDate = new DateOnly(2026, 9, 1);

        var second = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "TIE-B", sharedExpiryDate);
        var first = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "TIE-A", sharedExpiryDate);
        var earliest = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "EARLIEST", AsOf.AddDays(1));

        var expectedTieOrder = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(earliest.Id, result.Items[0].DocumentId);
        Assert.Equal(expectedTieOrder[0], result.Items[1].DocumentId);
        Assert.Equal(expectedTieOrder[1], result.Items[2].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_computes_days_until_expiry_for_future_and_already_expired_documents()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var future = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "FUTURE", AsOf.AddDays(5));
        var expired = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "EXPIRED", AsOf.AddDays(-3));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf },
            CancellationToken.None);

        var futureItem = result.Items.Single(item => item.DocumentId == future.Id);
        var expiredItem = result.Items.Single(item => item.DocumentId == expired.Id);

        Assert.Equal(5, futureItem.DaysUntilExpiry);
        Assert.False(futureItem.IsExpired);
        Assert.Equal(-3, expiredItem.DaysUntilExpiry);
        Assert.True(expiredItem.IsExpired);
    }

    [Fact]
    public async Task SearchAsync_treats_document_expiring_exactly_on_as_of_date_as_not_expired()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();

        var onAsOf = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ON-AS-OF", AsOf);

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf },
            CancellationToken.None);

        var item = Assert.Single(result.Items, item => item.DocumentId == onAsOf.Id);
        Assert.Equal(0, item.DaysUntilExpiry);
        Assert.False(item.IsExpired);
    }

    [Fact]
    public async Task SearchAsync_returns_second_page_and_total_count_across_all_pages()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();

        for (var index = 0; index < 30; index++)
        {
            await SeedDocumentAsync(dbContext, vesselId, documentTypeId, $"PAGE-{index:D2}", AsOf.AddDays(index));
        }

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var secondPage = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, PageNumber = 2 },
            CancellationToken.None);

        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(25, secondPage.PageSize);
        Assert.Equal(30, secondPage.TotalCount);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(AsOf.AddDays(25), secondPage.Items[0].ExpiryDate);
        Assert.Equal(AsOf.AddDays(29), secondPage.Items[^1].ExpiryDate);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_result_when_nothing_matches()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "OUT-OF-RANGE", new DateOnly(2030, 1, 1));

        var query = new ReportsDocumentsExpiryQuery(dbContext);

        var result = await query.SearchAsync(
            new ReportsDocumentsExpiryRequest { AsOf = AsOf, VesselId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static async Task<int> SeedDocumentTypeAsync(
        HarborOpsDbContext dbContext, string name = "Safety Management Certificate")
    {
        var documentType = new DocumentType(name, 60, "Flag State Authority", requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task<Document> SeedDocumentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        int documentTypeId,
        string number,
        DateOnly expiryDate,
        string status = "Active")
    {
        var document = new Document(
            vesselId,
            documentTypeId,
            number,
            expiryDate.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            status);

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
