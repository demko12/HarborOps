using System.Globalization;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class Voyages
{
    internal const string PlannedStatus = "Planned";
    internal const string InProgressStatus = "InProgress";
    internal const string CompletedStatus = "Completed";
    internal const string MissingValue = "—";
    internal const int DefaultWindowDays = 30;
    internal const int ImminentDepartureHours = 24;

    internal const string ForbiddenTitle = "You do not have access to voyages";

    internal const string ForbiddenMessage =
        "Your account is missing the voyages read permission. Ask an administrator to grant it.";

    private static readonly string[] StatusOptions =
        [PlannedStatus, InProgressStatus, CompletedStatus];

    internal const string DelayedStatus = "Delayed";
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const string DepartAction = "depart";
    internal const string ArriveAction = "arrive";

    private readonly List<VoyageListItemModel> voyages = [];
    private readonly Dictionary<string, int?> statusCounts = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource actionCancellation = new();

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private DateOnly fromDate = DefaultFrom();
    private DateOnly toDate = DefaultTo();
    private string statusFilter = string.Empty;
    private string? searchTerm;
    private DateOnly appliedFrom = DefaultFrom();
    private DateOnly appliedTo = DefaultTo();
    private string appliedStatus = string.Empty;
    private string? appliedSearch;
    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;

    private Guid? actionVoyageId;
    private string? actionKind;
    private string? actionVoyageNumber;
    private DateOnly actionDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private TimeOnly actionTime = TimeOnly.FromTimeSpan(DateTime.UtcNow.TimeOfDay);
    private DateOnly? actionMinDate;
    private DateOnly actionMaxDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private ElementReference actionDateInput;
    private bool shouldFocusAction;
    private VoyagesApiFailure? actionFailure;
    private string? actionSuccess;
    private bool isActionSubmitting;
    private bool CanManageVoyages => AuthorizationState.HasPermission(ManageVoyagesPermission);

    private int TotalPages => totalCount == 0
        ? 1
        : (int)Math.Ceiling(totalCount / (double)pageSize);

    private int FirstRowNumber => totalCount == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => Math.Min(pageNumber * pageSize, totalCount);

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!shouldFocusAction)
        {
            return;
        }

        shouldFocusAction = false;

        await actionDateInput.FocusAsync();
    }

    internal static string FormatRoute(string departurePortCode, string arrivalPortCode)
    {
        return $"{departurePortCode} → {arrivalPortCode}";
    }
    internal static string FormatCount(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? MissingValue;
    }
    internal static string FormatDate(DateOnly value)
    {
        return value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }
    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }
    internal static string FormatVesselReference(Guid vesselId)
    {
        return vesselId.ToString("N", CultureInfo.InvariantCulture)[..8];
    }
    internal static int StatusStep(string status)
    {
        return status switch
        {
            CompletedStatus => 3,
            InProgressStatus => 2,
            _ => 1,
        };
    }
    internal static string StatusModifier(string status)
    {
        return status switch
        {
            CompletedStatus => "completed",
            InProgressStatus => "in-progress",
            _ => "planned",
        };
    }
    internal static bool IsImminentDeparture(DateTime plannedDeparture, DateTime utcNow)
    {
        return plannedDeparture >= utcNow
            && plannedDeparture <= utcNow.AddHours(ImminentDepartureHours);
    }
    internal static string FailureTitle(VoyagesApiFailure failure)
    {
        return failure.IsForbidden ? ForbiddenTitle : failure.Title;
    }

    internal static string FailureMessage(VoyagesApiFailure failure)
    {
        return failure.IsForbidden ? ForbiddenMessage : failure.Message;
    }

    internal static DateTime CombineUtc(DateOnly date, TimeOnly time)
    {
        return DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);
    }

    internal static bool CanDepart(string status)
    {
        return string.Equals(status, PlannedStatus, StringComparison.Ordinal);
    }

    internal static bool CanArrive(string status)
    {
        return string.Equals(status, InProgressStatus, StringComparison.Ordinal);
    }

    internal static string FormatIsoDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static string ActionLabel(string kind, string? voyageNumber)
    {
        var action = string.Equals(kind, ArriveAction, StringComparison.Ordinal) ? "arrival" : "departure";

        return $"Record {action} for {voyageNumber} (UTC)";
    }

    /// an arrival can never precede its departure, so the picker refuses those days
    /// outright; the API stays the authority on the exact timestamp
    internal static DateOnly? EarliestActionDate(VoyageListItemModel voyage, string kind)
    {
        return string.Equals(kind, ArriveAction, StringComparison.Ordinal)
            && voyage.ActualDeparture is { } actualDeparture
            ? DateOnly.FromDateTime(actualDeparture)
            : null;
    }

    private void BeginAction(VoyageListItemModel voyage, string kind)
    {
        actionVoyageId = voyage.VoyageId;
        actionVoyageNumber = voyage.VoyageNumber;
        actionKind = kind;
        actionFailure = null;
        actionSuccess = null;
        actionMaxDate = DateOnly.FromDateTime(DateTime.UtcNow);
        actionMinDate = EarliestActionDate(voyage, kind);
        actionDate = actionMinDate is { } earliest && earliest > actionMaxDate ? earliest : actionMaxDate;
        actionTime = TimeOnly.FromTimeSpan(DateTime.UtcNow.TimeOfDay);
        shouldFocusAction = true;
    }

    private void HandleActionKeyDown(KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            CancelAction();
        }
    }

    private void CancelAction()
    {
        actionVoyageId = null;
        actionKind = null;
        actionVoyageNumber = null;
        actionFailure = null;
    }

    private async Task ConfirmActionAsync()
    {
        if (isActionSubmitting || actionVoyageId is not { } voyageId || actionKind is null)
        {
            return;
        }

        isActionSubmitting = true;
        actionFailure = null;
        actionSuccess = null;

        try
        {
            var stamp = CombineUtc(actionDate, actionTime);
            var voyageNumber = actionVoyageNumber;

            var result = string.Equals(actionKind, DepartAction, StringComparison.Ordinal)
                ? await VoyagesApi.RecordDepartureAsync(
                    voyageId,
                    new RecordDepartureWebRequest(stamp),
                    actionCancellation.Token)
                : await VoyagesApi.RecordArrivalAsync(
                    voyageId,
                    new RecordArrivalWebRequest(stamp),
                    actionCancellation.Token);

            if (actionCancellation.IsCancellationRequested)
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

            actionSuccess = $"{voyageNumber} is now {result.Value!.Status}.";
            CancelAction();

            await LoadAsync();
        }
        finally
        {
            isActionSubmitting = false;
        }
    }
    private static DateOnly DefaultFrom()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static DateOnly DefaultTo()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow).AddDays(DefaultWindowDays);
    }

    private async Task ApplyFiltersAsync()
    {
        appliedFrom = fromDate;
        appliedTo = toDate;
        appliedStatus = statusFilter;
        appliedSearch = searchTerm;
        pageNumber = 1;

        await LoadAsync();
    }
    private async Task ResetFiltersAsync()
    {
        fromDate = DefaultFrom();
        toDate = DefaultTo();
        statusFilter = string.Empty;
        searchTerm = null;

        await ApplyFiltersAsync();
    }
    private async Task GoToPreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return;
        }

        pageNumber--;
        await LoadAsync();
    }
    private async Task GoToNextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return;
        }

        pageNumber++;
        await LoadAsync();
    }
    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;

        isLoading = true;
        failure = null;

        VoyagesApiResult<VoyagePage> result;
        try
        {
            result = await VoyagesApi.GetVoyagesAsync(
                appliedFrom,
                appliedTo,
                appliedSearch,
                appliedStatus,
                true,
                pageNumber,
                pageSize,
                cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            voyages.Clear();
            totalCount = 0;
            statusCounts.Clear();
            return;
        }

        var page = result.Value!;
        voyages.Clear();
        voyages.AddRange(page.Items);
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;

        await LoadStatusCountsAsync(cancellationToken, requestVersion);
    }
    private async Task LoadStatusCountsAsync(CancellationToken cancellationToken, int requestVersion)
    {
        foreach (var status in StatusOptions)
        {
            VoyagesApiResult<VoyagePage> result;
            try
            {
                result = await VoyagesApi.GetVoyagesAsync(
                    appliedFrom,
                    appliedTo,
                    appliedSearch,
                    status,
                    true,
                    1,
                    1,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
            {
                return;
            }

            statusCounts[status] = result.IsSuccess ? result.Value!.TotalCount : null;
        }
    }

    private int? StatusCount(string status)
    {
        return statusCounts.TryGetValue(status, out var count) ? count : null;
    }
    public void Dispose()
    {
        actionCancellation.Cancel();
        actionCancellation.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
