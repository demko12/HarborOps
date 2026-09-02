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
