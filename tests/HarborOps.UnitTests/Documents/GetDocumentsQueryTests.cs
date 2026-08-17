using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentsQueryTests
{
    [Fact]
    public async Task GetDocuments_returns_empty_result_when_no_documents_exist()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(new GetDocumentsRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetDocuments_returns_all_documents_ordered_by_number_then_id_when_no_filters_given()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var first = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "AAA-1");
        var second = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "AAA-1");
        var third = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "ZZZ-9");
        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(new GetDocumentsRequest(), CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(3, response.Items.Count);
        var expectedTieOrder = new[] { first.Id, second.Id }.Order().ToArray();
        Assert.Equal(expectedTieOrder[0], response.Items[0].DocumentId);
        Assert.Equal(expectedTieOrder[1], response.Items[1].DocumentId);
        Assert.Equal(third.Id, response.Items[2].DocumentId);
    }

    [Fact]
    public async Task GetDocuments_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var targetDocument = await SeedDocumentAsync(dbContext, targetVesselId, documentTypeId, "TARGET-1");
        await SeedDocumentAsync(dbContext, otherVesselId, documentTypeId, "OTHER-1");

        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(
            new GetDocumentsRequest { VesselId = targetVesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(targetDocument.Id, item.DocumentId);
        Assert.Equal(targetVesselId, item.VesselId);
    }

    [Fact]
    public async Task GetDocuments_filters_by_search_case_insensitively()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        var matching = await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "BRZ-2024-0091");
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "CERT-2024-0002");
        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(
            new GetDocumentsRequest { Search = "brz" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(matching.Id, item.DocumentId);
    }

    [Fact]
    public async Task GetDocuments_returns_empty_result_when_search_matches_nothing()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "CERT-2024-0002");
        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(
            new GetDocumentsRequest { Search = "nonexistent-xyz" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetDocuments_respects_page_size_while_total_count_reflects_full_filtered_set()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        for (var index = 0; index < 5; index++)
        {
            await SeedDocumentAsync(dbContext, vesselId, documentTypeId, $"CERT-{index:D4}");
        }

        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(
            new GetDocumentsRequest { PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.PageSize);
    }

    [Fact]
    public async Task GetDocuments_uses_default_page_number_and_page_size_when_not_specified()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var vesselId = Guid.NewGuid();
        await SeedDocumentAsync(dbContext, vesselId, documentTypeId, "CERT-0001");
        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(new GetDocumentsRequest(), CancellationToken.None);

        Assert.Equal(GetDocumentsValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(GetDocumentsValidator.DefaultPageSize, response.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetDocuments_rejects_page_size_below_minimum(int pageSize)
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentsValidationException>(
            () => query.GetDocumentsAsync(
                new GetDocumentsRequest { PageSize = pageSize },
                CancellationToken.None));

        Assert.Contains(GetDocumentsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetDocuments_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentsValidationException>(
            () => query.GetDocumentsAsync(
                new GetDocumentsRequest { PageSize = GetDocumentsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetDocumentsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetDocuments_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentsValidationException>(
            () => query.GetDocumentsAsync(
                new GetDocumentsRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(GetDocumentsValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task GetDocuments_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentsValidationException>(
            () => query.GetDocumentsAsync(
                new GetDocumentsRequest { VesselId = Guid.Empty },
                CancellationToken.None));

        Assert.Contains(GetDocumentsValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task GetDocuments_maps_response_fields_from_document_and_document_type()
    {
        await using var dbContext = CreateDbContext();
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        var vesselId = Guid.NewGuid();
        var document = new Document(
            vesselId,
            documentType.Id,
            "CERT-1001",
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1),
            "Flag State Authority",
            "Active");
        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        var query = new GetDocumentsQuery(dbContext);

        var response = await query.GetDocumentsAsync(new GetDocumentsRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(document.Id, item.DocumentId);
        Assert.Equal(document.VesselId, item.VesselId);
        Assert.Equal(document.Number, item.Number);
        Assert.Equal(documentType.Name, item.DocumentTypeName);
        Assert.Equal(document.Status, item.Status);
        Assert.Equal(document.ExpiryDate, item.ExpiryDate);
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
        string number)
    {
        var document = new Document(
            vesselId,
            documentTypeId,
            number,
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1),
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
