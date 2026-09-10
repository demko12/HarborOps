using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class VesselPositions : IDisposable
{
    private const int PageSize = 50;

    private const int AttentionPreviewLimit = 6;

    private readonly CancellationTokenSource pageCancellation = new();
    private int loadVersion;

    private VesselPositionsWebModel? positions;
    private FleetDirectoryWebModel? fleets;
    private FleetApiFailure? failure;
    private bool isLoading = true;

    private Guid? selectedFleetId;
    private string? selectedStatus;
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

    private int MissingCount => positions?.Items.Count(vessel => !vessel.HasPosition) ?? 0;

    private int StaleCount => positions?.Items.Count(vessel => vessel.HasPosition && vessel.IsPositionStale) ?? 0;

    private int CurrentCount => positions?.Items.Count(vessel => !vessel.IsPositionStale) ?? 0;

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

    private async Task LoadPositionsAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
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

    private async Task ApplyFiltersAsync()
    {
        pageNumber = 1;
        await LoadPositionsAsync();
    }

    private async Task ResetFiltersAsync()
    {
        selectedFleetId = null;
        selectedStatus = null;
        staleOnly = false;
        await ApplyFiltersAsync();
    }

    private Task RefreshAsync() => LoadPositionsAsync();

    private async Task PreviousPageAsync()
    {
        if (pageNumber > 1)
        {
            pageNumber--;
            await LoadPositionsAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (pageNumber < TotalPages)
        {
            pageNumber++;
            await LoadPositionsAsync();
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

            await LoadPositionsAsync();
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
        pageCancellation.Cancel();
        pageCancellation.Dispose();
    }
}
