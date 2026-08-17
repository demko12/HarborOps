using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Cargo;

internal sealed class CargoBookingsApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/cargo/bookings";

    private const string ReviewAccessDetail = "Please review your access and try again.";
    private const string ReviewValuesDetail = "Please review the values and try again.";

    internal Task<CargoApiResult<CargoBookingPage>> SearchAsync(
        CargoBookingFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return GetAsync<CargoBookingPage>(
            BuildPath(filter),
            "Unable to load cargo bookings",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<CargoBookingDetails>> GetDetailsAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        return GetAsync<CargoBookingDetails>(
            $"{BasePath}/{bookingId}",
            "Unable to load the cargo booking",
            ReviewAccessDetail,
            cancellationToken);
    }

    internal Task<CargoApiResult<RegisteredCargoBooking>> RegisterAsync(
        RegisterCargoBookingWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RegisteredCargoBooking>(
            BasePath,
            request,
            "Unable to register the cargo booking",
            cancellationToken);
    }

    internal Task<CargoApiResult<DeclaredCargoSafetyInfo>> DeclareSafetyInformationAsync(
        Guid bookingId,
        DeclareCargoSafetyInfoWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<DeclaredCargoSafetyInfo>(
            $"{BasePath}/{bookingId}/safety-information",
            request,
            "Unable to declare the safety information",
            cancellationToken);
    }

    internal Task<CargoApiResult<RecordedCargoTrackingEvent>> RecordTrackingEventAsync(
        Guid bookingId,
        RecordCargoTrackingEventWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RecordedCargoTrackingEvent>(
            $"{BasePath}/{bookingId}/tracking-events",
            request,
            "Unable to record the tracking event",
            cancellationToken);
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

    private async Task<CargoApiResult<T>> GetAsync<T>(
        string path,
        string failureTitle,
        string failureDetail,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<T>(
                    default,
                    await ReadFailureAsync(response, failureTitle, failureDetail, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return new CargoApiResult<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
    }

    private async Task<CargoApiResult<T>> PostAsync<T>(
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CargoApiResult<T>(
                    default,
                    await ReadFailureAsync(response, failureTitle, ReviewValuesDetail, cancellationToken));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return new CargoApiResult<T>(value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CargoApiResult<T>(default, Unavailable());
        }
    }

    private static async Task<CargoApiFailure> ReadFailureAsync(
        HttpResponseMessage response,
        string failureTitle,
        string failureDetail,
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
            problem?.Title ?? failureTitle,
            problem?.Detail ?? failureDetail,
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
