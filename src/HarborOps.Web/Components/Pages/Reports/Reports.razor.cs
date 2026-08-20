using HarborOps.Web.Api;
using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class Reports : IDisposable
{
    private const int PageSize = 25;

    private static readonly string[] ComplianceCategories = ["Certificate", "Insurance", "Detention", "NonConformity"];
    private static readonly string[] DocumentStatuses = ["Active", "Valid", "Expired", "Suspended"];
    private static readonly string[] MaintenancePriorities = ["Low", "Medium", "High", "Critical"];
    private static readonly string[] MaintenanceStatuses = ["Open", "InProgress", "Completed", "Canceled"];

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private ReportKind selectedReport = ReportKind.DocumentsExpiry;
    private DateOnly asOfFilter = DateOnly.FromDateTime(DateTime.UtcNow);

    private Guid? vesselIdFilter;
    private int vesselPickerResetToken;

    private DateOnly? documentsFromFilter;
    private DateOnly? documentsToFilter;
    private string? documentsStatusFilter;

    private string? complianceCategoryFilter;

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
    private ReportsApiFailure? failure;
    private string? filterValidationMessage;
    private DateOnly reportAsOf;

    private IReadOnlyList<DocumentsExpiryReportRow> documentsRows = [];
    private IReadOnlyList<ComplianceExposureReportRow> complianceRows = [];
    private IReadOnlyList<MaintenanceBacklogReportRow> maintenanceRows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();

    private bool HasAnyReportAccess =>
        AuthorizationState.CanAccessDocuments || AuthorizationState.CanAccessCompliance || AuthorizationState.CanAccessMaintenance;

    private int CurrentRowCount => selectedReport switch
    {
        ReportKind.DocumentsExpiry => documentsRows.Count,
        ReportKind.ComplianceExposure => complianceRows.Count,
        ReportKind.MaintenanceBacklog => maintenanceRows.Count,
        _ => 0,
    };

    private int TotalPages => totalCount == 0 ? 1 : ((totalCount + PageSize - 1) / PageSize);

    private int FirstRowNumber => CurrentRowCount == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;

    private int LastRowNumber => FirstRowNumber + CurrentRowCount - 1;

    private string ReportTitle => selectedReport switch
    {
        ReportKind.DocumentsExpiry => "Documents expiry register",
        ReportKind.ComplianceExposure => "Compliance exposure register",
        ReportKind.MaintenanceBacklog => "Maintenance backlog register",
        _ => "Report",
    };

    protected override Task OnInitializedAsync()
    {
        if (!AuthorizationState.CanAccessDocuments)
        {
            selectedReport = AuthorizationState.CanAccessCompliance
                ? ReportKind.ComplianceExposure
                : ReportKind.MaintenanceBacklog;
        }

        if (!HasAnyReportAccess)
        {
            isLoading = false;

            return Task.CompletedTask;
        }

        return LoadAsync();
    }

    private Task SelectReportAsync(ReportKind kind)
    {
        if (selectedReport == kind)
        {
            return Task.CompletedTask;
        }

        selectedReport = kind;
        pageNumber = 1;
        filterValidationMessage = null;

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

        switch (selectedReport)
        {
            case ReportKind.DocumentsExpiry:
                documentsFromFilter = null;
                documentsToFilter = null;
                documentsStatusFilter = null;
                break;
            case ReportKind.ComplianceExposure:
                complianceCategoryFilter = null;
                break;
            case ReportKind.MaintenanceBacklog:
                maintenancePriorityFilter = null;
                maintenanceStatusFilter = null;
                maintenanceOverdueSelection = null;
                maintenanceDueFromFilter = null;
                maintenanceDueToFilter = null;
                break;
        }

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

    private Task LoadAsync()
    {
        filterValidationMessage = selectedReport switch
        {
            ReportKind.DocumentsExpiry => ValidateRange(documentsFromFilter, documentsToFilter),
            ReportKind.MaintenanceBacklog => ValidateRange(maintenanceDueFromFilter, maintenanceDueToFilter),
            _ => null,
        };

        if (filterValidationMessage is not null)
        {
            return Task.CompletedTask;
        }

        return selectedReport switch
        {
            ReportKind.DocumentsExpiry => LoadDocumentsExpiryAsync(),
            ReportKind.ComplianceExposure => LoadComplianceExposureAsync(),
            ReportKind.MaintenanceBacklog => LoadMaintenanceBacklogAsync(),
            _ => Task.CompletedTask,
        };
    }

    private async Task LoadDocumentsExpiryAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new DocumentsExpiryReportQuery(
            asOfFilter, vesselIdFilter, null, documentsFromFilter, documentsToFilter,
            documentsStatusFilter, pageNumber, PageSize);

        ReportsApiResult<DocumentsExpiryReportPage> result;

        try
        {
            result = await ReportsApi.GetDocumentsExpiryAsync(query, cancellationToken);
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
            documentsRows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        documentsRows = page.Items;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

    private async Task LoadComplianceExposureAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new ComplianceExposureReportQuery(
            asOfFilter, vesselIdFilter, complianceCategoryFilter, pageNumber, PageSize);

        ReportsApiResult<ComplianceExposureReportPage> result;

        try
        {
            result = await ReportsApi.GetComplianceExposureAsync(query, cancellationToken);
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
            complianceRows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        complianceRows = page.Items;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

    private async Task LoadMaintenanceBacklogAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new MaintenanceBacklogReportQuery(
            asOfFilter, vesselIdFilter, maintenancePriorityFilter, maintenanceStatusFilter,
            MaintenanceOverdueFilter, maintenanceDueFromFilter, maintenanceDueToFilter, pageNumber, PageSize);

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

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        maintenanceRows = page.Items;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

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
        vesselNames.TryGetValue(vesselId, out var name) ? name : $"Vessel {vesselId.ToString()[..8]}";

    private static string DocumentStatusLabel(DocumentsExpiryReportRow row) =>
        row.IsExpired ? "Expired" : row.Status;

    private static string DaysUntilExpiryLabel(DocumentsExpiryReportRow row)
    {
        var days = Math.Abs(row.DaysUntilExpiry);
        var unit = days == 1 ? "day" : "days";

        if (row.IsExpired)
        {
            return $"{days} {unit} overdue";
        }

        return row.DaysUntilExpiry == 0 ? "Expires today" : $"{days} {unit} left";
    }

    private static string? ValidateRange(DateOnly? from, DateOnly? to) =>
        from is { } fromDate && to is { } toDate && toDate < fromDate
            ? "The \"to\" date must be on or after the \"from\" date."
            : null;

    private string BuildDownloadUrl() => selectedReport switch
    {
        ReportKind.DocumentsExpiry => BuildDocumentsExpiryDownloadUrl(),
        ReportKind.ComplianceExposure => BuildComplianceExposureDownloadUrl(),
        ReportKind.MaintenanceBacklog => BuildMaintenanceBacklogDownloadUrl(),
        _ => "#",
    };

    private string BuildDocumentsExpiryDownloadUrl()
    {
        var query = new List<string> { $"asOf={asOfFilter:yyyy-MM-dd}" };

        if (vesselIdFilter is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (documentsFromFilter is { } from)
        {
            query.Add($"from={from:yyyy-MM-dd}");
        }

        if (documentsToFilter is { } to)
        {
            query.Add($"to={to:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(documentsStatusFilter))
        {
            query.Add($"status={Uri.EscapeDataString(documentsStatusFilter)}");
        }

        return $"/reports/download/documents-expiry?{string.Join('&', query)}";
    }

    private string BuildComplianceExposureDownloadUrl()
    {
        var query = new List<string> { $"asOf={asOfFilter:yyyy-MM-dd}" };

        if (vesselIdFilter is { } vesselId)
        {
            query.Add($"vesselId={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(complianceCategoryFilter))
        {
            query.Add($"category={Uri.EscapeDataString(complianceCategoryFilter)}");
        }

        return $"/reports/download/compliance-exposure?{string.Join('&', query)}";
    }

    private string BuildMaintenanceBacklogDownloadUrl()
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

    private enum ReportKind
    {
        DocumentsExpiry,
        ComplianceExposure,
        MaintenanceBacklog,
    }
}
