using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Documents;

internal sealed class DocumentsApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/documents";

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
