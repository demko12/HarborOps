using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class DefectsPanel : IDisposable
{
    private static readonly IReadOnlyList<string> Statuses = ["Reported", "Triaged", "Converted", "Rejected"];

    private static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];

    private readonly CancellationTokenSource disposeCancellation = new();

    private int loadVersion;

    private IReadOnlyList<DefectRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private string statusFilter = string.Empty;
    private string severityFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool CanReportDefects => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselFilter = null;
        statusFilter = string.Empty;
        severityFilter = string.Empty;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

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
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var filter = new DefectFilter(
            vesselFilter,
            string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
            string.IsNullOrEmpty(severityFilter) ? null : severityFilter,
            pageNumber,
            pageSize);

        MaintenanceApiResult<DefectsPage> result;

        try
        {
            result = await MaintenanceApi.GetDefectsAsync(filter, cancellationToken);
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
            rows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;

        await ComposeRowsAsync(page.Items, cancellationToken, requestVersion);
    }

    private async Task ComposeRowsAsync(
        IReadOnlyList<DefectListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new DefectRow(
                item.Id,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : MaintenanceFormat.UnknownVessel(),
                item.Title,
                item.Status,
                item.ConfirmedSeverity ?? item.ReportedSeverity,
                item.ReportedAtUtc))
            .ToList();
    }

    private void OpenDefectDetails(Guid defectId)
    {
        Navigation.NavigateTo($"/maintenance/defects/{defectId}");
    }

    private void HandleRowKeyDown(KeyboardEventArgs args, Guid defectId)
    {
        if (args.Key is "Enter" or " ")
        {
            OpenDefectDetails(defectId);
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
