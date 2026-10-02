using System.Globalization;
using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class ReportsDocumentsExpiry : IDisposable
{
    private static readonly string[] DocumentStatuses = ["Active", "Valid", "Expired", "Suspended"];

    internal const int DueSoonDays = 30;
    internal const int PlanningDays = 90;
    internal const int MonthsAhead = 12;
    internal const int AttentionListLimit = 5;
    internal const int DocumentsExpiryReportPageSizeLimit = 100;

    private DocumentsExpiryOverview? overview;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private DateOnly asOfFilter = DateOnly.FromDateTime(DateTime.UtcNow);

    private Guid? vesselIdFilter;
    private int vesselPickerResetToken;

    private DateOnly? documentsFromFilter;
    private DateOnly? documentsToFilter;
    private string? documentsStatusFilter;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool isReportDisplayed;
    private ReportsApiFailure? failure;
    private string? filterValidationMessage;
    private DateOnly reportAsOf;

    private IReadOnlyList<DocumentsExpiryReportRow> documentsRows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();

    private bool IsRefreshing => isLoading && isReportDisplayed;

    private int TotalPages => totalCount == 0 ? 1 : ((totalCount + Reports.PageSize - 1) / Reports.PageSize);

    private int FirstRowNumber => documentsRows.Count == 0 ? 0 : ((pageNumber - 1) * Reports.PageSize) + 1;

    private int LastRowNumber => FirstRowNumber + documentsRows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        if (!AuthorizationState.CanAccessDocuments)
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
        documentsFromFilter = null;
        documentsToFilter = null;
        documentsStatusFilter = null;
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
        filterValidationMessage = Reports.ValidateRange(documentsFromFilter, documentsToFilter);

        if (filterValidationMessage is not null)
        {
            return;
        }

        await LoadDocumentsExpiryAsync();

        if (!isLoading)
        {
            isReportDisplayed = failure is null;
        }
    }

    private async Task LoadDocumentsExpiryAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new DocumentsExpiryReportQuery(
            asOfFilter, vesselIdFilter, null, documentsFromFilter, documentsToFilter,
            documentsStatusFilter, pageNumber, Reports.PageSize);

        var overviewTask = LoadOverviewAsync(query, cancellationToken);

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
            overview = null;

            return;
        }

        var page = result.Value!;
        reportAsOf = page.AsOf;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        documentsRows = page.Items;

        DocumentsExpiryOverview? loadedOverview;

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

        var vesselIds = page.Items.Select(item => item.VesselId)
            .Concat(overview?.Vessels.Select(vessel => vessel.VesselId) ?? []);

        await ResolveVesselNamesAsync(vesselIds, cancellationToken, requestVersion);
    }

    private async Task<DocumentsExpiryOverview?> LoadOverviewAsync(
        DocumentsExpiryReportQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf;
        var firstMonth = new DateOnly(asOf.Year, asOf.Month, 1);

        Task<int?> Count(DateOnly? from, DateOnly? to) => CountWithinAsync(query, from, to, cancellationToken);

        var expiredTask = Count(null, asOf.AddDays(-1));
        var dueSoonTask = Count(asOf, asOf.AddDays(DueSoonDays));
        var dueLaterTask = Count(asOf.AddDays(DueSoonDays + 1), asOf.AddDays(PlanningDays));
        var monthTasks = Enumerable.Range(0, MonthsAhead)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => (Month: month, Count: Count(month < asOf ? asOf : month, month.AddMonths(1).AddDays(-1))))
            .ToList();
        var attentionQuery = WithinWindow(query, null, asOf.AddDays(PlanningDays));
        var attentionTask = attentionQuery is null
            ? Task.FromResult(new ReportsApiResult<DocumentsExpiryReportPage>(new DocumentsExpiryReportPage(asOf, [], 1, 1, 0), null))
            : ReportsApi.GetDocumentsExpiryAsync(attentionQuery, cancellationToken);

        var expired = await expiredTask;
        var dueSoon = await dueSoonTask;
        var dueLater = await dueLaterTask;
        var months = new List<DocumentsExpiryMonth>();
        foreach (var (month, countTask) in monthTasks)
        {
            months.Add(new DocumentsExpiryMonth(month, await countTask ?? 0));
        }

        var attention = await attentionTask;

        if (expired is null || dueSoon is null || dueLater is null || !attention.IsSuccess)
        {
            return null;
        }

        var attentionRows = attention.Value!.Items;

        return new DocumentsExpiryOverview(
            expired.Value,
            dueSoon.Value,
            dueLater.Value,
            months,
            BuildTypeBreakdown(attentionRows),
            BuildVesselAttention(attentionRows),
            attention.Value.TotalCount > attentionRows.Count);
    }

    private async Task<int?> CountWithinAsync(
        DocumentsExpiryReportQuery query, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (WithinWindow(query, from, to) is not { } windowQuery)
        {
            return 0;
        }

        var result = await ReportsApi.GetDocumentsExpiryAsync(windowQuery with { PageSize = 1 }, cancellationToken);

        return result.IsSuccess ? result.Value!.TotalCount : null;
    }

    internal static DocumentsExpiryReportQuery? WithinWindow(DocumentsExpiryReportQuery query, DateOnly? from, DateOnly? to)
    {
        var effectiveFrom = Later(query.From, from);
        var effectiveTo = Earlier(query.To, to);

        if (effectiveFrom is { } start && effectiveTo is { } end && end < start)
        {
            return null;
        }

        return query with { From = effectiveFrom, To = effectiveTo, PageNumber = 1, PageSize = DocumentsExpiryReportPageSizeLimit };
    }

    private static DateOnly? Later(DateOnly? first, DateOnly? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;

    private static DateOnly? Earlier(DateOnly? first, DateOnly? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;

    internal static IReadOnlyList<DocumentsExpiryTypeBreakdown> BuildTypeBreakdown(IReadOnlyList<DocumentsExpiryReportRow> rows) =>
        rows.GroupBy(row => row.DocumentTypeName)
            .Select(group => new DocumentsExpiryTypeBreakdown(
                group.Key,
                group.Count(row => row.IsExpired),
                group.Count(row => !row.IsExpired)))
            .OrderByDescending(item => item.Expired + item.Due)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(AttentionListLimit)
            .ToList();

    internal static IReadOnlyList<DocumentsExpiryVesselAttention> BuildVesselAttention(IReadOnlyList<DocumentsExpiryReportRow> rows) =>
        rows.GroupBy(row => row.VesselId)
            .Select(group =>
            {
                var mostUrgent = group.OrderBy(row => row.DaysUntilExpiry).First();

                return new DocumentsExpiryVesselAttention(
                    group.Key,
                    group.Count(row => row.IsExpired),
                    group.Count(row => !row.IsExpired && row.DaysUntilExpiry <= DueSoonDays),
                    mostUrgent);
            })
            .Where(item => item.Expired + item.DueSoon > 0)
            .OrderByDescending(item => item.Expired)
            .ThenByDescending(item => item.DueSoon)
            .ThenBy(item => item.MostUrgent.DaysUntilExpiry)
            .Take(AttentionListLimit)
            .ToList();

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

    private static string DocumentStatusLabel(DocumentsExpiryReportRow row) =>
        row.IsExpired ? "Expired" : row.Status;

    // Mirrors Documents.razor's GetDocumentStatusClass so a document's status reads the
    // same color on this report as it does on the Documents module's own expiring-documents
    // page - classified on the same label shown in the badge (DocumentStatusLabel), not the
    // raw row.Status, so an overdue "Active" document still colors as expired here too.
    private static string DocumentStatusClass(DocumentsExpiryReportRow row) =>
        DocumentStatusLabel(row).Trim().ToUpperInvariant() switch
        {
            "ACTIVE" => "reports-status-active",
            "VALID" => "reports-status-valid",
            "EXPIRED" => "reports-status-expired",
            "SUSPENDED" or "INACTIVE" => "reports-status-neutral",
            _ => "reports-status-neutral",
        };

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

    private string BuildDownloadUrl()
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

    private const double ChartLeft = 40;
    private const double ChartRight = 1140;
    private const double ChartBottom = 205;
    private const double ChartTop = 30;

    private static double ChartX(int index) => ChartLeft + (index * (ChartRight - ChartLeft) / (MonthsAhead - 1));

    private static double ChartY(int count, int max) => ChartBottom - (count * (ChartBottom - ChartTop) / Math.Max(1, max));

    private static string ChartLinePath(IReadOnlyList<DocumentsExpiryMonth> months)
    {
        var max = months.Max(month => month.Count);

        return string.Join(' ', months.Select((month, index) =>
            string.Create(CultureInfo.InvariantCulture, $"{(index == 0 ? 'M' : 'L')}{ChartX(index):0.#} {ChartY(month.Count, max):0.#}")));
    }

    private static string ChartAreaPath(IReadOnlyList<DocumentsExpiryMonth> months) =>
        ChartLinePath(months) + string.Create(
            CultureInfo.InvariantCulture,
            $" L{ChartX(months.Count - 1):0.#} {ChartBottom:0.#} L{ChartX(0):0.#} {ChartBottom:0.#} Z");

    private static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100d / whole);

    private static string StatusMixGradient(DocumentsExpiryOverview overview, int total)
    {
        var expired = Percent(overview.Expired, total);
        var dueSoon = expired + Percent(overview.DueSoon, total);
        var dueLater = dueSoon + Percent(overview.DueLater, total);

        return $"--mix-expired: {expired}; --mix-due-soon: {dueSoon}; --mix-due-later: {dueLater}";
    }

    private static string MostUrgentLabel(DocumentsExpiryReportRow row)
    {
        var days = Math.Abs(row.DaysUntilExpiry);
        var unit = days == 1 ? "day" : "days";

        return row.IsExpired
            ? $"{row.DocumentTypeName} expired {days} {unit} ago"
            : row.DaysUntilExpiry == 0 ? $"{row.DocumentTypeName} expires today" : $"{row.DocumentTypeName} expires in {days} {unit}";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    internal sealed record DocumentsExpiryOverview(
        int Expired,
        int DueSoon,
        int DueLater,
        IReadOnlyList<DocumentsExpiryMonth> Months,
        IReadOnlyList<DocumentsExpiryTypeBreakdown> Types,
        IReadOnlyList<DocumentsExpiryVesselAttention> Vessels,
        bool AttentionTruncated);

    internal sealed record DocumentsExpiryMonth(DateOnly Month, int Count);

    internal sealed record DocumentsExpiryTypeBreakdown(string Name, int Expired, int Due);

    internal sealed record DocumentsExpiryVesselAttention(Guid VesselId, int Expired, int DueSoon, DocumentsExpiryReportRow MostUrgent);
}
