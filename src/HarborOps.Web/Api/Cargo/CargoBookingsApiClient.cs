using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Cargo;

internal sealed class CargoBookingsApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/cargo/bookings";

    internal async Task<CargoApiResult<CargoBookingPage>> SearchAsync(
        CargoBookingFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<CargoBookingPage>(null, await ReadFailureAsync(response, cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<CargoBookingPage>(cancellationToken);

            return new CargoApiResult<CargoBookingPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<CargoBookingPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<CargoBookingPage>(null, Unavailable());
        }
    }

    private static string BuildPath(CargoBookingFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query.Insert(0, $"search={Uri.EscapeDataString(filter.Search.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Insert(0, $"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.IsHazardous is { } isHazardous)
        {
            query.Add($"isHazardous={(isHazardous ? "true" : "false")}");
        }

        return $"{BasePath}?{string.Join('&', query)}";
    }

    private static async Task<CargoApiFailure> ReadFailureAsync(
        HttpResponseMessage response,
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

        return new CargoApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? "Unable to load cargo bookings",
            problem?.Detail ?? "Please review your access and try again.",
            problem?.Errors ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    private static CargoApiFailure Unavailable()
    {
        return new CargoApiFailure(
            0,
            "HarborOps API is unavailable",
            "The cargo service cannot be reached right now.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }
}
