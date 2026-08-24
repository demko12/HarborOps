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
        bool includeActive,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&includeActive={(includeActive ? "true" : "false")}&pageNumber={pageNumber}&pageSize={pageSize}");

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    internal Task<VoyagesApiResult<ScheduledVoyage>> ScheduleAsync(
        ScheduleVoyageWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ScheduledVoyage>(BasePath, request, "Unable to schedule the voyage", cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageTransition>> RecordDepartureAsync(
        Guid voyageId,
        RecordDepartureWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<VoyageTransition>(
            $"{BasePath}/{voyageId}/departure",
            request,
            "Unable to record the departure",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageTransition>> RecordArrivalAsync(
        Guid voyageId,
        RecordArrivalWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<VoyageTransition>(
            $"{BasePath}/{voyageId}/arrival",
            request,
            "Unable to record the arrival",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortPage>> GetPortsAsync(
        string? search,
        string? country,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}/ports?search={Uri.EscapeDataString(search ?? string.Empty)}&country={Uri.EscapeDataString(country ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}");

        return GetAsync<PortPage>(path, "Unable to load ports", cancellationToken);
    }

    internal Task<VoyagesApiResult<PortLookupResult>> LookupPortsAsync(
        string? search,
        int take,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}/ports/lookup?search={Uri.EscapeDataString(search ?? string.Empty)}&take={take}");

        return GetAsync<PortLookupResult>(path, "Unable to load ports", cancellationToken);
    }

    internal Task<VoyagesApiResult<RegisteredPort>> RegisterPortAsync(
        RegisterPortWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RegisteredPort>(
            $"{BasePath}/ports",
            request,
            "Unable to register the port",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<BerthPage>> GetBerthsAsync(
        Guid portId,
        string? search,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}/berths?portId={portId}&search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}");

        return GetAsync<BerthPage>(path, "Unable to load berths", cancellationToken);
    }

    internal Task<VoyagesApiResult<RegisteredBerth>> RegisterBerthAsync(
        RegisterBerthWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RegisteredBerth>(
            $"{BasePath}/berths",
            request,
            "Unable to register the berth",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<RoutePage>> GetRoutesAsync(
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}/routes?search={Uri.EscapeDataString(search ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}");

        return GetAsync<RoutePage>(path, "Unable to load routes", cancellationToken);
    }

    internal Task<VoyagesApiResult<RouteDetails>> GetRouteDetailsAsync(
        Guid routeId,
        CancellationToken cancellationToken)
    {
        return GetAsync<RouteDetails>(
            $"{BasePath}/routes/{routeId}",
            "Unable to load the route",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<RegisteredRoute>> RegisterRouteAsync(
        RegisterRouteWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RegisteredRoute>(
            $"{BasePath}/routes",
            request,
            "Unable to register the route",
            cancellationToken);
    }

    private async Task<VoyagesApiResult<T>> GetAsync<T>(
        string path,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

                return new VoyagesApiResult<T>(value, null);
            }

            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
            }
            catch (JsonException)
            {
            }

            return new VoyagesApiResult<T>(default, new VoyagesApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? failureTitle,
                problem?.Detail ?? "Please review the filters and try again.",
                problem?.Errors ?? NoErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new VoyagesApiResult<T>(default, new VoyagesApiFailure(
                0,
                "HarborOps API is unavailable",
                "The voyages workspace could not reach the API.",
                NoErrors));
        }
    }

    private async Task<VoyagesApiResult<T>> PostAsync<T>(
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

                return new VoyagesApiResult<T>(value, null);
            }

            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
            }
            catch (JsonException)
            {
            }

            return new VoyagesApiResult<T>(default, new VoyagesApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? failureTitle,
                problem?.Detail ?? "Please review the values and try again.",
                problem?.Errors ?? NoErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new VoyagesApiResult<T>(default, new VoyagesApiFailure(
                0,
                "HarborOps API is unavailable",
                "The voyages board could not reach the API.",
                NoErrors));
        }
    }

    internal async Task<IReadOnlyDictionary<Guid, string>> LookupPortNamesAsync(
        IReadOnlyCollection<Guid> portIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portIds);

        if (portIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var query = string.Join(
            "&",
            portIds.Distinct().Select(portId => $"portIds={portId:D}"));

        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/ports/names?{query}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<Guid, string>();
            }

            var payload = await response.Content.ReadFromJsonAsync<PortNamesPayload>(cancellationToken);

            return payload?.Ports.ToDictionary(port => port.PortId, port => port.Name)
                ?? new Dictionary<Guid, string>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new Dictionary<Guid, string>();
        }
    }
}
