using System.Net;
using System.Net.Http.Headers;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class DownloadDocumentFileEndpointTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public DownloadDocumentFileEndpointTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_200_with_headers_and_original_content()
    {
        using var manageClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        var uploadedBytes = new byte[] { 10, 20, 30, 40, 50 };
        using (var uploadContent = CreateFileContent(uploadedBytes, "certificate.pdf", "application/pdf"))
        using (var uploadResponse = await manageClient.PostAsync(
            $"/api/documents/{documentId}/file", uploadContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        }

        // Manage, not Read: this test asserts content/header correctness on download, not
        // review-status visibility - the document defaults to Draft after upload, invisible
        // to a read-only client (see DocumentVisibilityEndpointTests for that coverage).
        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var response = await readClient.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);
        var downloadedBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(uploadedBytes, downloadedBytes);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var contentDisposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Equal("attachment", contentDisposition.DispositionType);
        Assert.Equal("certificate.pdf", contentDisposition.FileName?.Trim('"'));

        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniffValues));
        Assert.Contains("nosniff", nosniffValues);
    }

    [Fact]
    public async Task Get_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await client.GetAsync(
            $"/api/documents/{Guid.NewGuid()}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_when_document_has_no_file_returns_404()
    {
        // Manage, not Read: this test asserts the no-file 404 case, not visibility - a
        // freshly-registered document defaults to Draft, invisible to a read-only client
        // (see DocumentVisibilityEndpointTests for the dedicated visibility coverage).
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_read_only_permission_returns_200_for_approved_and_active_document()
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await DocumentReviewWorkflowTestSupport.CreateApprovedDocumentAsync(submitter, reviewer, documentTypeId);

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await readClient.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("PendingReview")]
    [InlineData("Rejected")]
    public async Task Get_with_read_only_permission_returns_403_when_not_approved(string reviewState)
    {
        using var submitter = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        using var reviewer = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);

        var documentId = reviewState switch
        {
            "Draft" => await DocumentReviewWorkflowTestSupport.CreateDraftDocumentWithFileAsync(submitter, documentTypeId),
            "PendingReview" => await DocumentReviewWorkflowTestSupport.CreatePendingReviewDocumentAsync(submitter, documentTypeId),
            "Rejected" => await DocumentReviewWorkflowTestSupport.CreateRejectedDocumentAsync(submitter, reviewer, documentTypeId),
            _ => throw new ArgumentOutOfRangeException(nameof(reviewState)),
        };

        using var readClient = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await readClient.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_no_permissions_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientAsync(_application, roleNames: []);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}/file", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static MultipartFormDataContent CreateFileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        return content;
    }

    private static async Task<int> SeedDocumentTypeAsync(DocumentFileStorageApiFactory application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static async Task<Guid> SeedDocumentAsync(DocumentFileStorageApiFactory application, int documentTypeId)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            $"CERT-{Guid.NewGuid():N}",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            "Active");

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document.Id;
    }
}
