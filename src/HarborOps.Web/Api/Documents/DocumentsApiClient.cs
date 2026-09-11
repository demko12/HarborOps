using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Api.Documents;

internal sealed class DocumentsApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/documents";

    // Must stay in sync with DocumentFileValidationOptions.MaxSizeBytes (server default,
    // 25 MB) - see report for this known coupling. IBrowserFile.OpenReadStream defaults to
    // a 500 KB cap unless a larger maxAllowedSize is passed explicitly, so this has to be
    // set here regardless; picking the server's current default keeps client and server
    // limits aligned today, but they are not otherwise linked and can drift if the server
    // value changes.
    private const long MaxUploadSizeBytes = 26_214_400;

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal async Task<DocumentsApiResult<ExpiringDocumentsPage>> GetExpiringDocumentsAsync(
        GetExpiringDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildExpiringPath(query),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<ExpiringDocumentsPage>(
                    null, await ReadFailureAsync(response, "Unable to load expiring documents", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<ExpiringDocumentsPage>(cancellationToken);

            return new DocumentsApiResult<ExpiringDocumentsPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<ExpiringDocumentsPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<ExpiringDocumentsPage>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<DocumentsPage>> GetDocumentsAsync(
        GetDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildDocumentsPath(query),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<DocumentsPage>(
                    null, await ReadFailureAsync(response, "Unable to load documents", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<DocumentsPage>(cancellationToken);

            return new DocumentsApiResult<DocumentsPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<DocumentsPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<DocumentsPage>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<RegisterDocumentResult>> RegisterDocumentAsync(
        RegisterDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(BasePath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new DocumentsApiResult<RegisterDocumentResult>(
                    null, await ReadFailureAsync(response, "Unable to register the document", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<RegisterDocumentResult>(cancellationToken);

            return new DocumentsApiResult<RegisterDocumentResult>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<RegisterDocumentResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<RegisterDocumentResult>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<DocumentTypesPage>> GetDocumentTypesAsync(
        GetDocumentTypesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildDocumentTypesPath(query),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<DocumentTypesPage>(
                    null, await ReadFailureAsync(response, "Unable to load document types", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<DocumentTypesPage>(cancellationToken);

            return new DocumentsApiResult<DocumentTypesPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<DocumentTypesPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<DocumentTypesPage>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<RegisterDocumentTypeResult>> RegisterDocumentTypeAsync(
        RegisterDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{BasePath}/types", request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new DocumentsApiResult<RegisterDocumentTypeResult>(
                    null, await ReadFailureAsync(response, "Unable to register the document type", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<RegisterDocumentTypeResult>(cancellationToken);

            return new DocumentsApiResult<RegisterDocumentTypeResult>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<RegisterDocumentTypeResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<RegisterDocumentTypeResult>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<DocumentDetailsDto>> GetDocumentByIdAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/{documentId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<DocumentDetailsDto>(
                    null, await ReadFailureAsync(response, "Unable to load the document", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<DocumentDetailsDto>(cancellationToken);

            return new DocumentsApiResult<DocumentDetailsDto>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<DocumentDetailsDto>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<DocumentDetailsDto>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<object>> UpdateMetadataAsync(Guid documentId, DocumentMetadataRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"{BasePath}/{documentId}/metadata", request, cancellationToken);
        return response.IsSuccessStatusCode ? new(new object(), null) : new(null, await ReadFailureAsync(response, "Unable to update document metadata", cancellationToken));
    }

    internal async Task<DocumentsApiResult<RegisterDocumentResult>> RenewAsync(Guid documentId, DocumentMetadataRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/renewals", request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created) return new(null, await ReadFailureAsync(response, "Unable to renew the document", cancellationToken));
        return new(await response.Content.ReadFromJsonAsync<RegisterDocumentResult>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<IReadOnlyList<DocumentRenewalHistoryItemDto>>> GetRenewalHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{BasePath}/{documentId}/renewal-history", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(null, await ReadFailureAsync(response, "Unable to load renewal history", cancellationToken));
            return new(await response.Content.ReadFromJsonAsync<IReadOnlyList<DocumentRenewalHistoryItemDto>>(cancellationToken), null);
        }
        catch (JsonException)
        {
            return new(null, new DocumentsApiFailure(0, "Unable to load renewal history", "Please try again.", NoErrors));
        }
    }

    internal async Task<DocumentsApiResult<object>> SubmitForReviewAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync($"{BasePath}/{documentId}/submit-for-review", null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<object>(null, await ReadFailureAsync(response, "Unable to submit the document for review", cancellationToken));
            }

            return new DocumentsApiResult<object>(new object(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<object>> ReviewAsync(Guid documentId, ReviewDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/review", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<object>(null, await ReadFailureAsync(response, "Unable to review the document", cancellationToken));
            }

            return new DocumentsApiResult<object>(new object(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<object>> RevokeAsync(Guid documentId, RevokeDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/revoke", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<object>(null, await ReadFailureAsync(response, "Unable to revoke the document", cancellationToken));
            }

            return new DocumentsApiResult<object>(new object(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<object>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<IReadOnlyList<DocumentReviewEventResponseDto>>> GetReviewHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{BasePath}/{documentId}/review-history", HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new(null, await ReadFailureAsync(response, "Unable to load review history", cancellationToken));
            }

            return new(await response.Content.ReadFromJsonAsync<IReadOnlyList<DocumentReviewEventResponseDto>>(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, Unavailable());
        }
        catch (JsonException)
        {
            return new(null, new DocumentsApiFailure(0, "Unable to load review history", "Please try again.", NoErrors));
        }
    }

    internal async Task<DocumentsApiResult<DocumentFileResponseDto>> UploadDocumentFileAsync(
        Guid documentId,
        IBrowserFile file,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            using var content = BuildFileContent(file, progress, cancellationToken);

            using var response = await httpClient.PostAsync(
                $"{BasePath}/{documentId}/file", content, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new DocumentsApiResult<DocumentFileResponseDto>(
                    null, await ReadFailureAsync(response, "Unable to upload the document file", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<DocumentFileResponseDto>(cancellationToken);

            return new DocumentsApiResult<DocumentFileResponseDto>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<DocumentFileResponseDto>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<DocumentFileResponseDto>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<DocumentFileResponseDto>> ReplaceDocumentFileAsync(
        Guid documentId,
        IBrowserFile file,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            using var content = BuildFileContent(file, progress, cancellationToken);

            using var response = await httpClient.PutAsync(
                $"{BasePath}/{documentId}/file", content, cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new DocumentsApiResult<DocumentFileResponseDto>(
                    null, await ReadFailureAsync(response, "Unable to replace the document file", cancellationToken));
            }

            var replaced = await response.Content.ReadFromJsonAsync<DocumentFileResponseDto>(cancellationToken);

            return new DocumentsApiResult<DocumentFileResponseDto>(replaced, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<DocumentFileResponseDto>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<DocumentFileResponseDto>(null, Unavailable());
        }
    }

    /// <summary>
    /// Downloads the document's file and returns the raw response so the caller (the download
    /// proxy endpoint) can stream the body and forward Content-Type/Content-Disposition headers
    /// unchanged. Unlike every other method here, the returned <see cref="HttpResponseMessage"/>
    /// on success is NOT disposed by this method - the caller owns it and must dispose it once
    /// the body has been streamed. On failure the response is read and disposed internally, as
    /// usual, before returning.
    /// </summary>
    internal async Task<DocumentsApiResult<HttpResponseMessage>> DownloadDocumentFileAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"{BasePath}/{documentId}/file",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadFailureAsync(response, "Unable to download the document file", cancellationToken);
                response.Dispose();

                return new DocumentsApiResult<HttpResponseMessage>(null, failure);
            }

            return new DocumentsApiResult<HttpResponseMessage>(response, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<HttpResponseMessage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<HttpResponseMessage>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<RetentionPoliciesResultDto>> GetRetentionPoliciesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/retention-policies",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<RetentionPoliciesResultDto>(
                    null, await ReadFailureAsync(response, "Unable to load retention policies", cancellationToken));
            }

            return new DocumentsApiResult<RetentionPoliciesResultDto>(
                await response.Content.ReadFromJsonAsync<RetentionPoliciesResultDto>(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<RetentionPoliciesResultDto>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<RetentionPoliciesResultDto>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<object>> UpdateRetentionPolicyAsync(
        int documentTypeId,
        UpdateRetentionPolicyRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync($"{BasePath}/retention-policies/{documentTypeId}", request, cancellationToken);

        return response.IsSuccessStatusCode
            ? new DocumentsApiResult<object>(new object(), null)
            : new DocumentsApiResult<object>(null, await ReadFailureAsync(response, "Unable to update the retention policy", cancellationToken));
    }

    internal async Task<DocumentsApiResult<RetentionQueuePageDto>> GetRetentionQueueAsync(
        GetRetentionQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildRetentionQueuePath(query),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new DocumentsApiResult<RetentionQueuePageDto>(
                    null, await ReadFailureAsync(response, "Unable to load the retention queue", cancellationToken));
            }

            return new DocumentsApiResult<RetentionQueuePageDto>(
                await response.Content.ReadFromJsonAsync<RetentionQueuePageDto>(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new DocumentsApiResult<RetentionQueuePageDto>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DocumentsApiResult<RetentionQueuePageDto>(null, Unavailable());
        }
    }

    internal async Task<DocumentsApiResult<DocumentRetentionStateResultDto>> ArchiveAsync(
        Guid documentId,
        ArchiveDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/archive", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new DocumentsApiResult<DocumentRetentionStateResultDto>(
                null, await ReadFailureAsync(response, "Unable to archive the document", cancellationToken));
        }

        return new DocumentsApiResult<DocumentRetentionStateResultDto>(
            await response.Content.ReadFromJsonAsync<DocumentRetentionStateResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<DocumentRetentionStateResultDto>> RestoreAsync(
        Guid documentId,
        RestoreDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/restore", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new DocumentsApiResult<DocumentRetentionStateResultDto>(
                null, await ReadFailureAsync(response, "Unable to restore the document", cancellationToken));
        }

        return new DocumentsApiResult<DocumentRetentionStateResultDto>(
            await response.Content.ReadFromJsonAsync<DocumentRetentionStateResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<PlaceLegalHoldResultDto>> PlaceLegalHoldAsync(
        Guid documentId,
        PlaceLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/legal-holds", request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            return new DocumentsApiResult<PlaceLegalHoldResultDto>(
                null, await ReadFailureAsync(response, "Unable to place a legal hold", cancellationToken));
        }

        return new DocumentsApiResult<PlaceLegalHoldResultDto>(
            await response.Content.ReadFromJsonAsync<PlaceLegalHoldResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<ReleaseLegalHoldResultDto>> ReleaseLegalHoldAsync(
        Guid documentId,
        Guid holdId,
        ReleaseLegalHoldRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/legal-holds/{holdId}/release", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new DocumentsApiResult<ReleaseLegalHoldResultDto>(
                null, await ReadFailureAsync(response, "Unable to release the legal hold", cancellationToken));
        }

        return new DocumentsApiResult<ReleaseLegalHoldResultDto>(
            await response.Content.ReadFromJsonAsync<ReleaseLegalHoldResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<DispositionRequestResultDto>> CreateDispositionRequestAsync(
        Guid documentId,
        CreateDispositionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/disposition-requests", request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            return new DocumentsApiResult<DispositionRequestResultDto>(
                null, await ReadFailureAsync(response, "Unable to create the disposition request", cancellationToken));
        }

        return new DocumentsApiResult<DispositionRequestResultDto>(
            await response.Content.ReadFromJsonAsync<DispositionRequestResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<DispositionRequestResultDto>> ApproveDispositionRequestAsync(
        Guid documentId,
        Guid requestId,
        ApproveDispositionRequestRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/disposition-requests/{requestId}/approve", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new DocumentsApiResult<DispositionRequestResultDto>(
                null, await ReadFailureAsync(response, "Unable to approve the disposition request", cancellationToken));
        }

        return new DocumentsApiResult<DispositionRequestResultDto>(
            await response.Content.ReadFromJsonAsync<DispositionRequestResultDto>(cancellationToken), null);
    }

    internal async Task<DocumentsApiResult<DispositionRequestResultDto>> RejectDispositionRequestAsync(
        Guid documentId,
        Guid requestId,
        RejectDispositionRequestRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BasePath}/{documentId}/disposition-requests/{requestId}/reject", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new DocumentsApiResult<DispositionRequestResultDto>(
                null, await ReadFailureAsync(response, "Unable to reject the disposition request", cancellationToken));
        }

        return new DocumentsApiResult<DispositionRequestResultDto>(
            await response.Content.ReadFromJsonAsync<DispositionRequestResultDto>(cancellationToken), null);
    }

    private static string BuildRetentionQueuePath(GetRetentionQueueQuery query)
    {
        var extra = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            extra.Add($"state={Uri.EscapeDataString(query.State)}");
        }

        if (query.DueBefore is { } dueBefore)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"dueBefore={dueBefore:yyyy-MM-dd}"));
        }

        if (query.PageNumber is { } pageNumber)
        {
            extra.Add($"pageNumber={pageNumber}");
        }

        if (query.PageSize is { } pageSize)
        {
            extra.Add($"pageSize={pageSize}");
        }

        return extra.Count == 0 ? $"{BasePath}/retention-queue" : $"{BasePath}/retention-queue?{string.Join('&', extra)}";
    }

    private static MultipartFormDataContent BuildFileContent(
        IBrowserFile file,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var browserStream = file.OpenReadStream(MaxUploadSizeBytes, cancellationToken);
        var progressStream = new ProgressReportingStream(browserStream, file.Size, progress);

        var streamContent = new StreamContent(progressStream);
        if (!string.IsNullOrWhiteSpace(file.ContentType))
        {
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        }

        var content = new MultipartFormDataContent
        {
            { streamContent, "file", file.Name },
        };

        return content;
    }

    private static string BuildExpiringPath(GetExpiringDocumentsQuery query)
    {
        var extra = new List<string>();

        if (query.From is { } from)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"from={from:yyyy-MM-dd}"));
        }

        if (query.To is { } to)
        {
            extra.Add(string.Create(CultureInfo.InvariantCulture, $"to={to:yyyy-MM-dd}"));
        }

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(query.ReviewStatus))
        {
            extra.Add($"reviewStatus={Uri.EscapeDataString(query.ReviewStatus)}");
        }

        if (query.PageNumber is { } pageNumber)
        {
            extra.Add($"pageNumber={pageNumber}");
        }

        if (query.PageSize is { } pageSize)
        {
            extra.Add($"pageSize={pageSize}");
        }

        return extra.Count == 0 ? $"{BasePath}/expiring" : $"{BasePath}/expiring?{string.Join('&', extra)}";
    }

    private static string BuildDocumentTypesPath(GetDocumentTypesQuery query)
    {
        var extra = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            extra.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        if (query.PageSize is { } pageSize)
        {
            extra.Add($"pageSize={pageSize}");
        }

        return extra.Count == 0 ? $"{BasePath}/types" : $"{BasePath}/types?{string.Join('&', extra)}";
    }

    private static string BuildDocumentsPath(GetDocumentsQuery query)
    {
        var extra = new List<string>();

        if (query.VesselId is { } vesselId)
        {
            extra.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            extra.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        if (query.PageNumber is { } pageNumber)
        {
            extra.Add($"pageNumber={pageNumber}");
        }

        if (query.PageSize is { } pageSize)
        {
            extra.Add($"pageSize={pageSize}");
        }

        return extra.Count == 0 ? BasePath : $"{BasePath}?{string.Join('&', extra)}";
    }

    private static async Task<DocumentsApiFailure> ReadFailureAsync(
        HttpResponseMessage response,
        string defaultTitle,
        CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        }
        catch (JsonException)
        {
            // Not every failure carries a problem details body; keep the page retryable.
        }

        return new DocumentsApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Documents access denied"
                : defaultTitle),
            problem?.Detail ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Your account does not have permission to access documents."
                : "Please review the details and try again."),
            problem?.Errors ?? NoErrors);
    }

    private static DocumentsApiFailure Unavailable()
    {
        return new DocumentsApiFailure(
            0,
            "HarborOps API is unavailable",
            "The documents service cannot be reached right now.",
            NoErrors);
    }
}
