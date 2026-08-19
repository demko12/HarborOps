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

            return result?.Vessels.ToDictionary(vessel => vessel.Id, vessel => vessel.Name)
                ?? new Dictionary<Guid, string>();
        }
        catch (HttpRequestException)
        {
            return new Dictionary<Guid, string>();
        }
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
}
