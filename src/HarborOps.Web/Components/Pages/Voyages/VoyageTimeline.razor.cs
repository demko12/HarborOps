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

    internal const string PendingClearanceStatus = "Pending";
    internal const string CompletedClearanceStatus = "Completed";
    internal const string ReopenedClearanceStatus = "Reopened";

    internal const string BerthActionKind = "berth";
    internal const string ArriveActionKind = "arrive";
    internal const string DepartActionKind = "depart";

    internal const int MinimumItineraryStops = 2;

    internal const string AllHistoryFilter = "all";
    internal const string DelayHistoryKind = "delays";
    internal const string RevisionHistoryKind = "revisions";
    internal const string ScheduleHistoryKind = "schedule";

    internal const int OnTimeToleranceMinutes = 15;
    internal const int MaximumTimelineDayTicks = 10;

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
    private DateOnly berthArrivalDate;
    private TimeOnly berthArrivalTime;
    private DateOnly berthDepartureDate;
    private TimeOnly berthDepartureTime;
    private string berthReason = string.Empty;
    private BerthAvailability? berthAvailability;
    private VoyagesApiFailure? berthAvailabilityFailure;
    private bool isLoadingBerthAvailability;
    private BerthAllocationHistory? berthAllocationHistory;

    private Guid? clearanceStopId;
    private Guid? activityStopId;

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
    private bool isDelayModalOpen;

    private string historyFilter = AllHistoryFilter;

    private VoyageSchedule? schedule;
    private VoyagesApiFailure? scheduleFailure;
    private string? scheduleSuccess;

    private VoyageEstimateRevisions? estimateRevisions;
    private VoyagesApiFailure? estimateRevisionsFailure;

    private Guid? impactDelayId;
    private DelayImpactPreview? impactPreview;
    private VoyagesApiFailure? impactFailure;
    private bool isLoadingImpact;
    private bool isApplyingImpact;
    private string impactReason = string.Empty;
    private string? impactSuccess;

    private Guid? retryingRevisionBatchId;
    private VoyagesApiFailure? alertFailure;

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
    private ElementReference routeMapElement;

    private IReadOnlyList<VoyageItineraryStopModel> MappedStops => itinerary?.Stops
        .Where(stop => stop.Latitude is not null && stop.Longitude is not null)
        .OrderBy(stop => stop.StopOrder)
        .ToList()
        ?? [];


    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (MappedStops.Count < 2 || disposeCancellation.IsCancellationRequested) return;
        await JS.InvokeVoidAsync("voyageRouteMap.render", routeMapElement, MappedStops);
    }

    private async Task FitRouteMapAsync()
    {
        await JS.InvokeVoidAsync("voyageRouteMap.fit", routeMapElement);
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

    internal static bool IsClearanceCompleted(VoyageItineraryStopModel stop)
    {
        return string.Equals(stop.ClearanceStatus, CompletedClearanceStatus, StringComparison.Ordinal);
    }

    internal static bool IsDepartureBlockedByClearance(VoyageItineraryStopModel stop)
    {
        return !IsClearanceCompleted(stop);
    }

    internal static string DepartureBlockedExplanation(VoyageItineraryStopModel stop)
    {
        if (stop.ClearanceRequiredItemCount == 0)
        {
            return "Departure is blocked: the port-call clearance has no required items yet.";
        }

        var unresolved = stop.ClearanceRequiredItemCount - stop.ClearanceRequiredResolvedCount;

        return unresolved > 0
            ? $"Departure is blocked: {unresolved} required clearance item(s) are neither cleared nor waived."
            : "Departure is blocked until the port-call clearance is completed.";
    }

    internal static string ClearanceStatusLabel(VoyageItineraryStopModel stop)
    {
        return string.IsNullOrWhiteSpace(stop.ClearanceStatus)
            ? PendingClearanceStatus
            : stop.ClearanceStatus;
    }

    internal static string ClearanceStatusModifier(VoyageItineraryStopModel stop)
    {
        return ClearanceStatusLabel(stop) switch
        {
            CompletedClearanceStatus => "completed",
            ReopenedClearanceStatus => "reopened",
            _ => "pending",
        };
    }

    internal static string ClearanceProgressLabel(VoyageItineraryStopModel stop)
    {
        return $"{stop.ClearanceRequiredResolvedCount}/{stop.ClearanceRequiredItemCount} required";
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

    internal static int StopCountWithModifier(VoyageItinerary itinerary, string modifier)
    {
        return itinerary.Stops.Count(stop => string.Equals(StatusModifier(stop.Status), modifier, StringComparison.Ordinal));
    }

    internal static int OverdueClearanceCount(VoyageItinerary itinerary)
    {
        return itinerary.Stops.Sum(stop => stop.ClearanceOverdueRequiredItemCount);
    }

    internal static string OverdueClearanceNote(VoyageItinerary itinerary)
    {
        var overdueStop = itinerary.Stops
            .OrderBy(stop => stop.StopOrder)
            .FirstOrDefault(stop => stop.ClearanceOverdueRequiredItemCount > 0);

        return overdueStop is null
            ? "Required items across port calls"
            : $"Stop {overdueStop.StopOrder} · {overdueStop.PortName} · View port calls";
    }

    internal static string FormatVariance(int minutes)
    {
        var sign = minutes < 0 ? "−" : "+";
        var hours = Math.Abs(minutes) / 60;
        var remainder = Math.Abs(minutes) % 60;

        if (hours == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{remainder}m");
        }

        return remainder == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{hours}h")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{hours}h {remainder}m");
    }

    internal static string VarianceModifier(int minutes)
    {
        if (minutes <= OnTimeToleranceMinutes)
        {
            return "on-time";
        }

        return minutes < 60 ? "late" : "very-late";
    }

    internal static string FormatPercent(double value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.###}%");
    }

    internal static string FormatFraction(double percent)
    {
        return (percent / 100).ToString("0.#####", CultureInfo.InvariantCulture);
    }

    internal static TimelineChart? BuildTimelineChart(
        VoyageItinerary itinerary,
        VoyageEstimateRevisions? revisions,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(itinerary);

        if (itinerary.Stops.Count == 0)
        {
            return null;
        }

        var stops = itinerary.Stops.OrderBy(stop => stop.StopOrder).ToList();
        var revisedStops = stops.ToDictionary(
            stop => stop.StopId,
            stop => revisions?.Stops.FirstOrDefault(candidate => candidate.StopId == stop.StopId && candidate.RevisedEta is not null));

        var instants = new List<DateTime>();

        foreach (var stop in stops)
        {
            instants.Add(stop.Eta);
            instants.Add(stop.Etd);

            if (stop.Ata is { } ata)
            {
                instants.Add(ata);
            }

            if (stop.Atd is { } atd)
            {
                instants.Add(atd);
            }

            if (revisedStops[stop.StopId] is { RevisedEta: { } revisedEta } revised)
            {
                instants.Add(revisedEta);
                instants.Add(revised.RevisedEtd ?? stop.Etd);
            }
        }

        if (stops.Any(stop => string.Equals(stop.Status, ArrivedStatus, StringComparison.Ordinal)))
        {
            instants.Add(nowUtc);
        }

        var start = instants.Min().Date;
        var latest = instants.Max();
        var end = latest == latest.Date ? latest : latest.Date.AddDays(1);

        if (end <= start)
        {
            end = start.AddDays(1);
        }

        var totalMinutes = (end - start).TotalMinutes;

        double Percent(DateTime instant)
        {
            return Math.Clamp((instant - start).TotalMinutes / totalMinutes * 100, 0, 100);
        }

        var dayCount = (int)Math.Round((end - start).TotalDays);
        var step = Math.Max(1, (int)Math.Ceiling(dayCount / (double)MaximumTimelineDayTicks));
        var days = new List<TimelineDayTick>();

        for (var offset = 0; offset < dayCount; offset += step)
        {
            var day = start.AddDays(offset);
            days.Add(new TimelineDayTick(
                day.ToString("dd MMM", CultureInfo.InvariantCulture),
                Percent(day),
                day == nowUtc.Date));
        }

        var rows = stops
            .Select(stop => BuildTimelineRow(stop, revisedStops[stop.StopId], itinerary.Delays, nowUtc, Percent))
            .ToList();

        var voyageDelays = itinerary.Delays
            .Where(delay => delay.VoyageStopId is null)
            .Select(delay => DelayMarker(delay, Percent))
            .ToList();

        double? nowPercent = nowUtc >= start && nowUtc <= end ? Percent(nowUtc) : null;

        return new TimelineChart(start, end, dayCount, days, nowPercent, rows, voyageDelays);
    }

    private static TimelineChartRow BuildTimelineRow(
        VoyageItineraryStopModel stop,
        VoyageEstimateStopModel? revised,
        IReadOnlyList<VoyageDelayModel> delays,
        DateTime nowUtc,
        Func<DateTime, double> percent)
    {
        var planText = $"Plan {FormatTimelineSpan(stop.Eta, stop.Etd)}";
        var plan = TimelineBarFor(stop.Eta, stop.Etd, planText, percent);
        TimelineBar? actual = null;
        TimelineBar? expected = null;
        TimelineBar? revisedBar = null;
        string? detailText = null;
        string? varianceLabel = null;
        var varianceModifier = string.Empty;

        if (stop.Ata is { } ata)
        {
            var actualEnd = stop.Atd ?? (nowUtc > ata ? nowUtc : ata);
            var expectedDeparture = revised?.RevisedEtd ?? stop.Etd;

            detailText = stop.Atd is { } departedAt
                ? $"Actual {FormatTimelineSpan(ata, departedAt)}"
                : $"Arrived {FormatTimelineStamp(ata)} · ETD {FormatTimelineStamp(expectedDeparture)}";
            actual = TimelineBarFor(ata, actualEnd, detailText, percent);

            var varianceMinutes = stop.Atd is { } atd
                ? (int)Math.Round((atd - stop.Etd).TotalMinutes)
                : (int)Math.Round((ata - stop.Eta).TotalMinutes);
            varianceLabel = FormatVariance(varianceMinutes);
            varianceModifier = VarianceModifier(varianceMinutes);

            if (stop.Atd is null && expectedDeparture > actualEnd)
            {
                expected = TimelineBarFor(actualEnd, expectedDeparture, $"Expected departure {FormatUtc(expectedDeparture)}", percent);
            }
        }
        else if (revised?.RevisedEta is { } revisedEta)
        {
            var revisedEtd = revised.RevisedEtd ?? stop.Etd;
            detailText = $"Revised {FormatTimelineSpan(revisedEta, revisedEtd)}";
            revisedBar = TimelineBarFor(revisedEta, revisedEtd, detailText, percent);
            varianceLabel = FormatVariance((int)Math.Round((revisedEta - stop.Eta).TotalMinutes));
            varianceModifier = "revised";
        }

        var detailBars = new[] { actual, expected, revisedBar }.OfType<TimelineBar>().ToList();
        var detailLabel = detailText is null
            ? null
            : TimelineLabelFor(
                detailText,
                detailBars.Min(bar => bar.LeftPercent),
                detailBars.Max(bar => bar.LeftPercent + bar.WidthPercent));

        var markers = delays
            .Where(delay => delay.VoyageStopId == stop.StopId)
            .Select(delay => DelayMarker(delay, percent))
            .ToList();

        var summary = string.Join(
            ". ",
            new[] { plan, actual, expected, revisedBar }
                .OfType<TimelineBar>()
                .Select(bar => bar.Title)
                .Concat(varianceLabel is null ? [] : [$"Variance {varianceLabel}"])
                .Concat(markers.Select(marker => marker.Title)));

        return new TimelineChartRow(
            stop,
            plan,
            revisedBar,
            actual,
            expected,
            TimelineLabelFor(planText, plan.LeftPercent, plan.LeftPercent + plan.WidthPercent),
            detailLabel,
            varianceLabel,
            varianceModifier,
            markers,
            summary);
    }

    internal static string FormatTimelineStamp(DateTime value)
    {
        return value.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string FormatTimelineSpan(DateTime from, DateTime to)
    {
        var end = to.Date == from.Date
            ? to.ToString("HH:mm", CultureInfo.InvariantCulture)
            : FormatTimelineStamp(to);

        return $"{FormatTimelineStamp(from)} – {end}";
    }

    private static TimelineLabel TimelineLabelFor(string text, double start, double end)
    {
        if (end <= 72)
        {
            return new TimelineLabel(text, end, "after");
        }

        return start >= 28
            ? new TimelineLabel(text, start, "before")
            : new TimelineLabel(text, start, "inside");
    }

    private static TimelineBar TimelineBarFor(DateTime from, DateTime to, string title, Func<DateTime, double> percent)
    {
        var left = percent(from);

        return new TimelineBar(left, Math.Max(percent(to) - left, 0.4), title);
    }

    private static TimelineDelayMarker DelayMarker(VoyageDelayModel delay, Func<DateTime, double> percent)
    {
        return new TimelineDelayMarker(
            percent(delay.CreatedAt),
            string.Create(CultureInfo.InvariantCulture, $"Delay {FormatUtc(delay.CreatedAt)} · {delay.Reason} · {delay.Duration} min"));
    }

    internal static IReadOnlyList<TimelineHistoryEntry> BuildHistory(
        VoyageItinerary? itinerary,
        VoyageEstimateRevisions? revisions,
        VoyageSchedule? schedule)
    {
        var entries = new List<TimelineHistoryEntry>();

        if (itinerary is not null)
        {
            entries.AddRange(itinerary.Delays.Select(delay => new TimelineHistoryEntry(DelayHistoryKind, delay.CreatedAt, Delay: delay)));
        }

        if (revisions is not null)
        {
            entries.AddRange(revisions.Revisions.Select(revision => new TimelineHistoryEntry(RevisionHistoryKind, revision.RevisedAtUtc, Revision: revision)));
        }

        if (schedule is not null)
        {
            entries.AddRange(schedule.Changes.Select(change => new TimelineHistoryEntry(ScheduleHistoryKind, change.ChangedAtUtc, ScheduleChange: change)));
        }

        return entries.OrderByDescending(entry => entry.OccurredAtUtc).ToList();
    }

    internal static IReadOnlyList<TimelineHistoryEntry> FilterHistory(IReadOnlyList<TimelineHistoryEntry> entries, string filter)
    {
        return string.Equals(filter, AllHistoryFilter, StringComparison.Ordinal)
            ? entries
            : entries.Where(entry => string.Equals(entry.Kind, filter, StringComparison.Ordinal)).ToList();
    }

    internal static (string Title, string Message) HistoryEmptyState(string filter)
    {
        return filter switch
        {
            DelayHistoryKind => ("No delays recorded", "Delays reported for this voyage or its stops will appear here."),
            RevisionHistoryKind => ("No revised estimates", "Applying a delay impact publishes revised ETAs and ETDs here."),
            ScheduleHistoryKind => ("No schedule changes", "Amendments and cancellations recorded for this voyage will appear here."),
            _ => ("No history yet", "Delays, revised estimates and schedule changes for this voyage will appear here."),
        };
    }

    private void SetHistoryFilter(string filter)
    {
        historyFilter = filter;
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
        scheduleFailure = null;
        estimateRevisionsFailure = null;

        var itineraryTask = VoyagesApi.GetItineraryAsync(
            requestedVoyageId,
            disposeCancellation.Token);
        var scheduleTask = VoyagesApi.GetScheduleChangesAsync(
            requestedVoyageId,
            disposeCancellation.Token);
        var revisionsTask = VoyagesApi.GetEstimateRevisionsAsync(
            requestedVoyageId,
            disposeCancellation.Token);

        try
        {
            await Task.WhenAll(itineraryTask, scheduleTask, revisionsTask);
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

        var itineraryResult = await itineraryTask;

        if (!itineraryResult.IsSuccess)
        {
            failure = itineraryResult.Failure;
            itinerary = null;

            return;
        }

        itinerary = itineraryResult.Value;

        ApplySchedule(await scheduleTask);
        ApplyEstimateRevisions(await revisionsTask);
    }

    private async Task RefreshItineraryAsync()
    {
        var requestedVoyageId = VoyageId;

        VoyagesApiResult<VoyageItinerary> result;
        try
        {
            result = await VoyagesApi.GetItineraryAsync(
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

        if (result.IsSuccess && result.Value is { } refreshed)
        {
            itinerary = refreshed;
        }
    }

    private async Task LoadEstimateRevisionsAsync()
    {
        var requestedVoyageId = VoyageId;
        estimateRevisionsFailure = null;

        VoyagesApiResult<VoyageEstimateRevisions> result;
        try
        {
            result = await VoyagesApi.GetEstimateRevisionsAsync(
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

        ApplyEstimateRevisions(result);
    }

    private void ApplyEstimateRevisions(VoyagesApiResult<VoyageEstimateRevisions> result)
    {
        if (!result.IsSuccess)
        {
            estimateRevisionsFailure = result.Failure;
            estimateRevisions = null;

            return;
        }

        estimateRevisionsFailure = null;
        estimateRevisions = result.Value;
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

        ApplySchedule(result);
    }

    private void ApplySchedule(VoyagesApiResult<VoyageSchedule> result)
    {
        if (!result.IsSuccess)
        {
            scheduleFailure = result.Failure;
            schedule = null;

            return;
        }

        scheduleFailure = null;
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
        selectedBerthId = stop.BerthId;
        berthArrivalDate = DateOnly.FromDateTime(stop.Eta);
        berthArrivalTime = TimeOnly.FromTimeSpan(stop.Eta.TimeOfDay);
        berthDepartureDate = DateOnly.FromDateTime(stop.Etd);
        berthDepartureTime = TimeOnly.FromTimeSpan(stop.Etd.TimeOfDay);
        berthReason = string.Empty;
        berthAvailability = null;
        berthAvailabilityFailure = null;
        berthAllocationHistory = null;
        _ = LoadBerthAllocationContextAsync(stop);
    }

    private void ToggleClearance(VoyageItineraryStopModel stop)
    {
        activityStopId = null;
        clearanceStopId = clearanceStopId == stop.StopId ? null : stop.StopId;
    }

    private void CloseClearance()
    {
        clearanceStopId = null;
    }

    private void ToggleActivityLog(VoyageItineraryStopModel stop)
    {
        clearanceStopId = null;
        activityStopId = activityStopId == stop.StopId ? null : stop.StopId;
    }

    private void CloseActivityLog()
    {
        activityStopId = null;
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
        berthAvailability = null;
        berthAllocationHistory = null;
    }

    private async Task LoadBerthAllocationContextAsync(VoyageItineraryStopModel stop)
    {
        isLoadingBerthAvailability = true;
        berthAvailabilityFailure = null;
        try
        {
            var start = CombineUtc(berthArrivalDate, berthArrivalTime);
            var end = CombineUtc(berthDepartureDate, berthDepartureTime);
            var availabilityTask = VoyagesApi.GetBerthAvailabilityAsync(stop.PortId, start, end, disposeCancellation.Token);
            var historyTask = VoyagesApi.GetBerthAllocationHistoryAsync(VoyageId, stop.StopId, disposeCancellation.Token);
            await Task.WhenAll(availabilityTask, historyTask);
            if (disposeCancellation.IsCancellationRequested || activeStopId != stop.StopId) return;
            var availabilityResult = await availabilityTask;
            var historyResult = await historyTask;
            if (availabilityResult.IsSuccess) berthAvailability = availabilityResult.Value;
            else berthAvailabilityFailure = availabilityResult.Failure;
            if (historyResult.IsSuccess) berthAllocationHistory = historyResult.Value;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) { }
        finally
        {
            if (!disposeCancellation.IsCancellationRequested && activeStopId == stop.StopId) isLoadingBerthAvailability = false;
        }
    }

    private void UseAlternative(BerthAvailabilityModel berth)
    {
        selectedBerthId = berth.BerthId;
        if (berth.NearestAvailableFromUtc is not { } start || berth.NearestAvailableToUtc is not { } end) return;
        berthArrivalDate = DateOnly.FromDateTime(start);
        berthArrivalTime = TimeOnly.FromTimeSpan(start.TimeOfDay);
        berthDepartureDate = DateOnly.FromDateTime(end);
        berthDepartureTime = TimeOnly.FromTimeSpan(end.TimeOfDay);
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
            var isInitialAssignment = itinerary?.Stops.FirstOrDefault(stop => stop.StopId == stopId)?.BerthId is null;
            var result = isInitialAssignment
                ? await VoyagesApi.AssignBerthAsync(VoyageId, stopId, new AssignBerthWebRequest(berthId), disposeCancellation.Token)
                : null;

            if (result is not null)
            {
                if (!result.IsSuccess)
                {
                    actionFailure = result.Failure;
                    if (result.Failure!.IsConflict || result.Failure.IsNotFound) await LoadAsync();
                    return;
                }

                actionSuccess = "Berth assigned.";
                CancelAction();
                await LoadAsync();
                return;
            }

            var replacement = await VoyagesApi.ReplaceBerthAllocationAsync(
                VoyageId, stopId,
                new ReplaceBerthAllocationWebRequest(berthId, CombineUtc(berthArrivalDate, berthArrivalTime), CombineUtc(berthDepartureDate, berthDepartureTime), berthReason),
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!replacement.IsSuccess)
            {
                actionFailure = replacement.Failure;

                if (replacement.Failure!.IsConflict || replacement.Failure.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            actionSuccess = "Berth allocation rescheduled.";
            CancelAction();

            await LoadAsync();
        }
        finally
        {
            isActionSubmitting = false;
        }
    }

    private bool IsReschedulingActiveStop => activeStopId is { } stopId
        && itinerary?.Stops.FirstOrDefault(stop => stop.StopId == stopId)?.BerthId is not null;

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

    private void OpenDelayModal()
    {
        isDelayModalOpen = true;
        delayFailure = null;
        delaySuccess = null;
        delayReason = string.Empty;
        delayDescription = null;
        delayDurationMinutes = null;
        delayStopSelection = string.Empty;
    }

    private void CloseDelayModal()
    {
        isDelayModalOpen = false;
        delayFailure = null;
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

            isDelayModalOpen = false;
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

    internal bool IsDelayApplied(Guid delayId)
    {
        return estimateRevisions is not null
            && estimateRevisions.Revisions.Any(revision => revision.DelayId == delayId);
    }

    internal VoyageEstimateStopModel? RevisedStop(Guid stopId)
    {
        return estimateRevisions?.Stops
            .FirstOrDefault(stop => stop.StopId == stopId && stop.RevisedEta is not null);
    }

    internal static string FormatDelta(int minutes)
    {
        return minutes > 0
            ? string.Create(CultureInfo.InvariantCulture, $"+{minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes} min");
    }

    private async Task BeginImpactAsync(VoyageDelayModel delay)
    {
        impactDelayId = delay.DelayId;
        impactPreview = null;
        impactFailure = null;
        impactSuccess = null;
        impactReason = string.Empty;
        isLoadingImpact = true;

        try
        {
            var result = await VoyagesApi.PreviewDelayImpactAsync(
                VoyageId,
                delay.DelayId,
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                impactFailure = result.Failure;

                return;
            }

            impactPreview = result.Value;
        }
        finally
        {
            isLoadingImpact = false;
        }
    }

    private void CancelImpact()
    {
        impactDelayId = null;
        impactPreview = null;
        impactFailure = null;
        impactReason = string.Empty;
    }

    private async Task ApplyImpactAsync()
    {
        if (isApplyingImpact || impactDelayId is not { } delayId || impactPreview is null)
        {
            return;
        }

        isApplyingImpact = true;
        impactFailure = null;

        try
        {
            var request = new ApplyDelayImpactWebRequest(
                impactReason,
                impactPreview.Stops
                    .Select(stop => new ApplyDelayImpactStopWebRequest(
                        stop.StopId,
                        stop.CurrentEta,
                        stop.CurrentEtd,
                        stop.ProposedEta,
                        stop.ProposedEtd))
                    .ToList());

            var result = await VoyagesApi.ApplyDelayImpactAsync(
                VoyageId,
                delayId,
                request,
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                impactFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    impactPreview = null;

                    await LoadAsync();
                }

                return;
            }

            impactSuccess = result.Value!.AlertCreated
                ? "Revised estimates published and the operations team was alerted."
                : "Revised estimates published. The operational alert could not be sent.";

            CancelImpact();

            await LoadAsync();
        }
        finally
        {
            isApplyingImpact = false;
        }
    }

    private async Task RetryAlertAsync(Guid revisionBatchId)
    {
        if (retryingRevisionBatchId is not null)
        {
            return;
        }

        retryingRevisionBatchId = revisionBatchId;
        alertFailure = null;

        try
        {
            var result = await VoyagesApi.RetryEstimateRevisionAlertAsync(
                VoyageId,
                revisionBatchId,
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                alertFailure = result.Failure;

                return;
            }

            await LoadEstimateRevisionsAsync();
        }
        finally
        {
            retryingRevisionBatchId = null;
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

    internal static string? ActivitySummaryLabel(VoyageItineraryStopModel stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        if (stop.PortCallActivityCount == 0 && stop.PortCallStatementStatus is null)
        {
            return null;
        }

        var activities = stop.PortCallActivityCount == 1
            ? "1 activity"
            : string.Create(CultureInfo.InvariantCulture, $"{stop.PortCallActivityCount} activities");

        return stop.PortCallStatementStatus is { } status
            ? $"{activities} · Statement {status.ToLowerInvariant()}"
            : activities;
    }

    internal static string ActivitySummaryModifier(VoyageItineraryStopModel stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return stop.PortCallStatementStatus switch
        {
            "Finalized" => "finalized",
            "Draft" => "draft",
            _ => "none",
        };
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

    internal sealed record TimelineChart(
        DateTime Start,
        DateTime End,
        int DayCount,
        IReadOnlyList<TimelineDayTick> Days,
        double? NowPercent,
        IReadOnlyList<TimelineChartRow> Rows,
        IReadOnlyList<TimelineDelayMarker> VoyageDelays);

    internal sealed record TimelineDayTick(string Label, double LeftPercent, bool IsToday);

    internal sealed record TimelineBar(double LeftPercent, double WidthPercent, string Title);

    internal sealed record TimelineDelayMarker(double LeftPercent, string Title);

    internal sealed record TimelineLabel(string Text, double LeftPercent, string Placement);

    internal sealed record TimelineChartRow(
        VoyageItineraryStopModel Stop,
        TimelineBar Plan,
        TimelineBar? Revised,
        TimelineBar? Actual,
        TimelineBar? Expected,
        TimelineLabel PlanLabel,
        TimelineLabel? DetailLabel,
        string? VarianceLabel,
        string VarianceModifier,
        IReadOnlyList<TimelineDelayMarker> Delays,
        string Summary);

    internal sealed record TimelineHistoryEntry(
        string Kind,
        DateTime OccurredAtUtc,
        VoyageDelayModel? Delay = null,
        VoyageEstimateRevisionModel? Revision = null,
        VoyageScheduleChangeModel? ScheduleChange = null);

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
