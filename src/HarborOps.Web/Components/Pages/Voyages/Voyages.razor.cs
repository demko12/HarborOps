using System.Globalization;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class Voyages
{
    internal const string PlannedStatus = "Planned";
    internal const string InProgressStatus = "InProgress";
    internal const string CompletedStatus = "Completed";
    internal const string CancelledStatus = "Cancelled";
    internal const string MissingValue = "—";
    internal const int DefaultWindowDays = 30;
    internal const int ImminentDepartureHours = 24;

    internal const string RoutesTab = "routes";

    internal const string RoutesTabUrl = $"/voyages?tab={RoutesTab}";

    internal const string ForbiddenTitle = "You do not have access to voyages";

    internal const string ForbiddenMessage =
        "Your account is missing the voyages read permission. Ask an administrator to grant it.";

    private static readonly string[] StatusOptions =
        [PlannedStatus, InProgressStatus, CompletedStatus];

    private static readonly string[] FilterStatusOptions =
        [PlannedStatus, InProgressStatus, CompletedStatus, CancelledStatus];

    internal const string DelayedStatus = "Delayed";
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const string DepartAction = "depart";
    internal const string ArriveAction = "arrive";
    internal const string CancelVoyageAction = "cancel";

    internal const int MasterDataKpiPortLookupTake = 50;
    internal const int MasterDataKpiRouteSampleSize = 100;

    private readonly List<VoyageListItemModel> voyages = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
    private readonly Dictionary<string, int?> statusCounts = new(StringComparer.Ordinal);
    private int? delayedKpiCount;
    private readonly CancellationTokenSource actionCancellation = new();

    private bool masterDataKpiRequested;
    private int? portsKpiCount;
    private int? berthsKpiCount;
    private int? routesKpiCount;
    private decimal? avgRouteDistanceKpi;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private VoyagesTab activeTab = VoyagesTab.Board;

    [SupplyParameterFromQuery(Name = "tab")]
    private string? RequestedTab { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    private DateOnly? FromDateFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    private DateOnly? ToDateFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    private string? StatusFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "search")]
    private string? SearchFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "page")]
    private int? PageFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "size")]
    private int? SizeFromQuery { get; set; }

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
    private ElementReference actionReasonInput;
    private string actionReason = string.Empty;
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
        ApplyQueryFilters();

        if (string.Equals(RequestedTab, RoutesTab, StringComparison.OrdinalIgnoreCase))
        {
            activeTab = VoyagesTab.Routes;
            masterDataKpiRequested = true;

            return Task.WhenAll(LoadAsync(), LoadMasterDataKpisAsync());
        }

        return LoadAsync();
    }

    private void ApplyQueryFilters()
    {
        if (FromDateFromQuery is { } from)
        {
            fromDate = from;
            appliedFrom = from;
        }

        if (ToDateFromQuery is { } to)
        {
            toDate = to;
            appliedTo = to;
        }

        if (!string.IsNullOrEmpty(StatusFromQuery))
        {
            statusFilter = StatusFromQuery;
            appliedStatus = StatusFromQuery;
        }

        if (!string.IsNullOrWhiteSpace(SearchFromQuery))
        {
            searchTerm = SearchFromQuery;
            appliedSearch = SearchFromQuery;
        }

        if (PageFromQuery is { } page && page > 0)
        {
            pageNumber = page;
        }

        if (SizeFromQuery is { } size && size > 0)
        {
            pageSize = size;
        }
    }

    private string BoardQuery
    {
        get
        {
            var parts = new List<string>();

            if (appliedFrom != DefaultFrom())
            {
                parts.Add($"from={FormatIsoDate(appliedFrom)}");
            }

            if (appliedTo != DefaultTo())
            {
                parts.Add($"to={FormatIsoDate(appliedTo)}");
            }

            if (!string.IsNullOrEmpty(appliedStatus))
            {
                parts.Add($"status={Uri.EscapeDataString(appliedStatus)}");
            }

            if (!string.IsNullOrWhiteSpace(appliedSearch))
            {
                parts.Add($"search={Uri.EscapeDataString(appliedSearch.Trim())}");
            }

            if (pageNumber > 1)
            {
                parts.Add($"page={pageNumber}");
            }

            if (pageSize != 25)
            {
                parts.Add($"size={pageSize}");
            }

            return string.Join('&', parts);
        }
    }

    private string TimelineHref(Guid voyageId)
    {
        var query = BoardQuery;

        return query.Length == 0
            ? $"/voyages/{voyageId}"
            : $"/voyages/{voyageId}?from={Uri.EscapeDataString(query)}";
    }

    private void OpenTimeline(Guid voyageId)
    {
        Navigation.NavigateTo(TimelineHref(voyageId));
    }

    private void HandleVoyageRowKeyDown(KeyboardEventArgs args, Guid voyageId)
    {
        if (args.Key is "Enter" or " ")
        {
            OpenTimeline(voyageId);
        }
    }

    private Task SelectTab(VoyagesTab tab)
    {
        activeTab = tab;

        if (masterDataKpiRequested || tab == VoyagesTab.Board)
        {
            return Task.CompletedTask;
        }

        masterDataKpiRequested = true;

        return LoadMasterDataKpisAsync();
    }

    private async Task LoadMasterDataKpisAsync()
    {
        var cancellationToken = disposeCancellation.Token;

        var portsTask = VoyagesApi.GetPortsAsync(null, null, 1, 1, cancellationToken);
        var routesTask = VoyagesApi.GetRoutesAsync(null, 1, MasterDataKpiRouteSampleSize, cancellationToken);
        var portLookupTask = VoyagesApi.LookupPortsAsync(null, MasterDataKpiPortLookupTake, cancellationToken);

        try
        {
            await Task.WhenAll(portsTask, routesTask, portLookupTask);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        var portsResult = portsTask.Result;
        var routesResult = routesTask.Result;
        var portLookupResult = portLookupTask.Result;

        portsKpiCount = portsResult.IsSuccess ? portsResult.Value!.TotalCount : null;

        if (routesResult.IsSuccess)
        {
            routesKpiCount = routesResult.Value!.TotalCount;
            var sampledRoutes = routesResult.Value!.Items;
            avgRouteDistanceKpi = sampledRoutes.Count > 0
                ? sampledRoutes.Average(route => route.EstimatedDistance)
                : 0m;
        }
        else
        {
            routesKpiCount = null;
            avgRouteDistanceKpi = null;
        }

        if (!portLookupResult.IsSuccess)
        {
            berthsKpiCount = null;

            return;
        }

        VoyagesApiResult<BerthPage>[] berthResults;

        try
        {
            berthResults = await Task.WhenAll(portLookupResult.Value!.Items
                .Select(port => VoyagesApi.GetBerthsAsync(port.PortId, null, null, 1, 1, cancellationToken)));
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        berthsKpiCount = berthResults.All(result => result.IsSuccess)
            ? berthResults.Sum(result => result.Value!.TotalCount)
            : null;
    }

    internal static string FormatAvgDistance(decimal? value)
    {
        return value is { } distance ? MasterDataFormat.DistanceNm(distance) : MissingValue;
    }

    private string AriaSelected(VoyagesTab tab)
    {
        return activeTab == tab ? "true" : "false";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!shouldFocusAction)
        {
            return;
        }

        shouldFocusAction = false;

        await (IsCancellingVoyage ? actionReasonInput : actionDateInput).FocusAsync();
    }

    internal static string FormatRoute(VoyageListItemModel voyage)
    {
        var departure = voyage.FirstStopPortName ?? voyage.DeparturePortCode;
        var arrival = voyage.LastStopPortName ?? voyage.ArrivalPortCode;

        return $"{departure} → {arrival}";
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
    private async Task ResolveVesselNamesAsync(
        IEnumerable<Guid> vesselIds,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var names = await FleetApi.LookupVesselNamesAsync(vesselIds.Distinct().ToArray(), cancellationToken);

        if (!disposeCancellation.IsCancellationRequested && requestVersion == loadVersion)
        {
            vesselNames = names;
        }
    }

    private string VesselDisplayName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : "Unknown vessel";
    internal static int StatusStep(string status)
    {
        return status switch
        {
            CompletedStatus => 3,
            InProgressStatus => 2,
            CancelledStatus => 0,
            _ => 1,
        };
    }
    internal static string StatusModifier(string status)
    {
        return status switch
        {
            CompletedStatus => "completed",
            InProgressStatus => "in-progress",
            CancelledStatus => "cancelled",
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

    internal static bool CanCancel(string status)
    {
        return string.Equals(status, PlannedStatus, StringComparison.Ordinal);
    }

    private bool IsCancellingVoyage =>
        string.Equals(actionKind, CancelVoyageAction, StringComparison.Ordinal);

    private string ConfirmLabel()
    {
        if (IsCancellingVoyage)
        {
            return isActionSubmitting ? "Cancelling..." : "Confirm cancellation";
        }

        return isActionSubmitting ? "Saving..." : "Confirm";
    }

    private string KeepLabel()
    {
        return IsCancellingVoyage ? "Keep voyage" : "Cancel";
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
        actionReason = string.Empty;
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
            var voyageNumber = actionVoyageNumber;

            if (IsCancellingVoyage)
            {
                var cancellation = await VoyagesApi.CancelVoyageAsync(
                    voyageId,
                    new CancelVoyageWebRequest(actionReason),
                    actionCancellation.Token);

                if (actionCancellation.IsCancellationRequested)
                {
                    return;
                }

                if (!cancellation.IsSuccess)
                {
                    actionFailure = cancellation.Failure;

                    if (cancellation.Failure!.IsConflict || cancellation.Failure.IsNotFound)
                    {
                        await LoadAsync();
                    }

                    return;
                }

                actionSuccess = $"{voyageNumber} is now {cancellation.Value!.Voyage.Status}.";
                CancelAction();

                await LoadAsync();

                return;
            }

            var stamp = CombineUtc(actionDate, actionTime);

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
            delayedKpiCount = null;
            return;
        }

        var page = result.Value!;
        voyages.Clear();
        voyages.AddRange(page.Items);
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;

        await ResolveVesselNamesAsync(page.Items.Select(voyage => voyage.VesselId), cancellationToken, requestVersion);
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

        VoyagesApiResult<VoyagePage> delayedResult;
        try
        {
            delayedResult = await VoyagesApi.GetDelayedVoyageCountAsync(
                appliedFrom,
                appliedTo,
                appliedSearch,
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

        delayedKpiCount = delayedResult.IsSuccess ? delayedResult.Value!.TotalCount : null;
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
