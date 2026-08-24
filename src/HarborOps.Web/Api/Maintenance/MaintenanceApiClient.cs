using System.Globalization;
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

    private const string InventoryItemsPath = "api/maintenance/inventory-items";

    internal async Task<MaintenanceApiResult<InventoryItemsPage>> GetInventoryItemsAsync(
        InventoryItemFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildInventoryItemsPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<InventoryItemsPage>(
                    null, await ReadFailureAsync(response, "Unable to load inventory items", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<InventoryItemsPage>(cancellationToken);

            return new MaintenanceApiResult<InventoryItemsPage>(page, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<InventoryItemsPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<InventoryItemsPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<WorkOrderPartUsagesResult>> GetPartUsagesAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/{workOrderId}/part-usages",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<WorkOrderPartUsagesResult>(
                    null, await ReadFailureAsync(response, "Unable to load part usage", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<WorkOrderPartUsagesResult>(cancellationToken);

            return new MaintenanceApiResult<WorkOrderPartUsagesResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<WorkOrderPartUsagesResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<RecordPartUsageResult>> RecordPartUsageAsync(
        Guid workOrderId,
        Guid partId,
        RecordPartUsageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{BasePath}/{workOrderId}/parts/{partId}/usage", request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<RecordPartUsageResult>(
                    null, await ReadFailureAsync(response, "Unable to record part usage", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<RecordPartUsageResult>(cancellationToken);

            return new MaintenanceApiResult<RecordPartUsageResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<RecordPartUsageResult>(null, Unavailable());
        }
    }

    private static string BuildInventoryItemsPath(InventoryItemFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query.Add($"search={Uri.EscapeDataString(filter.Search)}");
        }

        if (filter.LowStock is { } lowStock)
        {
            query.Add($"lowStock={(lowStock ? "true" : "false")}");
        }

        return $"{InventoryItemsPath}?{string.Join('&', query)}";
    }

    private const string EquipmentPath = "api/maintenance/equipment";

    private const string SchedulesPath = "api/maintenance/schedules";

    internal async Task<MaintenanceApiResult<EquipmentPage>> GetEquipmentAsync(
        EquipmentFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildEquipmentPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<EquipmentPage>(
                    null, await ReadFailureAsync(response, "Unable to load equipment", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<EquipmentPage>(cancellationToken);

            return new MaintenanceApiResult<EquipmentPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<EquipmentPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<EquipmentPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<RegisteredEquipment>> RegisterEquipmentAsync(
        RegisterEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(EquipmentPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<RegisteredEquipment>(
                    null, await ReadFailureAsync(response, "Unable to register the equipment", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<RegisteredEquipment>(cancellationToken);

            return new MaintenanceApiResult<RegisteredEquipment>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<RegisteredEquipment>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<RegisteredEquipment>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<MaintenanceSchedulesPage>> GetSchedulesAsync(
        MaintenanceScheduleFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildSchedulesPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<MaintenanceSchedulesPage>(
                    null, await ReadFailureAsync(response, "Unable to load preventive schedules", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<MaintenanceSchedulesPage>(cancellationToken);

            return new MaintenanceApiResult<MaintenanceSchedulesPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<MaintenanceSchedulesPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<MaintenanceSchedulesPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<MaintenanceScheduleDetails>> GetScheduleDetailsAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{SchedulesPath}/{scheduleId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<MaintenanceScheduleDetails>(
                    null, await ReadFailureAsync(response, "Unable to load the schedule", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<MaintenanceScheduleDetails>(cancellationToken);

            return new MaintenanceApiResult<MaintenanceScheduleDetails>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<MaintenanceScheduleDetails>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<MaintenanceScheduleDetails>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<RegisteredMaintenanceSchedule>> RegisterScheduleAsync(
        RegisterMaintenanceScheduleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(SchedulesPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<RegisteredMaintenanceSchedule>(
                    null, await ReadFailureAsync(response, "Unable to register the schedule", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<RegisteredMaintenanceSchedule>(cancellationToken);

            return new MaintenanceApiResult<RegisteredMaintenanceSchedule>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<RegisteredMaintenanceSchedule>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<RegisteredMaintenanceSchedule>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<UpdateScheduleNextDueResult>> UpdateScheduleNextDueAsync(
        Guid scheduleId,
        UpdateScheduleNextDueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{SchedulesPath}/{scheduleId}/next-due", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<UpdateScheduleNextDueResult>(
                    null, await ReadFailureAsync(response, "Unable to update the next due values", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<UpdateScheduleNextDueResult>(cancellationToken);

            return new MaintenanceApiResult<UpdateScheduleNextDueResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<UpdateScheduleNextDueResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<UpdateScheduleNextDueResult>(null, Unavailable());
        }
    }

    private static string BuildEquipmentPath(EquipmentFilter filter)
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

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query.Add($"category={Uri.EscapeDataString(filter.Category)}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Manufacturer))
        {
            query.Add($"manufacturer={Uri.EscapeDataString(filter.Manufacturer)}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query.Add($"search={Uri.EscapeDataString(filter.Search)}");
        }

        return $"{EquipmentPath}?{string.Join('&', query)}";
    }

    private static string BuildSchedulesPath(MaintenanceScheduleFilter filter)
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

        if (filter.EquipmentId is { } equipmentId)
        {
            query.Add($"equipmentId={equipmentId}");
        }

        if (filter.DueFrom is { } dueFrom)
        {
            query.Add($"dueFrom={FormatDate(dueFrom)}");
        }

        if (filter.DueTo is { } dueTo)
        {
            query.Add($"dueTo={FormatDate(dueTo)}");
        }

        if (filter.Overdue is { } overdue)
        {
            query.Add($"overdue={(overdue ? "true" : "false")}");
        }

        return $"{SchedulesPath}?{string.Join('&', query)}";
    }

    private static string FormatDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private const string DryDocksPath = "api/maintenance/dry-docks";

    internal async Task<MaintenanceApiResult<DryDocksPage>> GetDryDocksAsync(
        DryDockFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildDryDocksPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DryDocksPage>(
                    null, await ReadFailureAsync(response, "Unable to load dry docks", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<DryDocksPage>(cancellationToken);

            return new MaintenanceApiResult<DryDocksPage>(page, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DryDocksPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DryDocksPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<DryDockDetailsModel>> GetDryDockDetailsAsync(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{DryDocksPath}/{dryDockId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DryDockDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to load the dry dock", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<DryDockDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<DryDockDetailsModel>(details, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DryDockDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DryDockDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ScheduledDryDock>> ScheduleDryDockAsync(
        ScheduleDryDockWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(DryDocksPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<ScheduledDryDock>(
                    null, await ReadFailureAsync(response, "Unable to schedule the dry dock", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<ScheduledDryDock>(cancellationToken);

            return new MaintenanceApiResult<ScheduledDryDock>(created, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ScheduledDryDock>(null, Unavailable());
        }
    }

    internal Task<MaintenanceApiResult<DryDockTransition>> StartDryDockAsync(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        return PostDryDockActionAsync(dryDockId, "start", content: null, cancellationToken);
    }

    internal Task<MaintenanceApiResult<DryDockTransition>> CancelDryDockAsync(
        Guid dryDockId,
        CancellationToken cancellationToken)
    {
        return PostDryDockActionAsync(dryDockId, "cancel", content: null, cancellationToken);
    }

    internal Task<MaintenanceApiResult<DryDockTransition>> CompleteDryDockAsync(
        Guid dryDockId,
        CompleteDryDockWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return PostDryDockActionAsync(
            dryDockId,
            "complete",
            JsonContent.Create(request),
            cancellationToken);
    }

    private async Task<MaintenanceApiResult<DryDockTransition>> PostDryDockActionAsync(
        Guid dryDockId,
        string action,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                $"{DryDocksPath}/{dryDockId}/{action}",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DryDockTransition>(
                    null,
                    await ReadFailureAsync(response, "Unable to update the dry dock", cancellationToken));
            }

            var transition = await response.Content.ReadFromJsonAsync<DryDockTransition>(cancellationToken);

            return new MaintenanceApiResult<DryDockTransition>(transition, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DryDockTransition>(null, Unavailable());
        }
        finally
        {
            content?.Dispose();
        }
    }

    private static string BuildDryDocksPath(DryDockFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={filter.PageSize.ToString(CultureInfo.InvariantCulture)}",
        };

        if (filter.VesselId is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (filter.PortId is { } portId)
        {
            query.Add($"portId={portId}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.StartFrom is { } startFrom)
        {
            query.Add($"startFrom={startFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (filter.StartTo is { } startTo)
        {
            query.Add($"startTo={startTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (filter.Overdue is { } overdue)
        {
            query.Add($"overdue={(overdue ? "true" : "false")}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query.Add($"search={Uri.EscapeDataString(filter.Search.Trim())}");
        }

        return $"{DryDocksPath}?{string.Join('&', query)}";
    }
}
