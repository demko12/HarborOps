using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class ReportsComplianceExposure : IDisposable
{
    private static readonly string[] ComplianceCategories = ["Certificate", "Insurance", "Detention", "NonConformity"];

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private DateOnly asOfFilter = DateOnly.FromDateTime(DateTime.UtcNow);

    private Guid? vesselIdFilter;
    private int vesselPickerResetToken;

    private string? complianceCategoryFilter;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool isReportDisplayed;
    private ReportsApiFailure? failure;
    private DateOnly reportAsOf;

    private IReadOnlyList<ComplianceExposureReportRow> complianceRows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();

    private bool IsRefreshing => isLoading && isReportDisplayed;

    private int TotalPages => totalCount == 0 ? 1 : ((totalCount + Reports.PageSize - 1) / Reports.PageSize);

    private int FirstRowNumber => complianceRows.Count == 0 ? 0 : ((pageNumber - 1) * Reports.PageSize) + 1;

    private int LastRowNumber => FirstRowNumber + complianceRows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        if (!AuthorizationState.CanAccessCompliance)
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
        complianceCategoryFilter = null;
        pageNumber = 1;

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
        await LoadComplianceExposureAsync();

        if (!isLoading)
        {
            isReportDisplayed = failure is null;
        }
    }

    private async Task LoadComplianceExposureAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new ComplianceExposureReportQuery(
            asOfFilter, vesselIdFilter, complianceCategoryFilter, pageNumber, Reports.PageSize);

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

    // "Detained"/"Expired"/"Revoked" are the most severe outcomes on this report (red, same as an
    // expired document); "Expiring" and an open non-conformity both mean "needs attention
    // soon" (amber) - matching the severity language used everywhere else in the app.
    private static string ComplianceStatusClass(ComplianceExposureReportRow row) =>
        row.Status.Trim().ToUpperInvariant() switch
        {
            "EXPIRED" or "DETAINED" or "REVOKED" => "reports-status-expired",
            "EXPIRING" or "OPEN" => "reports-status-warning",
            _ => "reports-status-neutral",
        };

    private static string SeverityClass(string severity) =>
        severity.Trim().ToUpperInvariant() switch
        {
            "CRITICAL" => "reports-status-expired",
            "MAJOR" => "reports-status-warning",
            "MINOR" => "reports-status-neutral",
            _ => "reports-status-neutral",
        };

    private string BuildDownloadUrl()
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

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
