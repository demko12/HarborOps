using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class OperationsDashboardApiClient(HttpClient httpClient)
{
    internal async Task<OperationsDashboardApiResult> GetAsync(
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/operations/dashboard?asOf={asOf:yyyy-MM-dd}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadFromJsonAsync<OperationsDashboardResponse>(cancellationToken);
                return responseBody is null
                    ? OperationsDashboardApiResult.Failed("Unable to load operations dashboard", "Please try again.")
                    : new OperationsDashboardApiResult(responseBody, null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            return OperationsDashboardApiResult.Failed(
                response.StatusCode == HttpStatusCode.Forbidden
                    ? "Dashboard access denied"
                    : problem?.Title ?? "Unable to load operations dashboard",
                response.StatusCode == HttpStatusCode.Forbidden
                    ? "Your account does not have access to the operations dashboard."
                    : problem?.Detail ?? "Please try again.",
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return OperationsDashboardApiResult.Failed(
                "HarborOps API is unavailable",
                "Please try again.");
        }
    }

    private static async Task<ApiProblemDetails?> TryReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
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

internal sealed record OperationsDashboardResponse(
    DateOnly AsOf,
    IReadOnlyList<OperationsDashboardSectionResponse> Sections,
    IReadOnlyList<OperationsDashboardPriorityItemResponse> PriorityItems);

internal sealed record OperationsDashboardSectionResponse(
    string Capability,
    string Title,
    string SummaryLabel,
    int Count,
    string Link,
    IReadOnlyList<OperationsDashboardAttentionItemResponse> Items,
    bool IsUnavailable);

internal sealed record OperationsDashboardAttentionItemResponse(
    string Title,
    string Detail,
    string? DueLabel,
    string Severity,
    string? Link);

internal sealed record OperationsDashboardPriorityItemResponse(
    string Capability,
    string CapabilityTitle,
    string Title,
    string Detail,
    string? DueLabel,
    string Severity,
    string Link);

internal sealed record OperationsDashboardApiResult(
    OperationsDashboardResponse? Value,
    OperationsDashboardApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;

    internal static OperationsDashboardApiResult Failed(
        string title,
        string message,
        int statusCode = 0) =>
        new(null, new OperationsDashboardApiFailure(statusCode, title, message));
}

internal sealed record OperationsDashboardApiFailure(int StatusCode, string Title, string Message)
{
    internal bool IsForbidden => StatusCode == StatusCodes.Status403Forbidden;
}
