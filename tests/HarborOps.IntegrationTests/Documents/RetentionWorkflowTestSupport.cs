using System.Net.Http.Json;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.IntegrationTests.Documents;

/// <summary>
/// Retention/legal-hold/disposition helpers, mirroring
/// <see cref="DocumentReviewWorkflowTestSupport"/>'s shape: drive the API surface end-to-end
/// via real HTTP calls rather than touching Document/RetentionPolicy rows directly, except for
/// seeding the DocumentType (same precedent as DocumentReviewWorkflowTestSupport.SeedDocumentTypeAsync).
/// </summary>
internal static class RetentionWorkflowTestSupport
{
    internal static async Task<Guid> RegisterExpiredActiveDocumentAsync(
        HttpClient client, int documentTypeId, DateOnly expiryDate, string? number = null)
    {
        var request = new RegisterDocumentRequest
        {
            VesselId = Guid.NewGuid(),
            DocumentTypeId = documentTypeId,
            Number = number ?? $"CERT-{Guid.NewGuid():N}",
            IssueDate = expiryDate.AddYears(-1),
            ExpiryDate = expiryDate,
            IssuingAuthority = "Flag State Authority",
            Status = Document.ActiveStatus,
        };

        using var response = await client.PostAsJsonAsync("/api/documents", request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<RegisterDocumentResponse>(CancellationToken.None);

        return created!.Id;
    }

    internal static async Task<HttpResponseMessage> UpdateRetentionPolicyAsync(
        HttpClient client, int documentTypeId, int? retentionPeriodDays) =>
        await client.PutAsJsonAsync(
            $"/api/documents/retention-policies/{documentTypeId}",
            new UpdateRetentionPolicyRequest(retentionPeriodDays),
            CancellationToken.None);

    internal static async Task SetRetentionPolicyAsync(HttpClient client, int documentTypeId, int retentionPeriodDays)
    {
        using var response = await UpdateRetentionPolicyAsync(client, documentTypeId, retentionPeriodDays);
        response.EnsureSuccessStatusCode();
    }

    internal static async Task<HttpResponseMessage> ArchiveAsync(HttpClient client, Guid documentId) =>
        await client.PostAsJsonAsync($"/api/documents/{documentId}/archive", new ArchiveDocumentRequest(null), CancellationToken.None);

    internal static async Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid documentId) =>
        await client.PostAsJsonAsync($"/api/documents/{documentId}/restore", new RestoreDocumentRequest(null), CancellationToken.None);

    internal static async Task<HttpResponseMessage> PlaceLegalHoldAsync(
        HttpClient client, Guid documentId, string? reason = "Litigation pending.", string? reference = null) =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/legal-holds", new PlaceLegalHoldRequest(reason, reference), CancellationToken.None);

    internal static async Task<Guid> PlaceActiveLegalHoldAsync(HttpClient client, Guid documentId)
    {
        using var response = await PlaceLegalHoldAsync(client, documentId);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<PlaceLegalHoldResponse>(CancellationToken.None);

        return created!.Id;
    }

    internal static async Task<HttpResponseMessage> ReleaseLegalHoldAsync(
        HttpClient client, Guid documentId, Guid holdId, string? reason = "Resolved.") =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/legal-holds/{holdId}/release", new ReleaseLegalHoldRequest(reason), CancellationToken.None);

    internal static async Task<HttpResponseMessage> CreateDispositionRequestAsync(
        HttpClient client, Guid documentId, string? reason = "Retention period elapsed.") =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/disposition-requests", new CreateDispositionRequest(reason), CancellationToken.None);

    internal static async Task<Guid> CreatePendingDispositionRequestAsync(HttpClient requesterClient, Guid documentId)
    {
        using var response = await CreateDispositionRequestAsync(requesterClient, documentId);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<DispositionRequestResponse>(CancellationToken.None);

        return created!.Id;
    }

    internal static async Task<HttpResponseMessage> ApproveDispositionRequestAsync(
        HttpClient client, Guid documentId, Guid requestId, string? decisionReason = null) =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/disposition-requests/{requestId}/approve",
            new ApproveDispositionRequestRequest(decisionReason),
            CancellationToken.None);

    internal static async Task<HttpResponseMessage> RejectDispositionRequestAsync(
        HttpClient client, Guid documentId, Guid requestId, string? decisionReason = "Missing approval chain.") =>
        await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/disposition-requests/{requestId}/reject",
            new RejectDispositionRequestRequest(decisionReason),
            CancellationToken.None);
}
