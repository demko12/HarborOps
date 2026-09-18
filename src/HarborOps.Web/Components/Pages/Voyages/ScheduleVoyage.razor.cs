using System.Globalization;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class ScheduleVoyage
{
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const int PortCodeLength = 5;

    private const int VesselResultLimit = 25;

    private CancellationTokenSource pageCancellation = new();

    private string voyageNumber = string.Empty;
    private string departurePortCode = string.Empty;
    private string arrivalPortCode = string.Empty;
    private DateOnly plannedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    private TimeOnly plannedDepartureTime = new(8, 0);
    private DateOnly plannedArrivalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4);
    private TimeOnly plannedArrivalTime = new(17, 0);

    private string? vesselSearch;
    private IReadOnlyList<VesselListItemResponse> vesselResults = [];
    private VesselListItemResponse? selectedVessel;
    private FleetApiFailure? vesselFailure;
    private VoyagesApiFailure? failure;
    private bool isSearchingVessels;
    private bool isSubmitting;

    private int highlightedVessel = -1;
    private int vesselSearchVersion;

    private bool isVesselDropdownOpen;
    private ElementReference pageElement;
    private DotNetObjectReference<ScheduleVoyage>? outsideClickReference;

    private bool CanManageVoyages => AuthorizationState.HasPermission(ManageVoyagesPermission);

    private string DeparturePortCode
    {
        get => departurePortCode;
        set => departurePortCode = value.ToUpperInvariant();
    }

    private string ArrivalPortCode
    {
        get => arrivalPortCode;
        set => arrivalPortCode = value.ToUpperInvariant();
    }

    private DateTime PlannedDepartureUtc => CombineUtc(plannedDepartureDate, plannedDepartureTime);

    private DateTime PlannedArrivalUtc => CombineUtc(plannedArrivalDate, plannedArrivalTime);

    internal static string FormatDuration(TimeSpan duration)
    {
        var days = (int)duration.TotalDays;

        return days > 0
            ? $"{days} d {duration.Hours} h"
            : $"{duration.Hours} h {duration.Minutes} min";
    }

    internal static string DescribeRoute(string departurePortCode, string arrivalPortCode)
    {
        var departure = string.IsNullOrWhiteSpace(departurePortCode) ? "?" : departurePortCode;
        var arrival = string.IsNullOrWhiteSpace(arrivalPortCode) ? "?" : arrivalPortCode;

        return $"{departure} → {arrival}";
    }

    internal static DateTime CombineUtc(DateOnly date, TimeOnly time)
    {
        return DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
    }

    internal static string FormatIsoDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            outsideClickReference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("outsideClickInterop.register", pageElement, outsideClickReference);
        }
    }

    [JSInvokable]
    public void CloseVesselDropdowns()
    {
        isVesselDropdownOpen = false;
    }

    private Task ShowVesselOptionsAsync()
    {
        isVesselDropdownOpen = true;

        return vesselResults.Count > 0
            ? Task.CompletedTask
            : LoadVesselsAsync(vesselSearch);
    }

    private Task SearchVesselsAsync()
    {
        isVesselDropdownOpen = true;

        return LoadVesselsAsync(vesselSearch);
    }

    private async Task LoadVesselsAsync(string? search)
    {
        var requestVersion = ++vesselSearchVersion;

        isSearchingVessels = true;
        vesselFailure = null;

        var result = await FleetApi.GetVesselsAsync(
            search,
            null,
            1,
            VesselResultLimit,
            pageCancellation.Token);

        if (pageCancellation.IsCancellationRequested || requestVersion != vesselSearchVersion)
        {
            return;
        }

        isSearchingVessels = false;
        highlightedVessel = -1;

        if (!result.IsSuccess)
        {
            vesselFailure = result.Failure;
            vesselResults = [];

            return;
        }

        vesselResults = result.Value!.Items;
    }

    private void SelectVessel(VesselListItemResponse vessel)
    {
        selectedVessel = vessel;
        vesselResults = [];
        vesselSearch = null;
        highlightedVessel = -1;
        isVesselDropdownOpen = false;
    }

    internal static string VesselOptionId(int index)
    {
        return string.Create(CultureInfo.InvariantCulture, $"schedule-vessel-option-{index}");
    }

    private async Task HandleVesselKeyDown(KeyboardEventArgs args)
    {
        if (vesselResults.Count == 0)
        {
            return;
        }

        switch (args.Key)
        {
            case "ArrowDown":
                highlightedVessel = (highlightedVessel + 1) % vesselResults.Count;
                await ScrollHighlightedVesselIntoViewAsync();
                break;
            case "ArrowUp":
                highlightedVessel = highlightedVessel <= 0
                    ? vesselResults.Count - 1
                    : highlightedVessel - 1;
                await ScrollHighlightedVesselIntoViewAsync();
                break;
            case "Enter" when highlightedVessel >= 0:
                SelectVessel(vesselResults[highlightedVessel]);
                break;
            case "Escape":
                vesselResults = [];
                highlightedVessel = -1;
                isVesselDropdownOpen = false;
                break;
        }
    }

    private async Task ScrollHighlightedVesselIntoViewAsync()
    {
        await JS.InvokeVoidAsync("scrollInterop.intoView", VesselOptionId(highlightedVessel));
    }

    private void ClearVessel()
    {
        selectedVessel = null;
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting || selectedVessel is null)
        {
            return;
        }

        isSubmitting = true;
        failure = null;

        try
        {
            var request = new ScheduleVoyageWebRequest(
                voyageNumber,
                selectedVessel.VesselId,
                departurePortCode,
                arrivalPortCode,
                CombineUtc(plannedDepartureDate, plannedDepartureTime),
                CombineUtc(plannedArrivalDate, plannedArrivalTime));

            var result = await VoyagesApi.ScheduleAsync(request, pageCancellation.Token);

            if (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                failure = result.Failure;

                return;
            }

            Navigation.NavigateTo($"/voyages/{result.Value!.Id}");
        }
        finally
        {
            isSubmitting = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        pageCancellation.Cancel();

        if (outsideClickReference is not null)
        {
            try
            {
                await JS.InvokeVoidAsync("outsideClickInterop.unregister", pageElement);
            }
            catch (JSDisconnectedException)
            {
            }

            outsideClickReference.Dispose();
        }

        pageCancellation.Dispose();
    }
}
