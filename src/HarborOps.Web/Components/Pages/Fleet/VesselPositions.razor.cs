using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class VesselPositions : IDisposable, IAsyncDisposable
{
    private const int PageSize = 50;

    private const int AttentionPreviewLimit = 6;

    private readonly CancellationTokenSource pageCancellation = new();
    private CancellationTokenSource? searchCancellation;
    private int loadVersion;
    private bool disposed;
    private bool autoRefresh = true;
    private bool checkingPositions;

    private VesselPositionsWebModel? positions;
    private IReadOnlyList<VesselPositionWebModel> mapVessels = [];
    private FleetApiFailure? mapFailure;
    private readonly string mapId = $"fleet-positions-map-{Guid.NewGuid():N}";
    private ElementReference mapElement;
    private bool mapInitialized;
    private bool mapUnavailable;
    private int MappedCount => mapVessels.Count(vessel => vessel.HasPosition);
    private FleetDirectoryWebModel? fleets;
    private FleetApiFailure? failure;
    private bool isLoading = true;

    private Guid? selectedFleetId;
    private string? selectedStatus;
    private string? search;
    private bool staleOnly;
    private int pageNumber = 1;

    private Guid? reportVesselId;
    private decimal? reportLatitude;
    private decimal? reportLongitude;
    private bool isSubmitting;
    private FleetApiFailure? submitFailure;
    private string? submitSuccessMessage;

    private bool CanManageFleet =>
        AuthorizationState.HasPermission(CurrentUserAuthorizationState.FleetManagePermission);

    // Same distinction as VesselDetails: reporting a defect is a Maintenance action, gated
    // by maintenance.manage, independent of the Fleet-specific CanManageFleet permission.
    private bool CanReportDefects => AuthorizationState.HasPermission("maintenance.manage");

    private int MissingCount => mapVessels.Count(vessel => !vessel.HasPosition);

    private int StaleCount => mapVessels.Count(vessel => vessel.HasPosition && vessel.IsPositionStale);

    private int CurrentCount => mapVessels.Count(vessel => vessel.HasPosition && !vessel.IsPositionStale);

    private string CoveragePercent(int count) => mapVessels.Count > 0
        ? (100m * count / mapVessels.Count).ToString("0.#", CultureInfo.InvariantCulture)
        : "0";

    private IReadOnlyList<VesselPositionWebModel> AttentionVessels =>
        positions?.Items.Where(vessel => vessel.IsPositionStale).Take(AttentionPreviewLimit).ToList() ?? [];

    private int AttentionCount => positions?.Items.Count(vessel => vessel.IsPositionStale) ?? 0;

    private string StaleAfterLabel => positions is null
        ? "the configured threshold"
        : $"{positions.StaleAfterHours.ToString("0.##", CultureInfo.InvariantCulture)} h";

    private int TotalPages => positions is null || positions.TotalCount == 0
        ? 1
        : (int)Math.Ceiling((double)positions.TotalCount / positions.PageSize);

    private int FirstRowNumber => positions is null || positions.TotalCount == 0
        ? 0
        : ((positions.PageNumber - 1) * positions.PageSize) + 1;

    private int LastRowNumber => positions is null
        ? 0
        : Math.Min(positions.PageNumber * positions.PageSize, positions.TotalCount);

    protected override async Task OnInitializedAsync()
    {
        await LoadFleetsAsync();
        await LoadPositionsAsync();
    }

    private async Task RefreshAutomaticallyAsync()
    {
        var cancellationToken = pageCancellation.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    if (disposed || !autoRefresh || isLoading || isSubmitting || reportVesselId is not null) return;
                    checkingPositions = true;
                    StateHasChanged();
                    try { await LoadPositionsAsync(true, true); }
                    finally
                    {
                        checkingPositions = false;
                        if (!disposed) StateHasChanged();
                    }
                });
            }
        }
        catch (OperationCanceledException) when (disposed) { }
    }

    private string AisStatusLabel => positions?.AisTracking?.ConnectionState switch
    {
        "Connected" => "AIS feed connected",
        "Connecting" => "Connecting to AIS feed",
        "Reconnecting" => "AIS feed reconnecting; showing last received positions",
        "NotConfigured" => "AIS tracking needs server configuration",
        "AwaitingMmsi" => "Add a valid MMSI to a vessel to start AIS tracking",
        "FleetLimitExceeded" => "AIS tracking supports up to 200 registered MMSIs",
        "Disconnected" => "AIS feed disconnected; showing last received positions",
        _ => "AIS tracking is disabled",
    };

    private async Task LoadFleetsAsync()
    {
        try
        {
            var result = await FleetApi.GetFleetsAsync(null, 1, 100, pageCancellation.Token);
            if (result.IsSuccess)
            {
                fleets = result.Value;
            }
        }
        catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
        {
            // The page is going away; the filter simply stays empty.
        }
    }

    private Task LoadPositionsAsync() => LoadPositionsAsync(true);

    private async Task LoadPositionsAsync(bool reloadMap, bool background = false)
    {
        var requestVersion = ++loadVersion;
        if (!background) isLoading = true;
        failure = null;

        FleetApiResult<VesselPositionsWebModel> result;
        try
        {
            result = await FleetApi.GetVesselPositionsAsync(
                selectedFleetId,
                selectedStatus,
                staleOnly,
                pageNumber,
                PageSize,
                search,
                pageCancellation.Token);
        }
        catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
        {
            return;
        }

        if (pageCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (result.IsSuccess && result.Value is not null)
        {
            positions = result.Value;
            pageNumber = positions.PageNumber;

            if (reloadMap)
            {
                await LoadMapVesselsAsync(positions, requestVersion);
                if (pageCancellation.IsCancellationRequested || requestVersion != loadVersion) return;
            }

            if (reportVesselId is { } selected
                && positions.Items.All(vessel => vessel.VesselId != selected))
            {
                reportVesselId = null;
            }
        }
        else
        {
            failure = result.Failure;
        }

        isLoading = false;
    }

    private async Task LoadMapVesselsAsync(VesselPositionsWebModel tablePage, int requestVersion)
    {
        mapUnavailable = false;
        mapFailure = null;
        if (tablePage.TotalCount <= tablePage.PageSize && tablePage.PageNumber == 1)
        {
            mapVessels = tablePage.Items;
            return;
        }

        var vessels = new List<VesselPositionWebModel>();
        var fleetId = selectedFleetId;
        var status = selectedStatus;
        var onlyStale = staleOnly;
        var searchText = search;
        var mapPage = 1;
        while (true)
        {
            FleetApiResult<VesselPositionsWebModel> result;
            try
            {
                result = await FleetApi.GetVesselPositionsAsync(
                    fleetId, status, onlyStale, mapPage, 100, searchText, pageCancellation.Token);
            }
            catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (pageCancellation.IsCancellationRequested || requestVersion != loadVersion) return;
            if (!result.IsSuccess || result.Value is not { } batch)
            {
                mapFailure = result.Failure;
                mapVessels = [];
                return;
            }

            vessels.AddRange(batch.Items);
            if (batch.Items.Count == 0 || mapPage * batch.PageSize >= batch.TotalCount) break;
            mapPage++;
        }

        mapVessels = vessels.DistinctBy(vessel => vessel.VesselId).ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pageCancellation.IsCancellationRequested) return;
        // Prerendered instances never reach this lifecycle method.
        if (firstRender) _ = RefreshAutomaticallyAsync();
        try
        {
            if (isLoading || failure is not null || mapFailure is not null || MappedCount == 0 || mapUnavailable)
            {
                if (mapInitialized)
                {
                    await JS.InvokeVoidAsync("fleetPositionsMap.dispose", mapId);
                    mapInitialized = false;
                }
                return;
            }

            mapInitialized = await JS.InvokeAsync<bool>("fleetPositionsMap.render", mapId, mapElement,
                mapVessels.Where(vessel => vessel.HasPosition).Select(vessel => new
                {
                    vessel.VesselId,
                    vessel.Name,
                    vessel.ImoNumber,
                    vessel.CurrentStatus,
                    vessel.FleetName,
                    Latitude = vessel.CurrentLatitude,
                    Longitude = vessel.CurrentLongitude,
                    vessel.LastPositionReportedAtUtc,
                    vessel.IsPositionStale,
                    vessel.PositionSource,
                    vessel.Mmsi,
                }));
            if (!mapInitialized)
            {
                mapUnavailable = true;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException)
        {
            mapUnavailable = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task FitMapAsync()
    {
        try { await JS.InvokeVoidAsync("fleetPositionsMap.fit", mapId); }
        catch (JSDisconnectedException) { }
        catch (JSException) { mapUnavailable = true; }
    }

    private void RetryMap() => mapUnavailable = false;

    public async ValueTask DisposeAsync()
    {
        Dispose();
        // Prerendering also disposes the component, before JS interop is available.
        if (!mapInitialized) return;
        mapInitialized = false;
        try { await JS.InvokeVoidAsync("fleetPositionsMap.dispose", mapId); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    private async Task ApplyFiltersAsync()
    {
        pageNumber = 1;
        await LoadPositionsAsync();
    }

    private async Task ResetFiltersAsync()
    {
        selectedFleetId = null;
        selectedStatus = null;
        search = null;
        staleOnly = false;
        await ApplyFiltersAsync();
    }

    private Task RefreshAsync() => LoadPositionsAsync();

    private async Task ScheduleSearchAsync()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(pageCancellation.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), searchCancellation.Token);
            pageNumber = 1;
            await LoadPositionsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void BeginPositionReport(VesselPositionWebModel vessel)
    {
        if (!CanManageFleet) return;
        reportVesselId = vessel.VesselId;
        reportLatitude = vessel.CurrentLatitude;
        reportLongitude = vessel.CurrentLongitude;
        submitFailure = null;
        submitSuccessMessage = null;
    }

    private void CancelPositionReport()
    {
        reportVesselId = null;
        reportLatitude = null;
        reportLongitude = null;
        submitFailure = null;
    }

    private async Task PreviousPageAsync()
    {
        if (pageNumber > 1)
        {
            pageNumber--;
            await LoadPositionsAsync(false);
        }
    }

    private async Task NextPageAsync()
    {
        if (pageNumber < TotalPages)
        {
            pageNumber++;
            await LoadPositionsAsync(false);
        }
    }

    private async Task SubmitPositionAsync()
    {
        if (isSubmitting || reportVesselId is not { } vesselId)
        {
            return;
        }

        isSubmitting = true;
        submitFailure = null;
        submitSuccessMessage = null;

        try
        {
            var request = new RecordVesselPositionWebRequest(reportLatitude, reportLongitude);

            FleetApiResult<RecordVesselPositionWebResponse> result;
            try
            {
                result = await FleetApi.RecordVesselPositionAsync(vesselId, request, pageCancellation.Token);
            }
            catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                submitFailure = result.Failure;
                return;
            }

            var vesselName = positions?.Items
                .FirstOrDefault(vessel => vessel.VesselId == vesselId)?.Name ?? "the vessel";

            submitSuccessMessage =
                $"Position recorded for {vesselName} at {FormatLatitude(result.Value.Latitude)}, {FormatLongitude(result.Value.Longitude)}.";
            reportLatitude = null;
            reportLongitude = null;

            // A successful report is current by definition. Keeping the "missing or stale"
            // filter active would immediately hide the vessel the operator has just updated.
            staleOnly = false;
            pageNumber = 1;

            await LoadPositionsAsync();
            reportVesselId = null;
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private static string DetailsUrl(Guid vesselId) =>
        $"/fleet/{vesselId}?returnTo={Uri.EscapeDataString("/fleet/positions")}";

    internal static string FormatLatitude(decimal? latitude) => latitude is null
        ? "—"
        : $"{Math.Abs(latitude.Value).ToString("0.0000", CultureInfo.InvariantCulture)}° {(latitude.Value < 0 ? "S" : "N")}";

    internal static string FormatLongitude(decimal? longitude) => longitude is null
        ? "—"
        : $"{Math.Abs(longitude.Value).ToString("0.0000", CultureInfo.InvariantCulture)}° {(longitude.Value < 0 ? "W" : "E")}";

    internal static string FormatTimestamp(DateTime? timestamp) => timestamp is null
        ? "Never reported"
        : timestamp.Value.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    internal static string FreshnessLabel(VesselPositionWebModel vessel) => vessel switch
    {
        { HasPosition: false } => "No position",
        { IsPositionStale: true } => "Stale",
        _ => "Current",
    };

    internal static string FreshnessClass(VesselPositionWebModel vessel) => vessel switch
    {
        { HasPosition: false } => "positions-badge-missing",
        { IsPositionStale: true } => "positions-badge-stale",
        _ => "positions-badge-current",
    };

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pageCancellation.Cancel();
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        pageCancellation.Dispose();
    }
}
