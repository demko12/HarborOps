using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

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
        catch (HttpRequestException)
        {
            return new FleetApiResult<VesselPageResponse>(null, new FleetApiFailure(
                0,
                "HarborOps API is unavailable",
                "Please try again.",
                NoErrors));
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

internal sealed record VesselPageResponse(
    IReadOnlyList<VesselListItemResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record VesselListItemResponse(
    Guid VesselId,
    string Name,
    string ImoNumber,
    string FlagState,
    string CurrentStatus,
    Guid VesselTypeId,
    string VesselTypeName,
    Guid CompanyId,
    decimal? Dwt,
    DateTime UpdatedAtUtc,
    string? LifecycleStage,
    Guid? FleetId,
    string? FleetName);

internal sealed record FleetApiResult<T>(T? Value, FleetApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record FleetApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;
}
