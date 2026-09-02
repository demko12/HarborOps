using System.Globalization;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class VoyageTimeline : IDisposable
{
    internal const string PlannedStatus = "Planned";
    internal const string ArrivedStatus = "Arrived";
    internal const string DepartedStatus = "Departed";
    internal const string MissingValue = "—";

    internal const string InProgressVoyageStatus = "InProgress";
    internal const string CompletedVoyageStatus = "Completed";
    internal const string CancelledVoyageStatus = "Cancelled";
    internal const string CancelledChangeType = "Cancelled";

    internal const string ManageVoyagesPermission = "voyages.manage";

    internal const string BerthActionKind = "berth";
    internal const string ArriveActionKind = "arrive";
    internal const string DepartActionKind = "depart";

    internal const int MinimumItineraryStops = 2;

    private readonly CancellationTokenSource disposeCancellation = new();

    private VoyageItinerary? itinerary;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedVoyageId;

    [Parameter]
    public Guid VoyageId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? BoardQuery { get; set; }

    private string BoardHref => string.IsNullOrWhiteSpace(BoardQuery) ? "/voyages" : $"/voyages?{BoardQuery}";

    private bool CanManageVoyages => AuthorizationState.HasPermission(ManageVoyagesPermission);

    private bool voyageIdCopied;

    private Guid? activeStopId;
    private string? activeActionKind;
    private DateOnly actionDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private TimeOnly actionTime = TimeOnly.FromTimeSpan(DateTime.UtcNow.TimeOfDay);
    private VoyagesApiFailure? actionFailure;
    private string? actionSuccess;
    private bool isActionSubmitting;

    private Guid? activeStopPortId;
    private Guid? selectedBerthId;

    private string delayReason = string.Empty;
    private string? delayDescription;
    private int? delayDurationMinutes;
    private string delayStopSelection = string.Empty;

    private bool isItineraryModalOpen;
    private Guid? selectedRouteId;
    private VoyagesApiFailure? itineraryFailure;
    private bool isSubmittingItinerary;
    private readonly List<ItineraryStopDraft> itineraryStops = [];
    private VoyagesApiFailure? delayFailure;
    private string? delaySuccess;
    private bool isSubmittingDelay;

    private VoyageSchedule? schedule;
    private VoyagesApiFailure? scheduleFailure;
    private string? scheduleSuccess;

    private bool isAmendModalOpen;
    private Guid? amendDeparturePortId;
    private Guid? amendArrivalPortId;
    private Guid? amendRouteId;
    private DateOnly amendDepartureDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private TimeOnly amendDepartureTime;
    private DateOnly amendArrivalDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private TimeOnly amendArrivalTime;
    private string amendReason = string.Empty;
    private VoyagesApiFailure? amendFailure;
    private bool isSubmittingAmend;

    private bool isCancelModalOpen;
    private string cancelReason = string.Empty;
    private VoyagesApiFailure? cancelFailure;
    private bool isSubmittingCancel;

    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string StatusModifier(string status)
    {
        return status switch
        {
            DepartedStatus => "departed",
            ArrivedStatus => "arrived",
            _ => "planned",
        };
    }

    internal static bool CanAssignBerth(VoyageItineraryStopModel stop)
    {
        return string.Equals(stop.Status, PlannedStatus, StringComparison.Ordinal);
    }

    internal static bool CanArrive(VoyageItineraryStopModel stop)
    {
        return string.Equals(stop.Status, PlannedStatus, StringComparison.Ordinal);
    }

    internal static bool CanDepart(VoyageItineraryStopModel stop)
    {
        return string.Equals(stop.Status, ArrivedStatus, StringComparison.Ordinal);
    }

    internal static string BerthState(VoyageItineraryStopModel stop)
    {
        if (stop.BerthId is null)
        {
            return "No berth assigned";
        }

        return stop.Status switch
        {
            ArrivedStatus => "Occupied",
            DepartedStatus => "Released",
            _ => "Reserved",
        };
    }

    internal static int TotalDelayMinutes(VoyageItinerary itinerary)
    {
        return itinerary.Delays.Sum(delay => delay.Duration);
    }

    internal static int CompletedStopCount(VoyageItinerary itinerary)
    {
        return itinerary.Stops.Count(stop => string.Equals(stop.Status, DepartedStatus, StringComparison.Ordinal));
    }

    internal static DateTime CombineUtc(DateOnly date, TimeOnly time)
    {
        return DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
    }

    internal static string DelaySourceLabel(VoyageItinerary itinerary, VoyageDelayModel delay)
    {
        if (delay.VoyageStopId is not { } stopId)
        {
            return "Voyage-level";
        }

        var stop = itinerary.Stops.FirstOrDefault(candidate => candidate.StopId == stopId);

        return stop is null
            ? "Stop"
            : $"Stop {stop.StopOrder} — {stop.PortName}";
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedVoyageId == VoyageId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedVoyageId = VoyageId;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedVoyageId = VoyageId;
        isLoading = true;
        failure = null;

        VoyagesApiResult<VoyageItinerary> result;
        try
        {
            result = await VoyagesApi.GetItineraryAsync(requestedVoyageId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedVoyageId != VoyageId)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            itinerary = null;

            return;
        }

        itinerary = result.Value;

        await LoadScheduleAsync();
    }

    private async Task LoadScheduleAsync()
    {
        var requestedVoyageId = VoyageId;
        scheduleFailure = null;

        VoyagesApiResult<VoyageSchedule> result;
        try
        {
            result = await VoyagesApi.GetScheduleChangesAsync(
                requestedVoyageId,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedVoyageId != VoyageId)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            scheduleFailure = result.Failure;
            schedule = null;

            return;
        }

        schedule = result.Value;
    }

    private async Task CopyVoyageIdAsync()
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", VoyageId.ToString());

        voyageIdCopied = true;
        StateHasChanged();

        try
        {
            await Task.Delay(1500, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        voyageIdCopied = false;
        StateHasChanged();
    }

    private void BeginBerthAction(VoyageItineraryStopModel stop)
    {
        activeStopId = stop.StopId;
        activeStopPortId = stop.PortId;
        activeActionKind = BerthActionKind;
        actionFailure = null;
        actionSuccess = null;
        selectedBerthId = null;
    }

    private void BeginStopAction(VoyageItineraryStopModel stop, string kind)
    {
        activeStopId = stop.StopId;
        activeActionKind = kind;
        actionFailure = null;
        actionSuccess = null;
        actionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        actionTime = TimeOnly.FromTimeSpan(DateTime.UtcNow.TimeOfDay);
    }

    private void CancelAction(KeyboardEventArgs? args = null)
    {
        if (args is not null && !string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            return;
        }

        activeStopId = null;
        activeStopPortId = null;
        activeActionKind = null;
        actionFailure = null;
        selectedBerthId = null;
    }

    private async Task ConfirmBerthAsync()
    {
        if (isActionSubmitting || activeStopId is not { } stopId || selectedBerthId is not { } berthId)
        {
            return;
        }

        isActionSubmitting = true;
        actionFailure = null;

        try
        {
            var result = await VoyagesApi.AssignBerthAsync(
                VoyageId,
                stopId,
                new AssignBerthWebRequest(berthId),
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            actionSuccess = "Berth assigned.";
            CancelAction();

            await LoadAsync();
        }
        finally
        {
            isActionSubmitting = false;
        }
    }

    private async Task ConfirmStopActionAsync()
    {
        if (isActionSubmitting || activeStopId is not { } stopId || activeActionKind is null)
        {
            return;
        }

        isActionSubmitting = true;
        actionFailure = null;

        try
        {
            var stamp = CombineUtc(actionDate, actionTime);

            var result = string.Equals(activeActionKind, ArriveActionKind, StringComparison.Ordinal)
                ? await VoyagesApi.RecordStopArrivalAsync(
                    VoyageId,
                    stopId,
                    new RecordStopArrivalWebRequest(stamp),
                    disposeCancellation.Token)
                : await VoyagesApi.RecordStopDepartureAsync(
                    VoyageId,
                    stopId,
                    new RecordStopDepartureWebRequest(stamp),
                    disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            actionSuccess = $"Stop is now {result.Value!.Status}.";
            CancelAction();

            await LoadAsync();
        }
        finally
        {
            isActionSubmitting = false;
        }
    }

    private async Task SubmitDelayAsync()
    {
        if (isSubmittingDelay)
        {
            return;
        }

        isSubmittingDelay = true;
        delayFailure = null;
        delaySuccess = null;

        try
        {
            var voyageStopId = string.IsNullOrEmpty(delayStopSelection)
                ? (Guid?)null
                : Guid.Parse(delayStopSelection);

            var request = new RecordDelayWebRequest(
                delayReason,
                string.IsNullOrWhiteSpace(delayDescription) ? null : delayDescription.Trim(),
                delayDurationMinutes,
                voyageStopId);

            var result = await VoyagesApi.RecordDelayAsync(VoyageId, request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                delayFailure = result.Failure;

                return;
            }

            delaySuccess = "Delay recorded.";
            delayReason = string.Empty;
            delayDescription = null;
            delayDurationMinutes = null;
            delayStopSelection = string.Empty;

            await LoadAsync();
        }
        finally
        {
            isSubmittingDelay = false;
        }
    }

    internal static bool CanAmendSchedule(string voyageStatus)
    {
        return string.Equals(voyageStatus, PlannedStatus, StringComparison.Ordinal);
    }

    internal static string VoyageStatusModifier(string voyageStatus)
    {
        return voyageStatus switch
        {
            InProgressVoyageStatus => "in-progress",
            CompletedVoyageStatus => "completed",
            CancelledVoyageStatus => "cancelled",
            _ => "planned",
        };
    }

    internal static string ChangeTypeModifier(string changeType)
    {
        return string.Equals(changeType, CancelledChangeType, StringComparison.Ordinal)
            ? "cancelled"
            : "amended";
    }

    internal static string FormatActor(Guid actorId)
    {
        return actorId.ToString("D", CultureInfo.InvariantCulture)[..8];
    }

    internal static IReadOnlyList<ScheduleChangeField> ChangedFields(VoyageScheduleChangeModel change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var fields = new List<ScheduleChangeField>();

        AddIfChanged(fields, "Status", change.PreviousStatus, change.NewStatus);
        AddIfChanged(
            fields,
            "Departure port",
            FormatReference(change.PreviousDeparturePortId, change.PreviousDeparturePortName),
            FormatReference(change.NewDeparturePortId, change.NewDeparturePortName));
        AddIfChanged(
            fields,
            "Arrival port",
            FormatReference(change.PreviousArrivalPortId, change.PreviousArrivalPortName),
            FormatReference(change.NewArrivalPortId, change.NewArrivalPortName));
        AddIfChanged(
            fields,
            "Route",
            FormatReference(change.PreviousRouteId, change.PreviousRouteName),
            FormatReference(change.NewRouteId, change.NewRouteName));
        AddIfChanged(
            fields,
            "Planned departure",
            FormatUtc(change.PreviousPlannedDeparture),
            FormatUtc(change.NewPlannedDeparture));
        AddIfChanged(
            fields,
            "Planned arrival",
            FormatUtc(change.PreviousPlannedArrival),
            FormatUtc(change.NewPlannedArrival));

        return fields;
    }

    private static void AddIfChanged(
        List<ScheduleChangeField> fields,
        string label,
        string previousValue,
        string newValue)
    {
        if (!string.Equals(previousValue, newValue, StringComparison.Ordinal))
        {
            fields.Add(new ScheduleChangeField(label, previousValue, newValue));
        }
    }

    private static string FormatReference(Guid? id, string? name)
    {
        return id is null ? MissingValue : name ?? MissingValue;
    }

    private void OpenAmendModal()
    {
        if (schedule is null)
        {
            return;
        }

        isAmendModalOpen = true;
        amendFailure = null;
        scheduleSuccess = null;
        amendDeparturePortId = schedule.Voyage.DeparturePortId;
        amendArrivalPortId = schedule.Voyage.ArrivalPortId;
        amendRouteId = schedule.Voyage.RouteId;
        amendDepartureDate = DateOnly.FromDateTime(schedule.Voyage.PlannedDeparture);
        amendDepartureTime = TimeOnly.FromTimeSpan(schedule.Voyage.PlannedDeparture.TimeOfDay);
        amendArrivalDate = DateOnly.FromDateTime(schedule.Voyage.PlannedArrival);
        amendArrivalTime = TimeOnly.FromTimeSpan(schedule.Voyage.PlannedArrival.TimeOfDay);
        amendReason = string.Empty;
    }

    private void CloseAmendModal()
    {
        isAmendModalOpen = false;
        amendFailure = null;
    }

    private async Task SubmitAmendAsync()
    {
        if (isSubmittingAmend)
        {
            return;
        }

        isSubmittingAmend = true;
        amendFailure = null;

        try
        {
            var request = new AmendVoyageScheduleWebRequest(
                amendRouteId,
                amendDeparturePortId,
                amendArrivalPortId,
                CombineUtc(amendDepartureDate, amendDepartureTime),
                CombineUtc(amendArrivalDate, amendArrivalTime),
                amendReason);

            var result = await VoyagesApi.AmendScheduleAsync(
                VoyageId,
                request,
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                amendFailure = result.Failure;

                return;
            }

            isAmendModalOpen = false;
            scheduleSuccess = "Schedule amended.";

            await LoadAsync();
        }
        finally
        {
            isSubmittingAmend = false;
        }
    }

    private void OpenCancelModal()
    {
        isCancelModalOpen = true;
        cancelFailure = null;
        scheduleSuccess = null;
        cancelReason = string.Empty;
    }

    private void CloseCancelModal()
    {
        isCancelModalOpen = false;
        cancelFailure = null;
    }

    private async Task SubmitCancelAsync()
    {
        if (isSubmittingCancel)
        {
            return;
        }

        isSubmittingCancel = true;
        cancelFailure = null;

        try
        {
            var result = await VoyagesApi.CancelVoyageAsync(
                VoyageId,
                new CancelVoyageWebRequest(cancelReason),
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                cancelFailure = result.Failure;

                return;
            }

            isCancelModalOpen = false;
            scheduleSuccess = "Voyage cancelled.";

            await LoadAsync();
        }
        finally
        {
            isSubmittingCancel = false;
        }
    }

    internal sealed record ScheduleChangeField(
        string Label,
        string PreviousValue,
        string NewValue);

    private void OpenItineraryModal()
    {
        isItineraryModalOpen = true;
        itineraryFailure = null;
        selectedRouteId = null;
        itineraryStops.Clear();
        itineraryStops.Add(new ItineraryStopDraft());
        itineraryStops.Add(new ItineraryStopDraft());
    }

    private void CloseItineraryModal()
    {
        isItineraryModalOpen = false;
    }

    private async Task OnRouteSelectedAsync(Guid? routeId)
    {
        selectedRouteId = routeId;
        itineraryFailure = null;

        if (routeId is null)
        {
            itineraryStops.Clear();
            itineraryStops.Add(new ItineraryStopDraft());
            itineraryStops.Add(new ItineraryStopDraft());
            return;
        }

        var result = await VoyagesApi.GetRouteDetailsAsync(routeId.Value, disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            itineraryFailure = result.Failure;
            selectedRouteId = null;
            return;
        }

        var orderedStops = result.Value!.Stops.OrderBy(stop => stop.StopOrder).ToList();

        itineraryStops.Clear();

        foreach (var stop in orderedStops)
        {
            itineraryStops.Add(new ItineraryStopDraft
            {
                IsRouteCovered = true,
                PortLabel = stop.PortName,
                DistanceFromPrevious = stop.DistanceFromPrevious,
                EstimatedDurationFromPrevious = stop.EstimatedDurationFromPrevious,
            });
        }
    }

    private void AddItineraryStop()
    {
        itineraryStops.Add(new ItineraryStopDraft());
    }

    private void RemoveItineraryStop(int index)
    {
        if (itineraryStops[index].IsRouteCovered || itineraryStops.Count <= MinimumItineraryStops)
        {
            return;
        }

        itineraryStops.RemoveAt(index);
    }

    private async Task SubmitItineraryAsync()
    {
        if (isSubmittingItinerary)
        {
            return;
        }

        isSubmittingItinerary = true;
        itineraryFailure = null;

        try
        {
            var stopsPayload = itineraryStops
                .Select((stop, index) => new PlanVoyageItineraryStopWebRequest(
                    index + 1,
                    stop.IsRouteCovered ? null : stop.PortId,
                    stop.IsRouteCovered ? null : (index == 0 ? 0m : stop.DistanceFromPrevious),
                    stop.IsRouteCovered ? null : (index == 0 ? 0 : stop.EstimatedDurationFromPrevious),
                    CombineUtc(stop.EtaDate, stop.EtaTime),
                    CombineUtc(stop.EtdDate, stop.EtdTime)))
                .ToList();

            var request = new PlanVoyageItineraryWebRequest(selectedRouteId, stopsPayload);

            var result = await VoyagesApi.PlanItineraryAsync(VoyageId, request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                itineraryFailure = result.Failure;

                return;
            }

            isItineraryModalOpen = false;

            await LoadAsync();
        }
        finally
        {
            isSubmittingItinerary = false;
        }
    }

    private sealed class ItineraryStopDraft
    {
        public bool IsRouteCovered { get; set; }

        public string? PortLabel { get; set; }

        public Guid? PortId { get; set; }

        public decimal? DistanceFromPrevious { get; set; }

        public int? EstimatedDurationFromPrevious { get; set; }

        public DateOnly EtaDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public TimeOnly EtaTime { get; set; }

        public DateOnly EtdDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public TimeOnly EtdTime { get; set; }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
