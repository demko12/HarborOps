using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api.Fleet;

internal sealed class FleetApiClient(HttpClient httpClient)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal async Task<FleetApiResult<VesselPageResponse>> GetVesselsAsync(
        string? search,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"api/fleet/vessels?search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}";

        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<VesselPageResponse>(
                    await response.Content.ReadFromJsonAsync<VesselPageResponse>(cancellationToken),
                    null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            return new FleetApiResult<VesselPageResponse>(null, new FleetApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Fleet access denied" : "Unable to load vessels"),
                problem?.Detail ?? (isForbidden
                    ? "Your account does not have permission to view vessels."
                    : "Please try again."),
                problem?.Errors ?? NoErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new FleetApiResult<VesselPageResponse>(null, new FleetApiFailure(
                0,
                "HarborOps API is unavailable",
                "Please try again.",
                NoErrors));
        }
    }

    internal async Task<FleetApiResult<VesselDetailsWebModel>> GetVesselDetailsAsync(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/fleet/vessels/{vesselId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<VesselDetailsWebModel>(
                    await response.Content.ReadFromJsonAsync<VesselDetailsWebModel>(cancellationToken),
                    null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            return new FleetApiResult<VesselDetailsWebModel>(null, new FleetApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? response.StatusCode switch
                {
                    HttpStatusCode.Forbidden => "Fleet access denied",
                    HttpStatusCode.NotFound => "Vessel not found",
                    _ => "Unable to load the vessel",
                },
                problem?.Detail ?? response.StatusCode switch
                {
                    HttpStatusCode.Forbidden => "Your account does not have permission to view this vessel.",
                    HttpStatusCode.NotFound => "This vessel does not exist or may have been removed.",
                    _ => "Please try again.",
                },
                problem?.Errors ?? NoErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new FleetApiResult<VesselDetailsWebModel>(null, new FleetApiFailure(
                0,
                "HarborOps API is unavailable",
                "Please try again.",
                NoErrors));
        }
    }

    internal Task<FleetApiResult<VesselTypesWebModel>> GetVesselTypesAsync(CancellationToken cancellationToken) =>
        GetAsync<VesselTypesWebModel>("api/fleet/vessel-types", "Unable to load vessel types", cancellationToken);

    internal Task<FleetApiResult<VesselDetailsWebModel>> UpdateVesselRegistryDetailsAsync(
        Guid vesselId,
        UpdateVesselRegistryDetailsWebRequest request,
        CancellationToken cancellationToken) =>
        PutAsync<UpdateVesselRegistryDetailsWebRequest, VesselDetailsWebModel>(
            $"api/fleet/vessels/{vesselId}/registry-details",
            request,
            "Unable to update the vessel registry details",
            cancellationToken);

    internal Task<FleetApiResult<VesselDetailsWebModel>> UpdateVesselSpecificationsAsync(
        Guid vesselId,
        UpdateVesselSpecificationsWebRequest request,
        CancellationToken cancellationToken) =>
        PutAsync<UpdateVesselSpecificationsWebRequest, VesselDetailsWebModel>(
            $"api/fleet/vessels/{vesselId}/specifications",
            request,
            "Unable to update the vessel specifications",
            cancellationToken);

    internal Task<FleetApiResult<FleetDirectoryWebModel>> GetFleetsAsync(string? search, int pageNumber, int pageSize, CancellationToken cancellationToken) =>
        GetAsync<FleetDirectoryWebModel>($"api/fleet/fleets?search={Uri.EscapeDataString(search ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}", "Unable to load fleets", cancellationToken);

    internal Task<FleetApiResult<FleetDetailsWebModel>> GetFleetDetailsAsync(Guid fleetId, CancellationToken cancellationToken) =>
        GetAsync<FleetDetailsWebModel>($"api/fleet/fleets/{fleetId}", "Unable to load the fleet", cancellationToken);

    internal Task<FleetApiResult<FleetAssignmentHistoryWebModel>> GetFleetAssignmentHistoryAsync(Guid vesselId, CancellationToken cancellationToken) =>
        GetAsync<FleetAssignmentHistoryWebModel>($"api/fleet/vessels/{vesselId}/fleet-assignments", "Unable to load fleet assignment history", cancellationToken);

    internal Task<FleetApiResult<CreateFleetWebResponse>> CreateFleetAsync(CreateFleetWebRequest request, CancellationToken cancellationToken) =>
        PostAsync<CreateFleetWebRequest, CreateFleetWebResponse>("api/fleet/fleets", request, "Unable to create the fleet", cancellationToken);

    internal Task<FleetApiResult<object>> AssignFleetAsync(Guid vesselId, AssignFleetWebRequest request, CancellationToken cancellationToken) =>
        PostAsync<AssignFleetWebRequest, object>($"api/fleet/vessels/{vesselId}/fleet-assignments", request, "Unable to update the fleet assignment", cancellationToken);

    internal async Task<FleetApiResult<object>> EndFleetAssignmentAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync($"api/fleet/vessels/{vesselId}/fleet-assignments/end", null, cancellationToken);
            return response.IsSuccessStatusCode
                ? new FleetApiResult<object>(new object(), null)
                : new FleetApiResult<object>(null, await ReadFailureAsync(response, "Unable to update the fleet assignment", cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return Unavailable<object>(); }
    }

    internal async Task<FleetApiResult<RecordVesselStatusChangeWebResponse>> RecordStatusChangeAsync(
        Guid vesselId,
        RecordVesselStatusChangeWebRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync($"api/fleet/vessels/{vesselId}/status-changes", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<RecordVesselStatusChangeWebResponse>(
                    await response.Content.ReadFromJsonAsync<RecordVesselStatusChangeWebResponse>(cancellationToken),
                    null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            return new FleetApiResult<RecordVesselStatusChangeWebResponse>(null, new FleetApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? response.StatusCode switch
                {
                    HttpStatusCode.BadRequest => "The status change is not valid",
                    HttpStatusCode.Forbidden => "Fleet access denied",
                    HttpStatusCode.NotFound => "Vessel not found",
                    HttpStatusCode.Conflict => "The status change conflicts with the current vessel status",
                    _ => "Unable to record the status change",
                },
                problem?.Detail ?? response.StatusCode switch
                {
                    HttpStatusCode.Forbidden => "Your account does not have permission to change vessel status.",
                    HttpStatusCode.NotFound => "This vessel does not exist or may have been removed.",
                    _ => "Please try again.",
                },
                problem?.Errors ?? NoErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new FleetApiResult<RecordVesselStatusChangeWebResponse>(null, new FleetApiFailure(
                0,
                "HarborOps API is unavailable",
                "Please try again.",
                NoErrors));
        }
    }

    internal async Task<IReadOnlyDictionary<Guid, string>> LookupVesselNamesAsync(
        IReadOnlyCollection<Guid> vesselIds,
        CancellationToken cancellationToken)
    {
        if (vesselIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/fleet/vessels/lookup",
                new VesselLookupWebRequest(vesselIds),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<Guid, string>();
            }

            var result = await response.Content.ReadFromJsonAsync<VesselLookupWebResponse>(cancellationToken);

            return result?.Vessels?.ToDictionary(vessel => vessel.Id, vessel => vessel.Name)
                ?? new Dictionary<Guid, string>();
        }
        catch (HttpRequestException)
        {
            return new Dictionary<Guid, string>();
        }
    }

    internal Task<FleetApiResult<VesselPositionsWebModel>> GetVesselPositionsAsync(
    Guid? fleetId,
    string? status,
    bool staleOnly,
    int pageNumber,
    int pageSize,
    CancellationToken cancellationToken)
        => GetVesselPositionsAsync(fleetId, status, staleOnly, pageNumber, pageSize, null, cancellationToken);

    internal Task<FleetApiResult<VesselPositionsWebModel>> GetVesselPositionsAsync(
    Guid? fleetId,
    string? status,
    bool staleOnly,
    int pageNumber,
    int pageSize,
    string? search,
    CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}",
        };

        if (fleetId is { } fleet)
        {
            parameters.Add($"fleetId={fleet}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (staleOnly)
        {
            parameters.Add("staleOnly=true");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        return GetAsync<VesselPositionsWebModel>(
            $"api/fleet/vessel-positions?{string.Join('&', parameters)}",
            "Unable to load vessel positions",
            cancellationToken);
    }

    internal Task<FleetApiResult<RecordVesselPositionWebResponse>> RecordVesselPositionAsync(
    Guid vesselId,
    RecordVesselPositionWebRequest request,
    CancellationToken cancellationToken)
    {
        return PostAsync<RecordVesselPositionWebRequest, RecordVesselPositionWebResponse>(
            $"api/fleet/vessels/{vesselId}/position-reports",
            request,
            "Unable to record the position report",
            cancellationToken);
    }

    internal Task<FleetApiResult<VesselPositionHistoryWebModel>> GetVesselPositionHistoryAsync(Guid vesselId, DateTime? fromUtc, DateTime? toUtc, int pageNumber, CancellationToken cancellationToken)
    {
        var parameters = new List<string> { $"pageNumber={pageNumber}", "pageSize=50" };
        if (fromUtc is { } from) parameters.Add($"fromUtc={Uri.EscapeDataString(DateTime.SpecifyKind(from, DateTimeKind.Utc).ToString("O"))}");
        if (toUtc is { } to) parameters.Add($"toUtc={Uri.EscapeDataString(DateTime.SpecifyKind(to, DateTimeKind.Utc).ToString("O"))}");
        return GetAsync<VesselPositionHistoryWebModel>($"api/fleet/vessels/{vesselId}/position-reports?{string.Join('&', parameters)}", "Unable to load position history", cancellationToken);
    }

    private static async Task<ApiProblemDetails?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<FleetApiResult<T>> GetAsync<T>(string path, string fallbackTitle, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<T>(await response.Content.ReadFromJsonAsync<T>(cancellationToken), null);
            }

            return new FleetApiResult<T>(null, await ReadFailureAsync(response, fallbackTitle, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return Unavailable<T>(); }
    }

    private async Task<FleetApiResult<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest request, string fallbackTitle, CancellationToken cancellationToken) where TResponse : class
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<TResponse>(await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken), null);
            }

            return new FleetApiResult<TResponse>(null, await ReadFailureAsync(response, fallbackTitle, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return Unavailable<TResponse>(); }
    }

    private async Task<FleetApiResult<TResponse>> PutAsync<TRequest, TResponse>(string path, TRequest request, string fallbackTitle, CancellationToken cancellationToken) where TResponse : class
    {
        try
        {
            using var response = await httpClient.PutAsJsonAsync(path, request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new FleetApiResult<TResponse>(await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken), null);
            }

            return new FleetApiResult<TResponse>(null, await ReadFailureAsync(response, fallbackTitle, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return Unavailable<TResponse>(); }
    }

    private static async Task<FleetApiFailure> ReadFailureAsync(HttpResponseMessage response, string fallbackTitle, CancellationToken cancellationToken)
    {
        var problem = await TryReadProblemAsync(response, cancellationToken);
        var forbidden = response.StatusCode == HttpStatusCode.Forbidden;
        return new FleetApiFailure((int)response.StatusCode, problem?.Title ?? (forbidden ? "Fleet access denied" : fallbackTitle), problem?.Detail ?? (forbidden ? "Your account does not have permission for this action." : "Please try again."), problem?.Errors ?? NoErrors);
    }

    private static FleetApiResult<T> Unavailable<T>() where T : class => new(null, new FleetApiFailure(0, "HarborOps API is unavailable", "Please try again.", NoErrors));
}
