using System.Net;
using System.Text.Json;

namespace HarborOps.Web.Api.Maintenance;

internal sealed class MaintenanceApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/maintenance/work-orders";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal async Task<MaintenanceApiResult<MaintenanceWorkOrdersPage>> GetWorkOrdersAsync(
        MaintenanceWorkOrderFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildGetPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<MaintenanceWorkOrdersPage>(
                    null, await ReadFailureAsync(response, "Unable to load work orders", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrdersPage>(cancellationToken);

            return new MaintenanceApiResult<MaintenanceWorkOrdersPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrdersPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrdersPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<CreatedMaintenanceWorkOrder>> CreateWorkOrderAsync(
    CreateMaintenanceWorkOrderRequest request,
    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(BasePath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<CreatedMaintenanceWorkOrder>(
                    null, await ReadFailureAsync(response, "Unable to create the work order", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<CreatedMaintenanceWorkOrder>(cancellationToken);

            return new MaintenanceApiResult<CreatedMaintenanceWorkOrder>(created, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<CreatedMaintenanceWorkOrder>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<CompleteMaintenanceWorkOrderApiResult>> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{BasePath}/{workOrderId}/completion", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<CompleteMaintenanceWorkOrderApiResult>(
                    null, await ReadFailureAsync(response, "Unable to complete the work order", cancellationToken));
            }

            var result = await response.Content
                .ReadFromJsonAsync<CompleteMaintenanceWorkOrderApiResult>(cancellationToken);

            return new MaintenanceApiResult<CompleteMaintenanceWorkOrderApiResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<CompleteMaintenanceWorkOrderApiResult>(null, Unavailable());
        }
    }

    private static string BuildGetPath(MaintenanceWorkOrderFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (filter.VesselId is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Priority))
        {
            query.Add($"priority={Uri.EscapeDataString(filter.Priority)}");
        }

        if (filter.Overdue is { } overdue)
        {
            query.Add($"overdue={(overdue ? "true" : "false")}");
        }

        return $"{BasePath}?{string.Join('&', query)}";
    }

    private static async Task<MaintenanceApiFailure> ReadFailureAsync(
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

        return new MaintenanceApiFailure(
            (int)response.StatusCode,
            problem?.Title ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Maintenance access denied"
                : defaultTitle),
            problem?.Detail ?? (response.StatusCode == HttpStatusCode.Forbidden
                ? "Your account does not have permission to access maintenance work orders."
                : "Please review the details and try again."),
            problem?.Errors ?? NoErrors);
    }

    private static MaintenanceApiFailure Unavailable()
    {
        return new MaintenanceApiFailure(
            0,
            "HarborOps API is unavailable",
            "The maintenance service cannot be reached right now.",
            NoErrors);
    }

}
