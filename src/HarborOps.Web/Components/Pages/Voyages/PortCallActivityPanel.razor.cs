using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class PortCallActivityPanel : IDisposable
{
    internal const string ActiveStatus = "Active";
    internal const string VoidedStatus = "Voided";
    internal const string DraftStatus = "Draft";
    internal const string FinalizedStatus = "Finalized";

    internal const string StopArrivalSource = "StopArrival";
    internal const string StopDepartureSource = "StopDeparture";
    internal const string ManualActivitySource = "ManualActivity";
    internal const string DelaySource = "Delay";

    internal const string RecordAction = "record";
    internal const string CorrectAction = "correct";
    internal const string VoidAction = "void";
    internal const string FinalizeAction = "finalize";

    internal const string UtcOffset = "+00:00";
    internal const string PortOffset = "port";
    internal const string MissingValue = "—";

    internal static readonly IReadOnlyList<string> ActivityTypes =
    [
        "PilotOnBoard",
        "AllFast",
        "CargoStarted",
        "CargoCompleted",
        "OperationalNote",
    ];

    internal static readonly IReadOnlyList<string> Offsets = Enumerable
        .Range(-24, 53)
        .Select(halfHours => FormatOffset(TimeSpan.FromMinutes(halfHours * 30)))
        .ToList();

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<Guid, PortCallActivityDetails> historyCache = [];

    private PortCallTimeline? timeline;
    private TimeZoneInfo? portZone;
    private PortCallStatementModel? statement;
    private ElementReference activityFormFirstField;
    private bool focusActivityForm;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedStopId;

    private string? activeAction;
    private Guid? activeActivityId;
    private bool isSubmitting;
    private VoyagesApiFailure? actionFailure;
    private string? actionValidationMessage;
    private string? actionSuccess;

    private Guid? historyActivityId;
    private VoyagesApiFailure? historyFailure;

    private string formActivityType = "AllFast";
    private DateOnly? formOccurredDate;
    private TimeOnly formOccurredTime = TimeOnly.MinValue;
    private DateOnly? formEndedDate;
    private TimeOnly formEndedTime = TimeOnly.MinValue;
    private string formOffset = UtcOffset;
    private string formNotes = string.Empty;
    private string formReason = string.Empty;

    [Parameter]
    public Guid VoyageId { get; set; }

    [Parameter]
    public Guid StopId { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    internal string DefaultOffset => portZone is null ? UtcOffset : PortOffset;

    internal bool CanManageVoyages =>
        AuthorizationState.HasPermission(CurrentUserAuthorizationState.VoyagesManagePermission);

    internal bool CanRecord => CanManageVoyages && timeline?.CanRecordActivities == true;

    internal bool IsFinalized =>
        statement is not null && string.Equals(statement.Status, FinalizedStatus, StringComparison.Ordinal);

    internal string PrintHref => $"/voyages/{VoyageId}/stops/{StopId}/statement/print";

    internal string? ActionErrorMessage
    {
        get
        {
            if (actionValidationMessage is not null)
            {
                return actionValidationMessage;
            }

            if (actionFailure is not { } current)
            {
                return null;
            }

            return current.Errors.Count > 0
                ? string.Join(" ", current.Errors.SelectMany(entry => entry.Value))
                : current.Message;
        }
    }

    internal bool ActionErrorIsConflict =>
        actionValidationMessage is null && actionFailure?.IsConflict == true;

    internal IReadOnlyList<PortCallActivityModel> VoidedActivities =>
        timeline?.Activities
            .Where(activity => string.Equals(activity.Status, VoidedStatus, StringComparison.Ordinal))
            .ToList()
        ?? [];

    internal static string ActivityTypeLabel(string activityType)
    {
        return activityType switch
        {
            "PilotOnBoard" => "Pilot on board",
            "AllFast" => "All fast",
            "CargoStarted" => "Cargo started",
            "CargoCompleted" => "Cargo completed",
            "OperationalNote" => "Operational note",
            _ => activityType,
        };
    }

    internal static string SourceLabel(string source)
    {
        return source switch
        {
            StopArrivalSource => "Stop arrival",
            StopDepartureSource => "Stop departure",
            DelaySource => "Delay",
            ManualActivitySource => "Manual",
            _ => source,
        };
    }

    internal static string SourceModifier(string source)
    {
        return source switch
        {
            StopArrivalSource or StopDepartureSource => "stop",
            DelaySource => "delay",
            _ => "manual",
        };
    }

    internal static string EntryLabel(PortCallLogEntryModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.Equals(entry.Source, ManualActivitySource, StringComparison.Ordinal)
            ? ActivityTypeLabel(entry.Label)
            : entry.Label;
    }

    internal static string ChangeTypeLabel(string changeType)
    {
        return changeType switch
        {
            "Recorded" => "Recorded",
            "Corrected" => "Corrected",
            "Voided" => "Voided",
            _ => changeType,
        };
    }

    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : MissingValue;
    }

    internal static string FormatActorName(string? actorName, Guid actorId)
    {
        return string.IsNullOrWhiteSpace(actorName) ? VoyageTimeline.FormatActor(actorId) : actorName;
    }

    internal static string FormatPortLocal(DateTime? utc, TimeZoneInfo portZone)
    {
        ArgumentNullException.ThrowIfNull(portZone);

        if (utc is not { } stamp)
        {
            return MissingValue;
        }

        var instant = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant, portZone);

        return local.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            + $" LT (UTC{FormatOffset(portZone.GetUtcOffset(instant))})";
    }

    internal static string FormatRange(DateTime occurredAtUtc, DateTime? endedAtUtc)
    {
        return endedAtUtc is { } end
            ? $"{FormatUtc(occurredAtUtc)} – {FormatUtc(end)}"
            : FormatUtc(occurredAtUtc);
    }

    internal static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{absolute.Hours:00}:{absolute.Minutes:00}");
    }

    internal static TimeSpan ParseOffset(string offset)
    {
        ArgumentNullException.ThrowIfNull(offset);

        var sign = offset.StartsWith('-') ? -1 : 1;
        var parts = offset.TrimStart('+', '-').Split(':');

        return TimeSpan.FromMinutes(sign * ((int.Parse(parts[0], CultureInfo.InvariantCulture) * 60)
            + int.Parse(parts[1], CultureInfo.InvariantCulture)));
    }

    internal static DateTimeOffset? Compose(
        DateOnly? date,
        TimeOnly time,
        string offset,
        TimeZoneInfo? portZone)
    {
        if (date is not { } day)
        {
            return null;
        }

        var local = day.ToDateTime(time);

        return string.Equals(offset, PortOffset, StringComparison.Ordinal) && portZone is not null
            ? new DateTimeOffset(local, portZone.GetUtcOffset(local))
            : new DateTimeOffset(local, ParseOffset(
                string.Equals(offset, PortOffset, StringComparison.Ordinal) ? UtcOffset : offset));
    }

    internal static TimeZoneInfo? ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<RevisionChange> Changes(
        PortCallActivityRevisionModel? previous,
        PortCallActivityRevisionModel current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (previous is null)
        {
            return [];
        }

        var changes = new List<RevisionChange>();

        if (!string.Equals(previous.ActivityType, current.ActivityType, StringComparison.Ordinal))
        {
            changes.Add(new RevisionChange(
                "Activity",
                ActivityTypeLabel(previous.ActivityType),
                ActivityTypeLabel(current.ActivityType)));
        }

        if (previous.OccurredAtUtc != current.OccurredAtUtc)
        {
            changes.Add(new RevisionChange(
                "Occurred",
                FormatUtc(previous.OccurredAtUtc),
                FormatUtc(current.OccurredAtUtc)));
        }

        if (previous.EndedAtUtc != current.EndedAtUtc)
        {
            changes.Add(new RevisionChange(
                "Ended",
                FormatUtc(previous.EndedAtUtc),
                FormatUtc(current.EndedAtUtc)));
        }

        if (!string.Equals(previous.Notes ?? string.Empty, current.Notes ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add(new RevisionChange(
                "Notes",
                string.IsNullOrWhiteSpace(previous.Notes) ? MissingValue : previous.Notes,
                string.IsNullOrWhiteSpace(current.Notes) ? MissingValue : current.Notes));
        }

        return changes;
    }

    internal string FormatStamp(DateTime? utc)
    {
        if (utc is not { } stamp || portZone is null)
        {
            return FormatUtc(utc);
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(stamp, DateTimeKind.Utc), portZone);
        var localFormat = local.Date == stamp.Date ? "HH:mm" : "dd MMM HH:mm";

        return $"{FormatUtc(stamp)} · {local.ToString(localFormat, CultureInfo.InvariantCulture)} local";
    }

    internal string FormatStampRange(DateTime occurredAtUtc, DateTime? endedAtUtc)
    {
        return endedAtUtc is { } end
            ? $"{FormatStamp(occurredAtUtc)} – {FormatStamp(end)}"
            : FormatStamp(occurredAtUtc);
    }

    internal string OffsetLabel(string offset)
    {
        return string.Equals(offset, PortOffset, StringComparison.Ordinal) && timeline?.PortTimeZone is { } zoneId
            ? $"Port local time ({zoneId})"
            : $"UTC{offset}";
    }

    internal string? ConversionPreview(DateOnly? date, TimeOnly time)
    {
        if (Compose(date, time, formOffset, portZone) is not { } composed)
        {
            return null;
        }

        return $"Recorded as {FormatUtc(composed.UtcDateTime)} (UTC{FormatOffset(composed.Offset)}).";
    }

    internal IReadOnlyList<string> OffsetOptions =>
        portZone is null ? Offsets : [PortOffset, .. Offsets];

    internal PortCallActivityModel? FindActivity(Guid? activityId)
    {
        return activityId is { } id
            ? timeline?.Activities.FirstOrDefault(activity => activity.ActivityId == id)
            : null;
    }

    internal PortCallActivityDetails? HistoryFor(Guid activityId)
    {
        return historyCache.TryGetValue(activityId, out var details) ? details : null;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedStopId == StopId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedStopId = StopId;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedStopId = StopId;
        isLoading = true;
        failure = null;

        try
        {
            var timelineResult = await VoyagesApi.GetPortCallTimelineAsync(
                VoyageId,
                requestedStopId,
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested || requestedStopId != StopId)
            {
                return;
            }

            if (!timelineResult.IsSuccess)
            {
                failure = timelineResult.Failure;
                timeline = null;
                statement = null;
                isLoading = false;

                return;
            }

            timeline = timelineResult.Value;
            portZone = ResolveZone(timeline?.PortTimeZone);
            statement = null;

            if (timeline?.Statement is not null)
            {
                var statementResult = await VoyagesApi.GetPortCallStatementAsync(
                    VoyageId,
                    requestedStopId,
                    disposeCancellation.Token);

                if (disposeCancellation.IsCancellationRequested || requestedStopId != StopId)
                {
                    return;
                }

                if (!statementResult.IsSuccess)
                {
                    failure = statementResult.Failure;
                    isLoading = false;

                    return;
                }

                statement = statementResult.Value;
            }

            historyCache.Clear();
            isLoading = false;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task ToggleHistoryAsync(Guid activityId)
    {
        historyFailure = null;

        if (historyActivityId == activityId)
        {
            historyActivityId = null;

            return;
        }

        historyActivityId = activityId;

        if (historyCache.ContainsKey(activityId))
        {
            return;
        }

        try
        {
            var result = await VoyagesApi.GetPortCallActivityAsync(
                VoyageId,
                StopId,
                activityId,
                disposeCancellation.Token);

            if (result.IsSuccess && result.Value is { } details)
            {
                historyCache[activityId] = details;
            }
            else
            {
                historyFailure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
    }

    private void BeginRecord()
    {
        ResetForm();
        activeAction = RecordAction;

        var now = ToFormTime(DateTime.UtcNow);
        formOccurredDate = DateOnly.FromDateTime(now);
        formOccurredTime = new TimeOnly(now.Hour, now.Minute);
    }

    private void BeginCorrect(PortCallActivityModel activity)
    {
        ResetForm();
        activeAction = CorrectAction;
        activeActivityId = activity.ActivityId;
        formActivityType = activity.ActivityType;

        var occurred = ToFormTime(activity.OccurredAtUtc);
        formOccurredDate = DateOnly.FromDateTime(occurred);
        formOccurredTime = TimeOnly.FromTimeSpan(occurred.TimeOfDay);

        if (activity.EndedAtUtc is { } endedAtUtc)
        {
            var ended = ToFormTime(endedAtUtc);
            formEndedDate = DateOnly.FromDateTime(ended);
            formEndedTime = TimeOnly.FromTimeSpan(ended.TimeOfDay);
        }

        formNotes = activity.Notes ?? string.Empty;
    }

    private void CorrectFromBlocker(Guid? activityId)
    {
        if (!CanRecord || FindActivity(activityId) is not { } activity || activity.IsLocked)
        {
            return;
        }

        BeginCorrect(activity);
        focusActivityForm = true;
    }

    private DateTime ToFormTime(DateTime utc)
    {
        var stamp = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return portZone is null ? stamp : TimeZoneInfo.ConvertTimeFromUtc(stamp, portZone);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!focusActivityForm || activityFormFirstField.Id is null)
        {
            return;
        }

        focusActivityForm = false;

        await activityFormFirstField.FocusAsync();
    }

    private void BeginVoid(PortCallActivityModel activity)
    {
        ResetForm();
        activeAction = VoidAction;
        activeActivityId = activity.ActivityId;
    }

    private void BeginFinalize()
    {
        ResetForm();
        activeAction = FinalizeAction;
    }

    private void CancelAction(KeyboardEventArgs? args = null)
    {
        if (args is not null && !string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            return;
        }

        ResetForm();
    }

    private void ResetForm()
    {
        activeAction = null;
        activeActivityId = null;
        actionFailure = null;
        actionValidationMessage = null;
        formActivityType = "AllFast";
        formOccurredDate = null;
        formOccurredTime = TimeOnly.MinValue;
        formEndedDate = null;
        formEndedTime = TimeOnly.MinValue;
        formOffset = DefaultOffset;
        formNotes = string.Empty;
        formReason = string.Empty;
    }

    private bool TryBuildActivityValues(
        out DateTimeOffset occurredAt,
        out DateTimeOffset? endedAt,
        out string? notes)
    {
        occurredAt = default;
        endedAt = Compose(formEndedDate, formEndedTime, formOffset, portZone);
        notes = string.IsNullOrWhiteSpace(formNotes) ? null : formNotes.Trim();

        if (!ActivityTypes.Contains(formActivityType, StringComparer.Ordinal))
        {
            actionValidationMessage = "Choose an activity type.";

            return false;
        }

        if (Compose(formOccurredDate, formOccurredTime, formOffset, portZone) is not { } start)
        {
            actionValidationMessage = "Occurrence date and time are required.";

            return false;
        }

        occurredAt = start;
        var now = DateTimeOffset.UtcNow;

        if (start > now || endedAt > now)
        {
            actionValidationMessage = "Actual events cannot be in the future.";

            return false;
        }

        if (endedAt is { } end && end < start)
        {
            actionValidationMessage = "End time must not precede the occurrence time.";

            return false;
        }

        return true;
    }

    private async Task SubmitRecordAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (!TryBuildActivityValues(out var occurredAt, out var endedAt, out var notes))
        {
            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.RecordPortCallActivityAsync(
                VoyageId,
                StopId,
                new RecordPortCallActivityWebRequest(formActivityType, occurredAt, endedAt, notes),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Activity recorded.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitCorrectAsync()
    {
        if (isSubmitting || FindActivity(activeActivityId) is not { } activity)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (!TryBuildActivityValues(out var occurredAt, out var endedAt, out var notes))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(formReason))
        {
            actionValidationMessage = "Correction reason is required.";

            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.CorrectPortCallActivityAsync(
                VoyageId,
                StopId,
                activity.ActivityId,
                new CorrectPortCallActivityWebRequest(
                    formActivityType,
                    occurredAt,
                    endedAt,
                    notes,
                    formReason.Trim(),
                    activity.Version),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Activity corrected. The earlier version is kept in its history.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitVoidAsync()
    {
        if (isSubmitting || FindActivity(activeActivityId) is not { } activity)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (string.IsNullOrWhiteSpace(formReason))
        {
            actionValidationMessage = "Void reason is required.";

            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.VoidPortCallActivityAsync(
                VoyageId,
                StopId,
                activity.ActivityId,
                new VoidPortCallActivityWebRequest(formReason.Trim(), activity.Version),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Activity voided. It stays in the history.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task StartStatementAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        ResetForm();
        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.CreatePortCallStatementAsync(
                VoyageId,
                StopId,
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Statement draft started. Review the preview before finalizing.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitFinalizeAsync()
    {
        if (isSubmitting || statement is not { } draft)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;
        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.FinalizePortCallStatementAsync(
                VoyageId,
                StopId,
                new FinalizePortCallStatementWebRequest(draft.Version, draft.SourceVersion),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Statement finalized. It can no longer be changed.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task AfterActionAsync(VoyagesApiFailure? actionResultFailure, string successMessage)
    {
        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        actionSuccess = null;

        if (actionResultFailure is not null)
        {
            actionFailure = actionResultFailure;

            if (actionResultFailure.IsConflict || actionResultFailure.IsNotFound)
            {
                await LoadAsync();
            }

            return;
        }

        actionSuccess = successMessage;
        ResetForm();

        await LoadAsync();

        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }

    internal sealed record RevisionChange(string Field, string Before, string After);
}
