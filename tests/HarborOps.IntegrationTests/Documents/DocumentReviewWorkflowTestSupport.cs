using System.Net.Http.Headers;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

/// <summary>
/// Drives documents through the review workflow via real HTTP calls (register -> upload
/// file -> submit-for-review -> review/revoke), matching how the other Documents endpoint
/// test files seed a DocumentType directly against the DbContext but otherwise exercise the
/// API surface end-to-end rather than touching Document/DocumentReviewEvent rows directly.
/// </summary>
internal static class DocumentReviewWorkflowTestSupport
{
    internal static async Task<int> SeedDocumentTypeAsync(DocumentFileStorageApiFactory application)
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

    internal static async Task<Guid> RegisterDocumentAsync(HttpClient client, int documentTypeId, string? number = null)
    {
        var request = new RegisterDocumentRequest
        {
            VesselId = Guid.NewGuid(),
            DocumentTypeId = documentTypeId,
            Number = number ?? $"CERT-{Guid.NewGuid():N}",
            IssueDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            IssuingAuthority = "Flag State Authority",
            Status = Document.ActiveStatus,
        };

        using var response = await client.PostAsJsonAsync("/api/documents", request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<RegisterDocumentResponse>(CancellationToken.None);

        return created!.Id;
    }

    internal static async Task UploadFileAsync(HttpClient client, Guid documentId)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4, 5]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "certificate.pdf");

        using var response = await client.PostAsync($"/api/documents/{documentId}/file", content, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    internal static async Task<Guid> CreateDraftDocumentWithFileAsync(HttpClient client, int documentTypeId, string? number = null)
    {
        var documentId = await RegisterDocumentAsync(client, documentTypeId, number);
        await UploadFileAsync(client, documentId);

        return documentId;
    }

    internal static async Task<HttpResponseMessage> SubmitForReviewAsync(HttpClient client, Guid documentId) =>
        await client.PostAsync($"/api/documents/{documentId}/submit-for-review", null, CancellationToken.None);

    internal static async Task<Guid> CreatePendingReviewDocumentAsync(
        HttpClient submitterClient, int documentTypeId, string? number = null)
    {
        var documentId = await CreateDraftDocumentWithFileAsync(submitterClient, documentTypeId, number);
        using var response = await SubmitForReviewAsync(submitterClient, documentId);
        response.EnsureSuccessStatusCode();

        return documentId;
    }

    internal static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client, Guid documentId, string decision, string? notes = null) =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/review", new ReviewDocumentRequest(decision, notes), CancellationToken.None);

    internal static async Task<Guid> CreateApprovedDocumentAsync(
        HttpClient submitterClient, HttpClient reviewerClient, int documentTypeId, string? number = null)
    {
        var documentId = await CreatePendingReviewDocumentAsync(submitterClient, documentTypeId, number);
        using var response = await ReviewAsync(reviewerClient, documentId, Document.ApprovedReviewStatus);
        response.EnsureSuccessStatusCode();

        return documentId;
    }

    internal static async Task<Guid> CreateRejectedDocumentAsync(
        HttpClient submitterClient, HttpClient reviewerClient, int documentTypeId, string notes = "Missing signature page.", string? number = null)
    {
        var documentId = await CreatePendingReviewDocumentAsync(submitterClient, documentTypeId, number);
        using var response = await ReviewAsync(reviewerClient, documentId, Document.RejectedReviewStatus, notes);
        response.EnsureSuccessStatusCode();

        return documentId;
    }

    internal static async Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid documentId, string reason) =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/revoke", new RevokeDocumentRequest(reason), CancellationToken.None);
}
