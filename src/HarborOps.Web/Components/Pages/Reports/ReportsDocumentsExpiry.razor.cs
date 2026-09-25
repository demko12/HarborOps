using HarborOps.Web.Api.Reports;

namespace HarborOps.Web.Components.Pages.Reports;

public partial class ReportsDocumentsExpiry : IDisposable
{
    private static readonly string[] DocumentStatuses = ["Active", "Valid", "Expired", "Suspended"];

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

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
