using System.Text.Json;

namespace HarborOps.Web.Api.Fleet;

internal sealed class FleetApiClient(
    HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

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
                new { VesselIds = vesselIds },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<Guid, string>();
            }

            var result = await response.Content.ReadFromJsonAsync<VesselLookupApiResponse>(
                JsonOptions, cancellationToken);

            return result?.Vessels.ToDictionary(vessel => vessel.Id, vessel => vessel.Name)
                ?? new Dictionary<Guid, string>();
        }
        catch (HttpRequestException)
        {
            return new Dictionary<Guid, string>();
        }
    }

    private sealed record VesselLookupApiResponse(IReadOnlyList<VesselLookupItem> Vessels);
}
