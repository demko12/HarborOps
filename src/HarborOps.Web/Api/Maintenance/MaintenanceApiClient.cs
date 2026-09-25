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

    private const string SuppliersPath = "api/maintenance/suppliers";

    private const string PurchaseOrdersPath = "api/maintenance/purchase-orders";

    internal async Task<MaintenanceApiResult<SuppliersPage>> GetSuppliersAsync(
        SupplierFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildSuppliersPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SuppliersPage>(
                    null, await ReadFailureAsync(response, "Unable to load suppliers", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<SuppliersPage>(cancellationToken);

            return new MaintenanceApiResult<SuppliersPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SuppliersPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SuppliersPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<RegisteredSupplier>> RegisterSupplierAsync(
        RegisterSupplierRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(SuppliersPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<RegisteredSupplier>(
                    null, await ReadFailureAsync(response, "Unable to register the supplier", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<RegisteredSupplier>(cancellationToken);

            return new MaintenanceApiResult<RegisteredSupplier>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<RegisteredSupplier>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<RegisteredSupplier>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PurchaseOrdersPage>> GetPurchaseOrdersAsync(
        PurchaseOrderFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildPurchaseOrdersPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PurchaseOrdersPage>(
                    null, await ReadFailureAsync(response, "Unable to load purchase orders", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<PurchaseOrdersPage>(cancellationToken);

            return new MaintenanceApiResult<PurchaseOrdersPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PurchaseOrdersPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PurchaseOrdersPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PurchaseOrderDetails>> GetPurchaseOrderDetailsAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{PurchaseOrdersPath}/{purchaseOrderId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PurchaseOrderDetails>(
                    null, await ReadFailureAsync(response, "Unable to load the purchase order", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<PurchaseOrderDetails>(cancellationToken);

            return new MaintenanceApiResult<PurchaseOrderDetails>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PurchaseOrderDetails>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PurchaseOrderDetails>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<CreatedPurchaseOrder>> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(PurchaseOrdersPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<CreatedPurchaseOrder>(
                    null, await ReadFailureAsync(response, "Unable to create the purchase order", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<CreatedPurchaseOrder>(cancellationToken);

            return new MaintenanceApiResult<CreatedPurchaseOrder>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<CreatedPurchaseOrder>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<CreatedPurchaseOrder>(null, Unavailable());
        }
    }

    internal Task<MaintenanceApiResult<ChangePurchaseOrderStatusResult>> MarkPurchaseOrderOrderedAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangePurchaseOrderStatusAsync(
            purchaseOrderId, "order", "Unable to mark the purchase order as ordered", cancellationToken);
    }

    internal Task<MaintenanceApiResult<ChangePurchaseOrderStatusResult>> ReceivePurchaseOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangePurchaseOrderStatusAsync(
            purchaseOrderId, "receive", "Unable to receive the purchase order", cancellationToken);
    }

    internal Task<MaintenanceApiResult<ChangePurchaseOrderStatusResult>> CancelPurchaseOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangePurchaseOrderStatusAsync(
            purchaseOrderId, "cancel", "Unable to cancel the purchase order", cancellationToken);
    }
    private async Task<MaintenanceApiResult<ChangePurchaseOrderStatusResult>> ChangePurchaseOrderStatusAsync(
        Guid purchaseOrderId,
        string action,
        string defaultTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                $"{PurchaseOrdersPath}/{purchaseOrderId}/{action}",
                content: null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ChangePurchaseOrderStatusResult>(
                    null, await ReadFailureAsync(response, defaultTitle, cancellationToken));
            }

            var result = await response.Content
                .ReadFromJsonAsync<ChangePurchaseOrderStatusResult>(cancellationToken);

            return new MaintenanceApiResult<ChangePurchaseOrderStatusResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ChangePurchaseOrderStatusResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ChangePurchaseOrderStatusResult>(null, Unavailable());
        }
    }
    private static string BuildSuppliersPath(SupplierFilter filter)
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

        return $"{SuppliersPath}?{string.Join('&', query)}";
    }

    private static string BuildPurchaseOrdersPath(PurchaseOrderFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (filter.SupplierId is { } supplierId)
        {
            query.Add($"supplierId={supplierId}");
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.OrderDateFrom is { } orderDateFrom)
        {
            query.Add($"orderDateFrom={FormatDate(orderDateFrom)}");
        }

        if (filter.OrderDateTo is { } orderDateTo)
        {
            query.Add($"orderDateTo={FormatDate(orderDateTo)}");
        }

        if (filter.ExpectedDeliveryFrom is { } deliveryFrom)
        {
            query.Add($"expectedDeliveryFrom={FormatDate(deliveryFrom)}");
        }

        if (filter.ExpectedDeliveryTo is { } deliveryTo)
        {
            query.Add($"expectedDeliveryTo={FormatDate(deliveryTo)}");
        }

        if (filter.Overdue is { } overdue)
        {
            query.Add($"overdue={(overdue ? "true" : "false")}");
        }

        return $"{PurchaseOrdersPath}?{string.Join('&', query)}";
    }

    internal async Task<MaintenanceApiResult<MaintenanceWorkOrderDetailsModel>> GetWorkOrderDetailsAsync(
    Guid workOrderId,
    CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/{workOrderId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<MaintenanceWorkOrderDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to load the work order", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrderDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<MaintenanceWorkOrderDetailsModel>(details, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrderDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrderDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ReplaceWorkOrderEquipmentResult>> ReplaceWorkOrderEquipmentAsync(
        Guid workOrderId,
        ReplaceWorkOrderEquipmentWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PutAsJsonAsync(
                $"{BasePath}/{workOrderId}/equipment",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ReplaceWorkOrderEquipmentResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to update the equipment scope", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderEquipmentResult>(
                cancellationToken);

            return new MaintenanceApiResult<ReplaceWorkOrderEquipmentResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ReplaceWorkOrderEquipmentResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ReplaceWorkOrderEquipmentResult>(null, Unavailable());
        }
    }

    internal Task<MaintenanceApiResult<MaintenanceWorkOrderTransition>> StartWorkOrderAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        return PostWorkOrderActionAsync(workOrderId, "start", cancellationToken);
    }

    internal Task<MaintenanceApiResult<MaintenanceWorkOrderTransition>> CancelWorkOrderAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        return PostWorkOrderActionAsync(workOrderId, "cancel", cancellationToken);
    }

    private async Task<MaintenanceApiResult<MaintenanceWorkOrderTransition>> PostWorkOrderActionAsync(
        Guid workOrderId,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                $"{BasePath}/{workOrderId}/{action}",
                content: null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<MaintenanceWorkOrderTransition>(
                    null,
                    await ReadFailureAsync(response, "Unable to update the work order", cancellationToken));
            }

            var transition = await response.Content.ReadFromJsonAsync<MaintenanceWorkOrderTransition>(
                cancellationToken);

            return new MaintenanceApiResult<MaintenanceWorkOrderTransition>(transition, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrderTransition>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<MaintenanceWorkOrderTransition>(null, Unavailable());
        }
    }

    private const string InventoryReplenishmentPath = "api/maintenance/inventory/replenishment";

    internal async Task<MaintenanceApiResult<WorkOrderPartReservationsResult>> GetWorkOrderPartReservationsAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{BasePath}/{workOrderId}/part-reservations",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<WorkOrderPartReservationsResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to load the part reservations", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<WorkOrderPartReservationsResult>(
                cancellationToken);

            return new MaintenanceApiResult<WorkOrderPartReservationsResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<WorkOrderPartReservationsResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<WorkOrderPartReservationsResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ReplaceWorkOrderPartReservationsResult>> ReplaceWorkOrderPartReservationsAsync(
        Guid workOrderId,
        ReplaceWorkOrderPartReservationsWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PutAsJsonAsync(
                $"{BasePath}/{workOrderId}/part-reservations",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ReplaceWorkOrderPartReservationsResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to update the part reservations", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderPartReservationsResult>(
                cancellationToken);

            return new MaintenanceApiResult<ReplaceWorkOrderPartReservationsResult>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ReplaceWorkOrderPartReservationsResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ReplaceWorkOrderPartReservationsResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ReleasedWorkOrderPartReservations>> ReleaseWorkOrderPartReservationsAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                $"{BasePath}/{workOrderId}/part-reservations/release",
                content: null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ReleasedWorkOrderPartReservations>(
                    null,
                    await ReadFailureAsync(response, "Unable to release the part reservations", cancellationToken));
            }

            return new MaintenanceApiResult<ReleasedWorkOrderPartReservations>(
                new ReleasedWorkOrderPartReservations(workOrderId),
                null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ReleasedWorkOrderPartReservations>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ReleasedWorkOrderPartReservations>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<InventoryReplenishmentPage>> GetInventoryReplenishmentAsync(
        InventoryReplenishmentFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                $"{InventoryReplenishmentPath}?pageNumber={filter.PageNumber.ToString(CultureInfo.InvariantCulture)}&pageSize={filter.PageSize.ToString(CultureInfo.InvariantCulture)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<InventoryReplenishmentPage>(
                    null,
                    await ReadFailureAsync(response, "Unable to load the replenishment board", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<InventoryReplenishmentPage>(cancellationToken);

            return new MaintenanceApiResult<InventoryReplenishmentPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<InventoryReplenishmentPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<InventoryReplenishmentPage>(null, Unavailable());
        }
    }

    private const string DefectsPath = "api/maintenance/defects";

    internal async Task<MaintenanceApiResult<DefectsPage>> GetDefectsAsync(
        DefectFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildDefectsPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DefectsPage>(
                    null, await ReadFailureAsync(response, "Unable to load defects", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<DefectsPage>(cancellationToken);

            return new MaintenanceApiResult<DefectsPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DefectsPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DefectsPage>(null, Unavailable());
        }
    }

    private static string BuildDefectsPath(DefectFilter filter)
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

        if (!string.IsNullOrWhiteSpace(filter.Severity))
        {
            query.Add($"severity={Uri.EscapeDataString(filter.Severity)}");
        }

        return $"{DefectsPath}?{string.Join('&', query)}";
    }

    internal async Task<MaintenanceApiResult<DefectDetailsModel>> RegisterDefectAsync(
        RegisterDefectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(DefectsPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<DefectDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to register the defect", cancellationToken));
            }

            var created = await response.Content.ReadFromJsonAsync<DefectDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<DefectDetailsModel>(created, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<DefectDetailsModel>> GetDefectAsync(
        Guid defectId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{DefectsPath}/{defectId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DefectDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to load the defect", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<DefectDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<DefectDetailsModel>(details, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<DefectDetailsModel>> TriageDefectAsync(
        Guid defectId,
        TriageDefectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{DefectsPath}/{defectId}/triage", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DefectDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to triage the defect", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<DefectDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<DefectDetailsModel>(details, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<DefectDetailsModel>> RejectDefectAsync(
        Guid defectId,
        RejectDefectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{DefectsPath}/{defectId}/reject", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<DefectDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to reject the defect", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<DefectDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<DefectDetailsModel>(details, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<DefectDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ConvertDefectToWorkOrderResponse>> ConvertDefectToWorkOrderAsync(
        Guid defectId,
        ConvertDefectToWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{DefectsPath}/{defectId}/convert-to-work-order", request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<ConvertDefectToWorkOrderResponse>(
                    null, await ReadFailureAsync(response, "Unable to convert the defect", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<ConvertDefectToWorkOrderResponse>(cancellationToken);

            return new MaintenanceApiResult<ConvertDefectToWorkOrderResponse>(result, null);
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ConvertDefectToWorkOrderResponse>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ConvertDefectToWorkOrderResponse>(null, Unavailable());
        }
    }

    private const string PreventiveWorkPath = "api/maintenance/preventive-work";

    internal async Task<MaintenanceApiResult<PreventiveWorkForecastPage>> GetPreventiveWorkForecastAsync(
        PreventiveWorkForecastFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildPreventiveWorkForecastPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PreventiveWorkForecastPage>(
                    null,
                    await ReadFailureAsync(response, "Unable to load the preventive work forecast", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<PreventiveWorkForecastPage>(cancellationToken);

            return new MaintenanceApiResult<PreventiveWorkForecastPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PreventiveWorkForecastPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PreventiveWorkForecastPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PreventiveWorkPreview>> PreviewPreventiveWorkAsync(
        GeneratePreventiveWorkWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{PreventiveWorkPath}/generate/preview", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PreventiveWorkPreview>(
                    null,
                    await ReadFailureAsync(response, "Unable to preview preventive work", cancellationToken));
            }

            var preview = await response.Content.ReadFromJsonAsync<PreventiveWorkPreview>(cancellationToken);

            return new MaintenanceApiResult<PreventiveWorkPreview>(preview, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PreventiveWorkPreview>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PreventiveWorkPreview>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PreventiveWorkGenerationResult>> GeneratePreventiveWorkAsync(
        GeneratePreventiveWorkWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{PreventiveWorkPath}/generate", request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<PreventiveWorkGenerationResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to generate preventive work orders", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<PreventiveWorkGenerationResult>(cancellationToken);

            return new MaintenanceApiResult<PreventiveWorkGenerationResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PreventiveWorkGenerationResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PreventiveWorkGenerationResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PreventiveWorkReplacement>> ReplacePreventiveWorkOrderAsync(
        Guid occurrenceId,
        GeneratePreventiveWorkWebRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{PreventiveWorkPath}/occurrences/{occurrenceId}/replacement", request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<PreventiveWorkReplacement>(
                    null,
                    await ReadFailureAsync(response, "Unable to create the replacement work order", cancellationToken));
            }

            var replacement = await response.Content.ReadFromJsonAsync<PreventiveWorkReplacement>(cancellationToken);

            return new MaintenanceApiResult<PreventiveWorkReplacement>(replacement, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PreventiveWorkReplacement>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PreventiveWorkReplacement>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ScheduleOccurrencesResult>> GetScheduleOccurrencesAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{SchedulesPath}/{scheduleId}/occurrences",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ScheduleOccurrencesResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to load the schedule occurrences", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<ScheduleOccurrencesResult>(cancellationToken);

            return new MaintenanceApiResult<ScheduleOccurrencesResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ScheduleOccurrencesResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ScheduleOccurrencesResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<ScheduleWorkOrdersResult>> GetScheduleWorkOrdersAsync(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{SchedulesPath}/{scheduleId}/work-orders",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<ScheduleWorkOrdersResult>(
                    null,
                    await ReadFailureAsync(response, "Unable to load the generated work orders", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<ScheduleWorkOrdersResult>(cancellationToken);

            return new MaintenanceApiResult<ScheduleWorkOrdersResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<ScheduleWorkOrdersResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<ScheduleWorkOrdersResult>(null, Unavailable());
        }
    }

    private static string BuildPreventiveWorkForecastPath(PreventiveWorkForecastFilter filter)
    {
        var query = new List<string>
        {
            $"from={FormatDate(filter.From)}",
            $"to={FormatDate(filter.To)}",
            $"pageNumber={filter.PageNumber}",
            $"pageSize={filter.PageSize}",
        };

        if (filter.VesselId is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(filter.State))
        {
            query.Add($"state={Uri.EscapeDataString(filter.State)}");
        }

        return $"{PreventiveWorkPath}/forecast?{string.Join('&', query)}";
    }

    private const string StocktakesPath = "api/maintenance/stocktakes";

    internal async Task<MaintenanceApiResult<StocktakesPage>> GetStocktakesAsync(
        StocktakeFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildStocktakesPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakesPage>(
                    null, await ReadFailureAsync(response, "Unable to load stocktakes", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<StocktakesPage>(cancellationToken);

            return new MaintenanceApiResult<StocktakesPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakesPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakesPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakeDetailsModel>> GetStocktakeAsync(
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{StocktakesPath}/{stocktakeId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakeDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to load the stocktake", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<StocktakeDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakeDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakeDetailsModel>> CreateStocktakeAsync(
        CreateStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(StocktakesPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<StocktakeDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to create the stocktake", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<StocktakeDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakeDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakeDetailsModel>> UpdateStocktakeCountsAsync(
        Guid stocktakeId,
        UpdateStocktakeCountsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PutAsJsonAsync(
                $"{StocktakesPath}/{stocktakeId}/counts",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakeDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to save the counts", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<StocktakeDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakeDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakeDetailsModel>> RefreshStocktakeLinesAsync(
        Guid stocktakeId,
        RefreshStocktakeLinesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{StocktakesPath}/{stocktakeId}/refresh",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakeDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to refresh the baseline", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<StocktakeDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakeDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakePostingPreviewModel>> GetStocktakePreviewAsync(
        Guid stocktakeId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{StocktakesPath}/{stocktakeId}/preview",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakePostingPreviewModel>(
                    null, await ReadFailureAsync(response, "Unable to load the posting preview", cancellationToken));
            }

            var preview = await response.Content.ReadFromJsonAsync<StocktakePostingPreviewModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakePostingPreviewModel>(preview, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakePostingPreviewModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakePostingPreviewModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PostStocktakeResult>> PostStocktakeAsync(
        Guid stocktakeId,
        PostStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{StocktakesPath}/{stocktakeId}/post",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PostStocktakeResult>(
                    null, await ReadFailureAsync(response, "Unable to post the stocktake", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<PostStocktakeResult>(cancellationToken);

            return new MaintenanceApiResult<PostStocktakeResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PostStocktakeResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PostStocktakeResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<StocktakeDetailsModel>> CancelStocktakeAsync(
        Guid stocktakeId,
        CancelStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{StocktakesPath}/{stocktakeId}/cancel",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<StocktakeDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to cancel the stocktake", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<StocktakeDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<StocktakeDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<StocktakeDetailsModel>(null, Unavailable());
        }
    }

    private static string BuildStocktakesPath(StocktakeFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={filter.PageSize.ToString(CultureInfo.InvariantCulture)}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        }

        return $"{StocktakesPath}?{string.Join('&', query)}";
    }

    private const string SupplierReturnsPath = "api/maintenance/supplier-returns";

    internal async Task<MaintenanceApiResult<SupplierReturnsPage>> GetSupplierReturnsAsync(
        SupplierReturnFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        try
        {
            using var response = await httpClient.GetAsync(
                BuildSupplierReturnsPath(filter),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SupplierReturnsPage>(
                    null, await ReadFailureAsync(response, "Unable to load supplier returns", cancellationToken));
            }

            var page = await response.Content.ReadFromJsonAsync<SupplierReturnsPage>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnsPage>(page, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnsPage>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnsPage>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<SupplierReturnDetailsModel>> GetSupplierReturnAsync(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{SupplierReturnsPath}/{supplierReturnId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SupplierReturnDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to load the supplier return", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<SupplierReturnDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<SupplierReturnDetailsModel>> CreateSupplierReturnAsync(
        CreateSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(SupplierReturnsPath, request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new MaintenanceApiResult<SupplierReturnDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to create the supplier return", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<SupplierReturnDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<SupplierReturnDetailsModel>> UpdateSupplierReturnAsync(
        Guid supplierReturnId,
        UpdateSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PutAsJsonAsync(
                $"{SupplierReturnsPath}/{supplierReturnId}",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SupplierReturnDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to save the supplier return", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<SupplierReturnDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<SupplierReturnPostingPreviewModel>> GetSupplierReturnPreviewAsync(
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{SupplierReturnsPath}/{supplierReturnId}/preview",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SupplierReturnPostingPreviewModel>(
                    null, await ReadFailureAsync(response, "Unable to load the posting preview", cancellationToken));
            }

            var preview = await response.Content.ReadFromJsonAsync<SupplierReturnPostingPreviewModel>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnPostingPreviewModel>(preview, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnPostingPreviewModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnPostingPreviewModel>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<PostSupplierReturnResult>> PostSupplierReturnAsync(
        Guid supplierReturnId,
        PostSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{SupplierReturnsPath}/{supplierReturnId}/post",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<PostSupplierReturnResult>(
                    null, await ReadFailureAsync(response, "Unable to post the supplier return", cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<PostSupplierReturnResult>(cancellationToken);

            return new MaintenanceApiResult<PostSupplierReturnResult>(result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<PostSupplierReturnResult>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<PostSupplierReturnResult>(null, Unavailable());
        }
    }

    internal async Task<MaintenanceApiResult<SupplierReturnDetailsModel>> CancelSupplierReturnAsync(
        Guid supplierReturnId,
        CancelSupplierReturnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"{SupplierReturnsPath}/{supplierReturnId}/cancel",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaintenanceApiResult<SupplierReturnDetailsModel>(
                    null, await ReadFailureAsync(response, "Unable to cancel the supplier return", cancellationToken));
            }

            var details = await response.Content.ReadFromJsonAsync<SupplierReturnDetailsModel>(cancellationToken);

            return new MaintenanceApiResult<SupplierReturnDetailsModel>(details, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MaintenanceApiResult<SupplierReturnDetailsModel>(null, Unavailable());
        }
    }

    private static string BuildSupplierReturnsPath(SupplierReturnFilter filter)
    {
        var query = new List<string>
        {
            $"pageNumber={filter.PageNumber.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={filter.PageSize.ToString(CultureInfo.InvariantCulture)}",
        };

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query.Add($"status={Uri.EscapeDataString(filter.Status)}");
        }

        if (filter.PurchaseOrderId is { } purchaseOrderId)
        {
            query.Add($"purchaseOrderId={purchaseOrderId}");
        }

        return $"{SupplierReturnsPath}?{string.Join('&', query)}";
    }
}
