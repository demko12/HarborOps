using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class GetDocumentByIdEndpointTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public GetDocumentByIdEndpointTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_document_without_a_file()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(documentId, details.Id);
        Assert.False(details.HasFile);
        Assert.Null(details.FileName);
        Assert.Null(details.ContentType);
        Assert.Null(details.SizeBytes);
        Assert.Null(details.UploadedBy);
        Assert.Null(details.UploadedAtUtc);
    }

    [Fact]
    public async Task Get_returns_document_with_a_file_after_upload()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using (var uploadContent = CreateFileContent([1, 2, 3], "certificate.pdf", "application/pdf"))
        using (var uploadResponse = await client.PostAsync(
            $"/api/documents/{documentId}/file", uploadContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        }

        using var response = await client.GetAsync($"/api/documents/{documentId}", CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.True(details.HasFile);
        Assert.Equal("certificate.pdf", details.FileName);
        Assert.Equal("application/pdf", details.ContentType);
        Assert.Equal(3, details.SizeBytes);
        Assert.NotNull(details.UploadedBy);
        Assert.NotNull(details.UploadedAtUtc);
    }

    [Fact]
    public async Task Get_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);

        using var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_no_permissions_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientAsync(_application, roleNames: []);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var response = await client.GetAsync($"/api/documents/{documentId}", CancellationToken.None);

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
