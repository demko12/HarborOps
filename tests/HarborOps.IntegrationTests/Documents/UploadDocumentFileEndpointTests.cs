using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class UploadDocumentFileEndpointTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public UploadDocumentFileEndpointTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_with_valid_file_and_manage_permission_returns_201_and_persists_file()
    {
        var (client, userId) = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAndIdAsync(
            _application, Permissions.Documents.Manage);
        using var authenticatedClient = client;
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3, 4, 5], "certificate.pdf", "application/pdf");

        using var response = await authenticatedClient.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DocumentFileResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/documents/{documentId}", response.Headers.Location!.AbsolutePath);
        Assert.NotNull(result);
        Assert.Equal("certificate.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(5, result.SizeBytes);
        Assert.Equal(userId, result.UploadedBy);

        using var getResponse = await authenticatedClient.GetAsync(
            $"/api/documents/{documentId}", CancellationToken.None);
        var details = await getResponse.Content.ReadFromJsonAsync<DocumentDetailsResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(details);
        Assert.True(details.HasFile);
        Assert.Equal("certificate.pdf", details.FileName);
        Assert.Equal("application/pdf", details.ContentType);
        Assert.Equal(5, details.SizeBytes);
        Assert.Equal(userId, details.UploadedBy);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "certificate.pdf", "application/pdf");

        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_read_only_permission_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "certificate.pdf", "application/pdf");

        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var content = CreateFileContent([1, 2, 3], "certificate.pdf", "application/pdf");

        using var response = await client.PostAsync(
            $"/api/documents/{Guid.NewGuid()}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_when_document_already_has_a_file_returns_409()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using (var firstContent = CreateFileContent([1, 2, 3], "certificate.pdf", "application/pdf"))
        using (var firstResponse = await client.PostAsync(
            $"/api/documents/{documentId}/file", firstContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        }

        using var secondContent = CreateFileContent([4, 5, 6], "replacement.pdf", "application/pdf");
        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", secondContent, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_empty_file_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([], "certificate.pdf", "application/pdf");

        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_with_disallowed_extension_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "malware.exe", "application/octet-stream");

        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Post_when_file_exceeds_max_size_returns_413()
    {
        await using var factory = new SmallMaxSizeDocumentFileStorageApiFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }

        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            factory, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(factory);
        var documentId = await SeedDocumentAsync(factory, documentTypeId);

        var oversizedBytes = new byte[SmallMaxSizeDocumentFileStorageApiFactory.MaxSizeBytes + 1];
        using var content = CreateFileContent(oversizedBytes, "big.pdf", "application/pdf");

        using var response = await client.PostAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal((HttpStatusCode)StatusCodes.Status413PayloadTooLarge, response.StatusCode);
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
