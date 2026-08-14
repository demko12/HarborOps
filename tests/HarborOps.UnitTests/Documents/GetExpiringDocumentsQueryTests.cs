using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetExpiringDocumentsQueryTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    [Fact]
    public async Task GetExpiringDocuments_without_date_boundaries_returns_all_documents_through_paging()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "EARLY", new DateOnly(2020, 1, 1));
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "LATE", new DateOnly(2030, 1, 1));

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { PageNumber = 1, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Single(response.Items);
        Assert.Equal("EARLY", response.Items[0].Number);
    }

    [Fact]
    public async Task GetExpiringDocuments_with_from_only_applies_lower_bound()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "BEFORE", from.AddDays(-1));
        var included = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ON-FROM", from);

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from },
            CancellationToken.None);

        Assert.Equal(included.Id, Assert.Single(response.Items).DocumentId);
    }

    [Fact]
    public async Task GetExpiringDocuments_with_to_only_applies_upper_bound()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var to = new DateOnly(2026, 8, 31);
        var included = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ON-TO", to);
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "AFTER", to.AddDays(1));

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { To = to },
            CancellationToken.None);

        Assert.Equal(included.Id, Assert.Single(response.Items).DocumentId);
    }

    [Fact]
    public async Task GetExpiringDocuments_includes_documents_on_range_boundaries_and_excludes_outside()
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

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = to },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        var ids = response.Items.Select(item => item.DocumentId).ToArray();
        Assert.Contains(onFrom.Id, ids);
        Assert.Contains(onTo.Id, ids);
    }

    [Fact]
    public async Task GetExpiringDocuments_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        var targetDocument = await SeedDocumentAsync(
            dbContext, targetVesselId, documentTypeId, "TARGET-1", from.AddDays(5));
        await SeedDocumentAsync(dbContext, otherVesselId, documentTypeId, "OTHER-1", from.AddDays(6));

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = to, VesselId = targetVesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(targetDocument.Id, item.DocumentId);
        Assert.Equal(targetVesselId, item.VesselId);
    }

    [Fact]
    public async Task GetExpiringDocuments_orders_by_expiry_date_then_document_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);
        var sharedExpiryDate = new DateOnly(2026, 8, 15);

        var second = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "TIE-B", sharedExpiryDate);
        var first = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "TIE-A", sharedExpiryDate);

        var expectedTieOrder = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = to },
            CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(expectedTieOrder[0], response.Items[0].DocumentId);
        Assert.Equal(expectedTieOrder[1], response.Items[1].DocumentId);
    }

    [Fact]
    public async Task GetExpiringDocuments_computes_days_until_expiry_for_future_and_already_expired_documents()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = Today.AddDays(-10);
        var to = Today.AddDays(10);

        var future = await SeedDocumentAsync(
            dbContext, vesselId, documentTypeId, "FUTURE-1", Today.AddDays(5));
        var expired = await SeedDocumentAsync(
            dbContext, vesselId, documentTypeId, "EXPIRED-1", Today.AddDays(-3));

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = to },
            CancellationToken.None);

        var futureItem = response.Items.Single(item => item.DocumentId == future.Id);
        var expiredItem = response.Items.Single(item => item.DocumentId == expired.Id);

        Assert.Equal(5, futureItem.DaysUntilExpiry);
        Assert.Equal(-3, expiredItem.DaysUntilExpiry);
    }

    [Fact]
    public async Task GetExpiringDocuments_returns_empty_result_when_nothing_in_range()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(
            dbContext, vesselId, documentTypeId, "OUT-OF-RANGE", new DateOnly(2030, 1, 1));

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31) },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetExpiringDocuments_returns_second_page_and_total_count_across_all_pages()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 1, 1);

        for (var index = 0; index < 30; index++)
        {
            await SeedDocumentAsync(
                dbContext, vesselId, documentTypeId, $"PAGE-{index:D2}", from.AddDays(index));
        }

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var secondPage = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = from.AddDays(29), PageNumber = 2 },
            CancellationToken.None);

        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(GetExpiringDocumentsValidator.DefaultPageSize, secondPage.PageSize);
        Assert.Equal(30, secondPage.TotalCount);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(from.AddDays(25), secondPage.Items[0].ExpiryDate);
        Assert.Equal(from.AddDays(29), secondPage.Items[^1].ExpiryDate);
    }

    [Fact]
    public async Task GetExpiringDocuments_rejects_to_before_from()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var exception = await Assert.ThrowsAsync<GetExpiringDocumentsValidationException>(
            () => query.GetExpiringDocumentsAsync(
                new GetExpiringDocumentsRequest
                {
                    From = new DateOnly(2026, 6, 1),
                    To = new DateOnly(2026, 5, 1),
                },
                CancellationToken.None));

        Assert.Contains(GetExpiringDocumentsValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task GetExpiringDocuments_rejects_range_above_366_days()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var from = new DateOnly(2026, 1, 1);

        var exception = await Assert.ThrowsAsync<GetExpiringDocumentsValidationException>(
            () => query.GetExpiringDocumentsAsync(
                new GetExpiringDocumentsRequest { From = from, To = from.AddDays(367) },
                CancellationToken.None));

        Assert.Contains(GetExpiringDocumentsValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task GetExpiringDocuments_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var exception = await Assert.ThrowsAsync<GetExpiringDocumentsValidationException>(
            () => query.GetExpiringDocumentsAsync(
                new GetExpiringDocumentsRequest
                {
                    From = new DateOnly(2026, 1, 1),
                    To = new DateOnly(2026, 1, 31),
                    PageSize = GetExpiringDocumentsValidator.MaxPageSize + 1,
                },
                CancellationToken.None));

        Assert.Contains(GetExpiringDocumentsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetExpiringDocuments_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var exception = await Assert.ThrowsAsync<GetExpiringDocumentsValidationException>(
            () => query.GetExpiringDocumentsAsync(
                new GetExpiringDocumentsRequest
                {
                    From = new DateOnly(2026, 1, 1),
                    To = new DateOnly(2026, 1, 31),
                    PageNumber = 0,
                },
                CancellationToken.None));

        Assert.Contains(GetExpiringDocumentsValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task GetExpiringDocuments_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var exception = await Assert.ThrowsAsync<GetExpiringDocumentsValidationException>(
            () => query.GetExpiringDocumentsAsync(
                new GetExpiringDocumentsRequest
                {
                    From = new DateOnly(2026, 1, 1),
                    To = new DateOnly(2026, 1, 31),
                    VesselId = Guid.Empty,
                },
                CancellationToken.None));

        Assert.Contains(GetExpiringDocumentsValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task GetExpiringDocuments_accepts_range_of_exactly_366_days()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(366);

        var seeded = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "MAX-RANGE", to);

        var query = new GetExpiringDocumentsQuery(dbContext, new FakeDateTimeProvider(FixedUtcNow));

        var response = await query.GetExpiringDocumentsAsync(
            new GetExpiringDocumentsRequest { From = from, To = to },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(seeded.Id, Assert.Single(response.Items).DocumentId);
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

    private static async Task<Document> SeedDocumentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        int documentTypeId,
        string number,
        DateOnly expiryDate)
    {
        var document = new Document(
            vesselId,
            documentTypeId,
            number,
            expiryDate.AddYears(-1),
            expiryDate,
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
