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

public sealed class ReplaceDocumentFileEndpointTests : DocumentFileStorageIntegrationTestBase
{
    private readonly DocumentFileStorageApiFactory _application;

    public ReplaceDocumentFileEndpointTests(DocumentFileStorageApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_replaces_existing_file_returns_200_and_new_content_is_downloadable()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        var originalBytes = new byte[] { 1, 2, 3 };
        using (var uploadContent = CreateFileContent(originalBytes, "original.pdf", "application/pdf"))
        using (var uploadResponse = await client.PostAsync(
            $"/api/documents/{documentId}/file", uploadContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        }

        var replacementBytes = new byte[] { 9, 8, 7, 6 };
        using var replaceContent = CreateFileContent(replacementBytes, "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{documentId}/file", replaceContent, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<DocumentFileResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("replacement.pdf", result.FileName);
        Assert.Equal(4, result.SizeBytes);

        using var downloadResponse = await client.GetAsync(
            $"/api/documents/{documentId}/file", CancellationToken.None);
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal(replacementBytes, downloadedBytes);
        Assert.NotEqual(originalBytes, downloadedBytes);
    }

    [Fact]
    public async Task Put_when_document_does_not_exist_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);

        using var content = CreateFileContent([1, 2, 3], "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{Guid.NewGuid()}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_when_document_has_no_existing_file_returns_404()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_with_read_only_permission_returns_403()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Read);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using var content = CreateFileContent([1, 2, 3], "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_with_empty_file_returns_400()
    {
        using var client = await TestUserSetup.CreateAuthenticatedClientWithPermissionsAsync(
            _application, Permissions.Documents.Manage);
        var documentTypeId = await SeedDocumentTypeAsync(_application);
        var documentId = await SeedDocumentAsync(_application, documentTypeId);

        using (var uploadContent = CreateFileContent([1, 2, 3], "original.pdf", "application/pdf"))
        using (var uploadResponse = await client.PostAsync(
            $"/api/documents/{documentId}/file", uploadContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        }

        using var content = CreateFileContent([], "replacement.pdf", "application/pdf");

        using var response = await client.PutAsync(
            $"/api/documents/{documentId}/file", content, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Put_when_file_exceeds_max_size_returns_413()
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

        using (var uploadContent = CreateFileContent([1, 2, 3], "original.pdf", "application/pdf"))
        using (var uploadResponse = await client.PostAsync(
            $"/api/documents/{documentId}/file", uploadContent, CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        }

        var oversizedBytes = new byte[SmallMaxSizeDocumentFileStorageApiFactory.MaxSizeBytes + 1];
        using var content = CreateFileContent(oversizedBytes, "big.pdf", "application/pdf");

        using var response = await client.PutAsync(
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
