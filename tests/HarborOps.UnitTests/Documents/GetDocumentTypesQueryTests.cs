using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentTypesQueryTests
{
    [Fact]
    public async Task GetDocumentTypes_returns_empty_result_when_no_document_types_exist()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(new GetDocumentTypesRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(GetDocumentTypesValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task GetDocumentTypes_returns_all_types_ordered_by_name_then_id_when_search_is_not_specified()
    {
        await using var dbContext = CreateDbContext();
        var first = await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        var second = await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        var third = await SeedDocumentTypeAsync(dbContext, "Safety Management Certificate");
        await SeedDocumentTypeAsync(dbContext, "Load Line Certificate");
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(new GetDocumentTypesRequest(), CancellationToken.None);

        Assert.Equal(4, response.TotalCount);
        Assert.Equal(4, response.Items.Count);
        Assert.Equal(first.Id, response.Items[0].DocumentTypeId);
        Assert.Equal(second.Id, response.Items[1].DocumentTypeId);
        Assert.Equal("Load Line Certificate", response.Items[2].Name);
        Assert.Equal(third.Id, response.Items[3].DocumentTypeId);
    }

    [Fact]
    public async Task GetDocumentTypes_filters_by_search_case_insensitively()
    {
        await using var dbContext = CreateDbContext();
        var matching = await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        await SeedDocumentTypeAsync(dbContext, "Safety Management Plan");
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(
            new GetDocumentTypesRequest { Search = "CeRt" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(matching.Id, item.DocumentTypeId);
    }

    [Fact]
    public async Task GetDocumentTypes_returns_empty_result_when_search_matches_nothing()
    {
        await using var dbContext = CreateDbContext();
        await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(
            new GetDocumentTypesRequest { Search = "nonexistent-xyz" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetDocumentTypes_respects_page_size_while_total_count_reflects_full_filtered_set()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 5; index++)
        {
            await SeedDocumentTypeAsync(dbContext, $"Certificate Type {index}");
        }

        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(
            new GetDocumentTypesRequest { PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.PageSize);
    }

    [Fact]
    public async Task GetDocumentTypes_uses_default_page_size_when_not_specified()
    {
        await using var dbContext = CreateDbContext();
        await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        await SeedDocumentTypeAsync(dbContext, "Load Line Certificate");
        await SeedDocumentTypeAsync(dbContext, "Safety Management Certificate");
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(new GetDocumentTypesRequest(), CancellationToken.None);

        Assert.Equal(GetDocumentTypesValidator.DefaultPageSize, response.PageSize);
        Assert.Equal(3, response.Items.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetDocumentTypes_rejects_page_size_below_minimum(int pageSize)
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentTypesQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentTypesValidationException>(
            () => query.GetDocumentTypesAsync(
                new GetDocumentTypesRequest { PageSize = pageSize },
                CancellationToken.None));

        Assert.Contains(GetDocumentTypesValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetDocumentTypes_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentTypesQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetDocumentTypesValidationException>(
            () => query.GetDocumentTypesAsync(
                new GetDocumentTypesRequest { PageSize = GetDocumentTypesValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetDocumentTypesValidator.PageSizeField, exception.Errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(GetDocumentTypesValidator.MaxPageSize)]
    public async Task GetDocumentTypes_accepts_boundary_page_sizes(int pageSize)
    {
        await using var dbContext = CreateDbContext();
        await SeedDocumentTypeAsync(dbContext, "Certificate of Registry");
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(
            new GetDocumentTypesRequest { PageSize = pageSize },
            CancellationToken.None);

        Assert.Equal(pageSize, response.PageSize);
    }

    [Fact]
    public async Task GetDocumentTypes_maps_response_fields_from_document_type()
    {
        await using var dbContext = CreateDbContext();
        var documentType = await SeedDocumentTypeAsync(
            dbContext,
            "Certificate of Registry",
            mandatoryValidityPeriod: 90,
            issuingAuthority: "Flag State Authority",
            requiresSurvey: true);
        var query = new GetDocumentTypesQuery(dbContext);

        var response = await query.GetDocumentTypesAsync(new GetDocumentTypesRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(documentType.Id, item.DocumentTypeId);
        Assert.Equal(documentType.Name, item.Name);
        Assert.Equal(documentType.MandatoryValidityPeriod, item.MandatoryValidityPeriodDays);
        Assert.Equal(documentType.IssuingAuthority, item.IssuingAuthority);
        Assert.Equal(documentType.RequiresSurvey, item.RequiresSurvey);
    }

    private static async Task<DocumentType> SeedDocumentTypeAsync(
        HarborOpsDbContext dbContext,
        string name,
        int mandatoryValidityPeriod = 60,
        string issuingAuthority = "Flag State Authority",
        bool requiresSurvey = true)
    {
        var documentType = new DocumentType(name, mandatoryValidityPeriod, issuingAuthority, requiresSurvey);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
