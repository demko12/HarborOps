using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class ScheduleVoyage
{
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const int PortCodeLength = 5;

    private const int VesselResultLimit = 8;

    private CancellationTokenSource pageCancellation = new();

    private string voyageNumber = string.Empty;
    private string departurePortCode = string.Empty;
    private string arrivalPortCode = string.Empty;
    private DateOnly plannedDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private TimeOnly plannedDepartureTime = new(8, 0);
    private DateOnly plannedArrivalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
    private TimeOnly plannedArrivalTime = new(17, 0);

    private string? vesselSearch;
    private IReadOnlyList<VesselListItemResponse> vesselResults = [];
    private VesselListItemResponse? selectedVessel;
    private FleetApiFailure? vesselFailure;
    private VoyagesApiFailure? failure;
    private bool isSearchingVessels;
    private bool isSubmitting;

    private int highlightedVessel = -1;

    private bool CanManageVoyages => AuthorizationState.HasPermission(ManageVoyagesPermission);

    /// port codes are always upper case, so the field corrects while it is typed
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

    private async Task SearchVesselsAsync()
    {
        if (string.IsNullOrWhiteSpace(vesselSearch))
        {
            vesselResults = [];
            vesselFailure = null;

            return;
        }

        isSearchingVessels = true;
        vesselFailure = null;

        var result = await FleetApi.GetVesselsAsync(
            vesselSearch,
            null,
            1,
            VesselResultLimit,
            pageCancellation.Token);

        if (pageCancellation.IsCancellationRequested)
        {
            return;
        }

        isSearchingVessels = false;

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
    }

    private void HandleVesselKeyDown(KeyboardEventArgs args)
    {
        if (vesselResults.Count == 0)
        {
            return;
        }

        switch (args.Key)
        {
            case "ArrowDown":
                highlightedVessel = (highlightedVessel + 1) % vesselResults.Count;
                break;
            case "ArrowUp":
                highlightedVessel = highlightedVessel <= 0
                    ? vesselResults.Count - 1
                    : highlightedVessel - 1;
                break;
            case "Enter" when highlightedVessel >= 0:
                SelectVessel(vesselResults[highlightedVessel]);
                break;
            case "Escape":
                vesselResults = [];
                highlightedVessel = -1;
                break;
        }
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

            Navigation.NavigateTo("/voyages");
        }
        finally
        {
            isSubmitting = false;
        }
    }

    public void Dispose()
    {
        pageCancellation.Cancel();
        pageCancellation.Dispose();
    }
}
