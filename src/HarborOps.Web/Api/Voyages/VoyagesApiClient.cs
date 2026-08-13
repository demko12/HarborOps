using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Voyages;

internal sealed class VoyagesApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/voyages";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal async Task<VoyagesApiResult<VoyagePage>> GetVoyagesAsync(
        DateOnly from,
        DateOnly to,
        string? search,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}");

        try
        {
            using var response = await httpClient.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var page = await response.Content.ReadFromJsonAsync<VoyagePage>(cancellationToken);

                return new VoyagesApiResult<VoyagePage>(page, null);
            }

            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
            }
            catch (JsonException)
            {
            }

            return new VoyagesApiResult<VoyagePage>(default, new VoyagesApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? "Unable to load voyages",
                problem?.Detail ?? "Please review the filters and try again.",
                problem?.Errors ?? NoErrors));
        }
        catch (HttpRequestException)
        {
            return new VoyagesApiResult<VoyagePage>(default, new VoyagesApiFailure(
                0,
                "HarborOps API is unavailable",
                "The voyages board could not reach the API.",
                NoErrors));
        }
    }
}
