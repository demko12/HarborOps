using System.Globalization;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class ReportsComplianceExposure : IDisposable
{
    private static readonly string[] ComplianceCategories = ["Certificate", "Insurance", "Detention", "NonConformity"];

    private const string ClosedClosureStatus = "Closed";

    private static readonly (string Category, string Label, string Note, string Icon)[] CategoryKpis =
    [
        ("Certificate", "Certificates", "Expired, revoked or expiring within 60 days", "bi-patch-check"),
        ("Insurance", "Insurance policies", "Expired or expiring within 60 days", "bi-shield-plus"),
        ("Detention", "Detentions", "PSC inspections with a detention", "bi-sign-stop"),
        ("NonConformity", "Open findings", "Non-conformities still open", "bi-exclamation-diamond"),
    ];

    private ComplianceExposureOverview? overview;

    internal sealed record ComplianceExposureOverview(
        IReadOnlyDictionary<string, int> CategoryCounts,
        ComplianceHealthRing? Insurance,
        ComplianceHealthRing? Findings);

    internal sealed record ComplianceHealthRing(int Part, int Whole);

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

        var overviewTask = LoadOverviewAsync(asOfFilter, vesselIdFilter, cancellationToken);

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
            overview = null;

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        complianceRows = page.Items;

        ComplianceExposureOverview? loadedOverview;

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

    private async Task<ComplianceExposureOverview> LoadOverviewAsync(
        DateOnly asOf, Guid? vesselId, CancellationToken cancellationToken)
    {
        var categoryTasks = ComplianceCategories
            .Select(category => (Category: category, Result: ReportsApi.GetComplianceExposureAsync(
                new ComplianceExposureReportQuery(asOf, vesselId, category, 1, 1), cancellationToken)))
            .ToList();

        var vesselQuery = vesselId is { } id ? $"vesselId={id}&" : string.Empty;
        var insuranceTotalTask = ComplianceApi.GetInsurancePoliciesAsync($"{vesselQuery}pageNumber=1&pageSize=1", cancellationToken);
        var insuranceInForceTask = ComplianceApi.GetInsurancePoliciesAsync(
            string.Create(CultureInfo.InvariantCulture, $"{vesselQuery}expiryFrom={asOf:yyyy-MM-dd}&pageNumber=1&pageSize=1"),
            cancellationToken);

        Task<ComplianceApiResult<GetNonConformitiesResponse>>? findingsTotalTask = null;
        Task<ComplianceApiResult<GetNonConformitiesResponse>>? findingsClosedTask = null;
        if (vesselId is null)
        {
            findingsTotalTask = ComplianceApi.GetNonConformitiesAsync(new GetNonConformitiesRequest { PageNumber = 1, PageSize = 1 }, cancellationToken);
            findingsClosedTask = ComplianceApi.GetNonConformitiesAsync(
                new GetNonConformitiesRequest { ClosureStatus = ClosedClosureStatus, PageNumber = 1, PageSize = 1 },
                cancellationToken);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (category, task) in categoryTasks)
        {
            var categoryResult = await task;
            if (categoryResult.IsSuccess)
            {
                counts[category] = categoryResult.Value!.TotalCount;
            }
        }

        var insuranceTotal = await insuranceTotalTask;
        var insuranceInForce = await insuranceInForceTask;
        var insurance = insuranceTotal.IsSuccess && insuranceInForce.IsSuccess
            ? new ComplianceHealthRing(insuranceInForce.Value!.TotalCount, insuranceTotal.Value!.TotalCount)
            : null;

        ComplianceHealthRing? findings = null;
        if (findingsTotalTask is not null && findingsClosedTask is not null)
        {
            var findingsTotal = await findingsTotalTask;
            var findingsClosed = await findingsClosedTask;
            if (findingsTotal.IsSuccess && findingsClosed.IsSuccess)
            {
                findings = new ComplianceHealthRing(findingsClosed.Value!.TotalCount, findingsTotal.Value!.TotalCount);
            }
        }

        return new ComplianceExposureOverview(counts, insurance, findings);
    }

    private Task ToggleCategoryAsync(string category)
    {
        complianceCategoryFilter = complianceCategoryFilter == category ? null : category;

        return SearchAsync();
    }

    private string CategoryCount(string category) =>
        overview?.CategoryCounts.TryGetValue(category, out var count) == true ? count.ToString(CultureInfo.InvariantCulture) : "—";

    private static int RingPercent(ComplianceHealthRing ring) =>
        ring.Whole == 0 ? 0 : (int)Math.Round(ring.Part * 100d / ring.Whole);

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
