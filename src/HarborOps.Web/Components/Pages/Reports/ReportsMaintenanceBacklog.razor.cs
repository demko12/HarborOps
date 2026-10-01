using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class ReportsMaintenanceBacklog : IDisposable
{
    private static readonly string[] MaintenancePriorities = ["Low", "Medium", "High", "Critical"];
    private static readonly string[] MaintenanceStatuses = ["Open", "InProgress", "Completed", "Canceled"];

    private const string OpenStatus = "Open";
    private const string InProgressStatus = "InProgress";
    private const string CriticalPriority = "Critical";
    internal const int OverdueSampleSize = 100;

    private MaintenanceBacklogOverview? overview;

    internal sealed record MaintenanceBacklogOverview(
        int Open,
        int Overdue,
        int Critical,
        int CriticalOverdue,
        int? AverageOverdueDays,
        bool AverageIsSampled);

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private DateOnly asOfFilter = DateOnly.FromDateTime(DateTime.UtcNow);

    private Guid? vesselIdFilter;
    private int vesselPickerResetToken;

    private string? maintenancePriorityFilter;
    private string? maintenanceStatusFilter;

    private string? maintenanceOverdueSelection;
    private DateOnly? maintenanceDueFromFilter;
    private DateOnly? maintenanceDueToFilter;

    private bool? MaintenanceOverdueFilter => maintenanceOverdueSelection switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool isReportDisplayed;
    private ReportsApiFailure? failure;
    private string? filterValidationMessage;
    private DateOnly reportAsOf;

    private IReadOnlyList<MaintenanceBacklogReportRow> maintenanceRows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();

    private bool IsRefreshing => isLoading && isReportDisplayed;

    private int TotalPages => totalCount == 0 ? 1 : ((totalCount + Reports.PageSize - 1) / Reports.PageSize);

    private int FirstRowNumber => maintenanceRows.Count == 0 ? 0 : ((pageNumber - 1) * Reports.PageSize) + 1;

    private int LastRowNumber => FirstRowNumber + maintenanceRows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        if (!AuthorizationState.CanAccessMaintenance)
        {
            isLoading = false;

            return Task.CompletedTask;
        }

        return LoadAsync();
    }

    private Task SearchAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    // VesselPicker exposes Value/ValueChanged rather than a plain bindable field (its
    // selection comes from an async search, not a keystroke), so it can't use @bind:after
    // the way the native inputs above do - this plays the same role, assigning the field
    // and then reloading, mirroring Compliance.razor's SelectVesselAsync.
    private Task OnVesselFilterChangedAsync(Guid? vesselId)
    {
        vesselIdFilter = vesselId;

        return SearchAsync();
    }

    private Task ResetFiltersAsync()
    {
        vesselIdFilter = null;
        vesselPickerResetToken++;
        maintenancePriorityFilter = null;
        maintenanceStatusFilter = null;
        maintenanceOverdueSelection = null;
        maintenanceDueFromFilter = null;
        maintenanceDueToFilter = null;
        pageNumber = 1;
        filterValidationMessage = null;

        return LoadAsync();
    }

    private Task RetryAsync() => LoadAsync();

    private Task PreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        pageNumber--;

        return LoadAsync();
    }

    private Task NextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return Task.CompletedTask;
        }

        pageNumber++;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        filterValidationMessage = Reports.ValidateRange(maintenanceDueFromFilter, maintenanceDueToFilter);

        if (filterValidationMessage is not null)
        {
            return;
        }

        await LoadMaintenanceBacklogAsync();

        if (!isLoading)
        {
            isReportDisplayed = failure is null;
        }
    }

    private async Task LoadMaintenanceBacklogAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new MaintenanceBacklogReportQuery(
            asOfFilter, vesselIdFilter, maintenancePriorityFilter, maintenanceStatusFilter,
            MaintenanceOverdueFilter, maintenanceDueFromFilter, maintenanceDueToFilter, pageNumber, Reports.PageSize);

        var overviewTask = LoadOverviewAsync(
            query with { Priority = null, Status = null, Overdue = null, PageNumber = 1, PageSize = 1 },
            cancellationToken);

        ReportsApiResult<MaintenanceBacklogReportPage> result;

        try
        {
            result = await ReportsApi.GetMaintenanceBacklogAsync(query, cancellationToken);
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
            maintenanceRows = [];
            totalCount = 0;
            overview = null;

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        maintenanceRows = page.Items;

        MaintenanceBacklogOverview? loadedOverview;

        try
        {
            loadedOverview = await overviewTask;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        overview = loadedOverview;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

    private async Task<MaintenanceBacklogOverview?> LoadOverviewAsync(
        MaintenanceBacklogReportQuery baseQuery, CancellationToken cancellationToken)
    {
        Task<ReportsApiResult<MaintenanceBacklogReportPage>> Fetch(MaintenanceBacklogReportQuery query) =>
            ReportsApi.GetMaintenanceBacklogAsync(query, cancellationToken);

        var openTask = Fetch(baseQuery with { Status = OpenStatus });
        var inProgressTask = Fetch(baseQuery with { Status = InProgressStatus });
        var overdueTask = Fetch(baseQuery with { Overdue = true, PageSize = OverdueSampleSize });
        var criticalOpenTask = Fetch(baseQuery with { Priority = CriticalPriority, Status = OpenStatus });
        var criticalInProgressTask = Fetch(baseQuery with { Priority = CriticalPriority, Status = InProgressStatus });
        var criticalOverdueTask = Fetch(baseQuery with { Priority = CriticalPriority, Overdue = true });

        var open = await openTask;
        var inProgress = await inProgressTask;
        var overdue = await overdueTask;
        var criticalOpen = await criticalOpenTask;
        var criticalInProgress = await criticalInProgressTask;
        var criticalOverdue = await criticalOverdueTask;

        if (!open.IsSuccess || !inProgress.IsSuccess || !overdue.IsSuccess
            || !criticalOpen.IsSuccess || !criticalInProgress.IsSuccess || !criticalOverdue.IsSuccess)
        {
            return null;
        }

        var overduePage = overdue.Value!;
        var asOf = overduePage.AsOf;

        return new MaintenanceBacklogOverview(
            open.Value!.TotalCount + inProgress.Value!.TotalCount,
            overduePage.TotalCount,
            criticalOpen.Value!.TotalCount + criticalInProgress.Value!.TotalCount,
            criticalOverdue.Value!.TotalCount,
            AverageOverdueDays(overduePage.Items, asOf),
            overduePage.TotalCount > overduePage.Items.Count);
    }

    internal static int? AverageOverdueDays(IReadOnlyList<MaintenanceBacklogReportRow> overdueRows, DateOnly asOf) =>
        overdueRows.Count == 0
            ? null
            : (int)Math.Round(overdueRows.Average(row => asOf.DayNumber - DateOnly.FromDateTime(row.DueUtc).DayNumber));

    private async Task ResolveVesselNamesAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken, int requestVersion)
    {
        var distinctIds = ids.Distinct().ToArray();
        var names = await FleetApi.LookupVesselNamesAsync(distinctIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        vesselNames = names;
    }

    private string VesselDisplayName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : "Unknown vessel";

    // Mirrors Maintenance.razor's MaintenanceFormat priority/status colors exactly, so a
    // work order reads the same color here as it does on the Maintenance module's own page.
    private static string MaintenancePriorityClass(string priority) =>
        priority.Trim().ToUpperInvariant() switch
        {
            "CRITICAL" => "reports-status-expired",
            "HIGH" => "reports-status-warning",
            "MEDIUM" => "reports-status-active",
            "LOW" => "reports-status-low",
            _ => "reports-status-neutral",
        };

    private static string MaintenanceStatusClass(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "OPEN" => "reports-status-active",
            "INPROGRESS" => "reports-status-warning",
            "COMPLETED" => "reports-status-completed",
            "CANCELED" => "reports-status-expired",
            _ => "reports-status-neutral",
        };

    private string BuildDownloadUrl()
    {
        var query = new List<string> { $"asOf={asOfFilter:yyyy-MM-dd}" };

        if (vesselIdFilter is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(maintenancePriorityFilter))
        {
            query.Add($"priority={Uri.EscapeDataString(maintenancePriorityFilter)}");
        }

        if (!string.IsNullOrWhiteSpace(maintenanceStatusFilter))
        {
            query.Add($"status={Uri.EscapeDataString(maintenanceStatusFilter)}");
        }

        if (MaintenanceOverdueFilter is { } overdue)
        {
            query.Add($"overdue={(overdue ? "true" : "false")}");
        }

        if (maintenanceDueFromFilter is { } dueFrom)
        {
            query.Add($"dueFrom={dueFrom:yyyy-MM-dd}");
        }

        if (maintenanceDueToFilter is { } dueTo)
        {
            query.Add($"dueTo={dueTo:yyyy-MM-dd}");
        }

        return $"/reports/download/maintenance-backlog?{string.Join('&', query)}";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
