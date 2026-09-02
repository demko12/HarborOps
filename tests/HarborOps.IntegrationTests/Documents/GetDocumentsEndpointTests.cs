using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class GetDocumentsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetDocumentsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_ok_with_empty_items_when_no_documents_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync(
            new Uri("/api/documents", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_returns_seeded_documents_with_document_type_name()
    {
        // Manage, not Read: this test asserts response shape, not visibility - a
        // freshly-registered document defaults to Draft, invisible to a read-only client
        // (see DocumentVisibilityEndpointTests for the dedicated visibility coverage).
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var vesselId = Guid.NewGuid();
        var expiryDate = new DateOnly(2026, 1, 1);
        var seeded = await SeedDocumentAsync(vesselId, "CERT-1001", expiryDate);

        using var response = await client.GetAsync(
            new Uri("/api/documents", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);

        Assert.Equal(seeded.Id, item.DocumentId);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("CERT-1001", item.Number);
        Assert.Equal("Safety Management Certificate", item.DocumentTypeName);
        Assert.Equal("Active", item.Status);
        Assert.Equal(expiryDate, item.ExpiryDate);
    }

    [Fact]
    public async Task Get_filters_by_vessel_id_query_parameter()
    {
        // Manage, not Read: this test asserts filter mechanics, not visibility (see comment
        // on Get_returns_seeded_documents_with_document_type_name above).
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var targetVesselId = Guid.NewGuid();
        var targetDocument = await SeedDocumentAsync(targetVesselId, "TARGET-1", new DateOnly(2026, 1, 1));
        await SeedDocumentAsync(Guid.NewGuid(), "OTHER-1", new DateOnly(2026, 1, 1));

        using var response = await client.GetAsync(
            new Uri($"/api/documents?vesselId={targetVesselId}", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(targetDocument.Id, item.DocumentId);
        Assert.Equal(targetVesselId, item.VesselId);
    }

    [Fact]
    public async Task Get_filters_by_search_query_parameter()
    {
        // Manage, not Read: this test asserts filter mechanics, not visibility (see comment
        // on Get_returns_seeded_documents_with_document_type_name above).
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var vesselId = Guid.NewGuid();
        var matching = await SeedDocumentAsync(vesselId, "BRZ-2024-0091", new DateOnly(2026, 1, 1));
        await SeedDocumentAsync(vesselId, "CERT-2024-0002", new DateOnly(2026, 1, 1));

        using var response = await client.GetAsync(
            new Uri("/api/documents?search=brz", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(matching.Id, item.DocumentId);
    }

    [Fact]
    public async Task Get_respects_page_size_and_page_number_while_total_count_reflects_full_filtered_set()
    {
        // Manage, not Read: this test asserts pagination mechanics, not visibility (see
        // comment on Get_returns_seeded_documents_with_document_type_name above).
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Manage);
        var vesselId = Guid.NewGuid();
        for (var index = 0; index < 5; index++)
        {
            await SeedDocumentAsync(vesselId, $"CERT-{index:D4}", new DateOnly(2026, 1, 1));
        }

        using var response = await client.GetAsync(
            new Uri("/api/documents?pageNumber=2&pageSize=2", UriKind.Relative),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetDocumentsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_page_size_above_maximum()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync(
            new Uri($"/api/documents?pageSize={GetDocumentsValidator.MaxPageSize + 1}", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDocumentsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_empty_vessel_id()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Documents.Read);

        using var response = await client.GetAsync(
            new Uri(
                "/api/documents?vesselId=00000000-0000-0000-0000-000000000000",
                UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetDocumentsValidator.VesselIdField, problem.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/documents", UriKind.Relative),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Document> SeedDocumentAsync(Guid vesselId, string number, DateOnly expiryDate)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);
        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        var document = new Document(
            vesselId,
            documentType.Id,
            number,
            expiryDate.AddYears(-1),
            expiryDate,
            "Flag State Authority",
            "Active");
        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }
}
