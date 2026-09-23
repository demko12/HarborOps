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

    internal Task<VoyagesApiResult<VoyagePage>> GetDelayedVoyageCountAsync(
        DateOnly from,
        DateOnly to,
        string? search,
        CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{BasePath}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&search={Uri.EscapeDataString(search ?? string.Empty)}&includeActive=true&delayImpactApplied=true&pageNumber=1&pageSize=1");

        return GetAsync<VoyagePage>(path, "Unable to load delayed voyages", cancellationToken);
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

    internal Task<VoyagesApiResult<VoyageItinerary>> GetItineraryAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        return GetAsync<VoyageItinerary>(
            $"{BasePath}/{voyageId}/itinerary",
            "Unable to load the voyage itinerary",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PlanVoyageItineraryResult>> PlanItineraryAsync(
        Guid voyageId,
        PlanVoyageItineraryWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PlanVoyageItineraryResult>(
            $"{BasePath}/{voyageId}/itinerary",
            request,
            "Unable to plan the voyage itinerary",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<AssignBerthResult>> AssignBerthAsync(
        Guid voyageId,
        Guid stopId,
        AssignBerthWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<AssignBerthResult>(
            $"{BasePath}/{voyageId}/stops/{stopId}/berth",
            request,
            "Unable to assign the berth",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<BerthAvailability>> GetBerthAvailabilityAsync(
        Guid portId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture,
            $"{BasePath}/ports/{portId}/berth-availability?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture))}");
        return GetAsync<BerthAvailability>(path, "Unable to find berth availability", cancellationToken);
    }

    internal Task<VoyagesApiResult<ReplacedBerthAllocation>> ReplaceBerthAllocationAsync(
        Guid voyageId, Guid stopId, ReplaceBerthAllocationWebRequest request, CancellationToken cancellationToken)
        => SendAsync<ReplacedBerthAllocation>(HttpMethod.Put, $"{BasePath}/{voyageId}/stops/{stopId}/berth-allocation", request, "Unable to reschedule the berth allocation", cancellationToken);

    internal Task<VoyagesApiResult<BerthAllocationHistory>> GetBerthAllocationHistoryAsync(
        Guid voyageId, Guid stopId, CancellationToken cancellationToken)
        => GetAsync<BerthAllocationHistory>($"{BasePath}/{voyageId}/stops/{stopId}/berth-allocation-history", "Unable to load berth allocation history", cancellationToken);

    internal Task<VoyagesApiResult<StopTransition>> RecordStopArrivalAsync(
        Guid voyageId,
        Guid stopId,
        RecordStopArrivalWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<StopTransition>(
            $"{BasePath}/{voyageId}/stops/{stopId}/arrival",
            request,
            "Unable to record the stop arrival",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<StopTransition>> RecordStopDepartureAsync(
        Guid voyageId,
        Guid stopId,
        RecordStopDepartureWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<StopTransition>(
            $"{BasePath}/{voyageId}/stops/{stopId}/departure",
            request,
            "Unable to record the stop departure",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallClearance>> GetPortCallClearanceAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PortCallClearance>(
            ClearancePath(voyageId, stopId),
            "Unable to load the port-call clearance",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<AddedClearanceItem>> AddClearanceItemAsync(
        Guid voyageId,
        Guid stopId,
        AddClearanceItemWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<AddedClearanceItem>(
            $"{ClearancePath(voyageId, stopId)}/items",
            request,
            "Unable to add the clearance item",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<ClearanceItemSaved>> UpdateClearanceItemAsync(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        UpdateClearanceItemWebRequest request,
        CancellationToken cancellationToken)
    {
        return SendNoContentAsync(
            HttpMethod.Put,
            $"{ClearancePath(voyageId, stopId)}/items/{itemId}",
            request,
            "Unable to update the clearance item",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<ClearanceItemDecision>> ClearClearanceItemAsync(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        ClearClearanceItemWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ClearanceItemDecision>(
            $"{ClearancePath(voyageId, stopId)}/items/{itemId}/clear",
            request,
            "Unable to clear the item",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<ClearanceItemDecision>> WaiveClearanceItemAsync(
        Guid voyageId,
        Guid stopId,
        Guid itemId,
        WaiveClearanceItemWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ClearanceItemDecision>(
            $"{ClearancePath(voyageId, stopId)}/items/{itemId}/waive",
            request,
            "Unable to waive the item",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<ClearanceStatusChange>> CompleteClearanceAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return PostAsync<ClearanceStatusChange>(
            $"{ClearancePath(voyageId, stopId)}/complete",
            new { },
            "Unable to complete the clearance",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<ClearanceStatusChange>> ReopenClearanceAsync(
        Guid voyageId,
        Guid stopId,
        ReopenClearanceWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<ClearanceStatusChange>(
            $"{ClearancePath(voyageId, stopId)}/reopen",
            request,
            "Unable to reopen the clearance",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallTimeline>> GetPortCallTimelineAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PortCallTimeline>(
            $"{StopPath(voyageId, stopId)}/activity-timeline",
            "Unable to load the port-call activity log",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallActivityDetails>> GetPortCallActivityAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PortCallActivityDetails>(
            $"{StopPath(voyageId, stopId)}/activities/{activityId}",
            "Unable to load the activity history",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallActivityChange>> RecordPortCallActivityAsync(
        Guid voyageId,
        Guid stopId,
        RecordPortCallActivityWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PortCallActivityChange>(
            $"{StopPath(voyageId, stopId)}/activities",
            request,
            "Unable to record the activity",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallActivityChange>> CorrectPortCallActivityAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        CorrectPortCallActivityWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PortCallActivityChange>(
            $"{StopPath(voyageId, stopId)}/activities/{activityId}/correct",
            request,
            "Unable to correct the activity",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallActivityChange>> VoidPortCallActivityAsync(
        Guid voyageId,
        Guid stopId,
        Guid activityId,
        VoidPortCallActivityWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PortCallActivityChange>(
            $"{StopPath(voyageId, stopId)}/activities/{activityId}/void",
            request,
            "Unable to void the activity",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallStatementModel>> GetPortCallStatementAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PortCallStatementModel>(
            $"{StopPath(voyageId, stopId)}/statement",
            "Unable to load the port-call statement",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallStatementModel>> CreatePortCallStatementAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return PostAsync<PortCallStatementModel>(
            $"{StopPath(voyageId, stopId)}/statement",
            new { },
            "Unable to start the port-call statement",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallStatementModel>> FinalizePortCallStatementAsync(
        Guid voyageId,
        Guid stopId,
        FinalizePortCallStatementWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<PortCallStatementModel>(
            $"{StopPath(voyageId, stopId)}/statement/finalize",
            request,
            "Unable to finalize the port-call statement",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<PortCallStatementModel>> GetPortCallStatementPrintAsync(
        Guid voyageId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        return GetAsync<PortCallStatementModel>(
            $"{StopPath(voyageId, stopId)}/statement/print",
            "Unable to load the printable statement",
            cancellationToken);
    }

    private static string StopPath(Guid voyageId, Guid stopId) =>
        $"{BasePath}/{voyageId}/stops/{stopId}";

    private static string ClearancePath(Guid voyageId, Guid stopId) =>
        $"{BasePath}/{voyageId}/stops/{stopId}/clearance";

    private async Task<VoyagesApiResult<ClearanceItemSaved>> SendNoContentAsync(
        HttpMethod method,
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(method, path)
            {
                Content = JsonContent.Create(request),
            };
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new VoyagesApiResult<ClearanceItemSaved>(new ClearanceItemSaved(), null);
            }

            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken);
            }
            catch (JsonException)
            {
            }

            return new VoyagesApiResult<ClearanceItemSaved>(default, new VoyagesApiFailure(
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
            return new VoyagesApiResult<ClearanceItemSaved>(default, new VoyagesApiFailure(
                0,
                "HarborOps API is unavailable",
                "The voyages board could not reach the API.",
                NoErrors));
        }
    }

    internal Task<VoyagesApiResult<RecordedDelay>> RecordDelayAsync(
        Guid voyageId,
        RecordDelayWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<RecordedDelay>(
            $"{BasePath}/{voyageId}/delays",
            request,
            "Unable to record the delay",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<DelayImpactPreview>> PreviewDelayImpactAsync(
        Guid voyageId,
        Guid delayId,
        CancellationToken cancellationToken)
    {
        return PostAsync<DelayImpactPreview>(
            $"{BasePath}/{voyageId}/delays/{delayId}/impact-preview",
            new { },
            "Unable to assess the delay impact",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageEstimateRevisionModel>> ApplyDelayImpactAsync(
        Guid voyageId,
        Guid delayId,
        ApplyDelayImpactWebRequest request,
        CancellationToken cancellationToken)
    {
        return PostAsync<VoyageEstimateRevisionModel>(
            $"{BasePath}/{voyageId}/delays/{delayId}/apply-impact",
            request,
            "Unable to publish the revised estimates",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageEstimateRevisions>> GetEstimateRevisionsAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        return GetAsync<VoyageEstimateRevisions>(
            $"{BasePath}/{voyageId}/estimate-revisions",
            "Unable to load the estimate revisions",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<EstimateRevisionAlert>> RetryEstimateRevisionAlertAsync(
        Guid voyageId,
        Guid revisionBatchId,
        CancellationToken cancellationToken)
    {
        return PostAsync<EstimateRevisionAlert>(
            $"{BasePath}/{voyageId}/estimate-revisions/{revisionBatchId}/alert",
            new { },
            "Unable to resend the operational alert",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageSchedule>> GetScheduleChangesAsync(
        Guid voyageId,
        CancellationToken cancellationToken)
    {
        return GetAsync<VoyageSchedule>(
            $"{BasePath}/{voyageId}/schedule-changes",
            "Unable to load the schedule changes",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageSchedule>> AmendScheduleAsync(
        Guid voyageId,
        AmendVoyageScheduleWebRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<VoyageSchedule>(
            HttpMethod.Put,
            $"{BasePath}/{voyageId}/schedule",
            request,
            "Unable to amend the voyage schedule",
            cancellationToken);
    }

    internal Task<VoyagesApiResult<VoyageSchedule>> CancelVoyageAsync(
        Guid voyageId,
        CancelVoyageWebRequest request,
        CancellationToken cancellationToken)
    {
        return SendAsync<VoyageSchedule>(
            HttpMethod.Post,
            $"{BasePath}/{voyageId}/cancel",
            request,
            "Unable to cancel the voyage",
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

    private Task<VoyagesApiResult<T>> PostAsync<T>(
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        return SendAsync<T>(HttpMethod.Post, path, request, failureTitle, cancellationToken);
    }

    private async Task<VoyagesApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object request,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(method, path)
            {
                Content = JsonContent.Create(request),
            };
            using var response = await httpClient.SendAsync(message, cancellationToken);

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
