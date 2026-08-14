using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class GetDocumentTypesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetDocumentTypesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_ok_with_empty_items_when_no_document_types_exist()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri("/api/documents/types", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentTypesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(GetDocumentTypesValidator.DefaultPageSize, result.PageSize);
    }

    [Fact]
    public async Task Get_returns_seeded_document_types_ordered_by_name_then_id()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var typeC = await SeedDocumentTypeAsync("Certificate of Registry");
        var typeS = await SeedDocumentTypeAsync("Safety Management Certificate");
        var typeL = await SeedDocumentTypeAsync("Load Line Certificate");

        using var response = await client.GetAsync(
            new Uri("/api/documents/types", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentTypesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(typeC.Id, result.Items[0].DocumentTypeId);
        Assert.Equal(typeL.Id, result.Items[1].DocumentTypeId);
        Assert.Equal(typeS.Id, result.Items[2].DocumentTypeId);
    }

    [Fact]
    public async Task Get_filters_by_search_query_parameter()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var matching = await SeedDocumentTypeAsync("Certificate of Registry");
        await SeedDocumentTypeAsync("Safety Management Plan");

        using var response = await client.GetAsync(
            new Uri("/api/documents/types?search=cert", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentTypesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(matching.Id, item.DocumentTypeId);
    }

    [Fact]
    public async Task Get_respects_page_size_query_parameter_while_total_count_reflects_full_filtered_set()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        for (var index = 0; index < 5; index++)
        {
            await SeedDocumentTypeAsync($"Certificate Type {index}");
        }

        using var response = await client.GetAsync(
            new Uri("/api/documents/types?pageSize=2", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentTypesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Get_returns_validation_problem_for_page_size_below_minimum(int pageSize)
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri($"/api/documents/types?pageSize={pageSize}", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDocumentTypesValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(
            new Uri($"/api/documents/types?pageSize={GetDocumentTypesValidator.MaxPageSize + 1}", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDocumentTypesValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/documents/types", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_response_uses_camel_case_property_names()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        await SeedDocumentTypeAsync(
            "Certificate of Registry",
            mandatoryValidityPeriod: 90,
            issuingAuthority: "Flag State Authority",
            requiresSurvey: true);

        using var response = await client.GetAsync(
            new Uri("/api/documents/types", UriKind.Relative),
            CancellationToken.None);
        var content = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"documentTypeId\"", content, StringComparison.Ordinal);
        Assert.Contains("\"name\"", content, StringComparison.Ordinal);
        Assert.Contains("\"mandatoryValidityPeriodDays\"", content, StringComparison.Ordinal);
        Assert.Contains("\"issuingAuthority\"", content, StringComparison.Ordinal);
        Assert.Contains("\"requiresSurvey\"", content, StringComparison.Ordinal);
        Assert.Contains("\"pageSize\"", content, StringComparison.Ordinal);
        Assert.Contains("\"totalCount\"", content, StringComparison.Ordinal);
    }

    private async Task<DocumentType> SeedDocumentTypeAsync(
        string name,
        int mandatoryValidityPeriod = 60,
        string issuingAuthority = "Flag State Authority",
        bool requiresSurvey = true)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var documentType = new DocumentType(name, mandatoryValidityPeriod, issuingAuthority, requiresSurvey);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType;
    }
}
