using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Shared;
using Microsoft.AspNetCore.WebUtilities;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class DryDocksPanel : IDisposable
{
    internal const int SuccessToastMilliseconds = 3000;
    internal const int PortOptionLimit = 100;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly ScheduleDryDockModel scheduleModel = new();

    private CancellationTokenSource? toastCancellation;
    private int loadVersion;

    private IReadOnlyList<DryDockBoardRow> rows = [];
    private IReadOnlyList<PortListItemModel> portOptions = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private Guid? portFilter;
    private string statusFilter = string.Empty;
    private bool overdueOnlyFilter;
    private string? searchFilter;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool isScheduleFormOpen;
    private bool isScheduling;
    private MaintenanceApiFailure? scheduleFailure;
    private string? scheduleSuccessMessage;
    private PortPicker? schedulePortPicker;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int? overdueKpiCount;

    protected override async Task OnInitializedAsync()
    {
        RestoreFiltersFromQuery();

        await LoadAsync();
        await LoadPortOptionsAsync();
    }

    private async Task LoadPortOptionsAsync()
    {
        VoyagesApiResult<PortPage> result;

        try
        {
            result = await VoyagesApi.GetPortsAsync(
                null,
                null,
                pageNumber: 1,
                pageSize: PortOptionLimit,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || !result.IsSuccess)
        {
            return;
        }

        portOptions = result.Value!.Items;
    }

    private static string PortOptionLabel(PortListItemModel port)
    {
        return string.IsNullOrWhiteSpace(port.City)
            ? $"{port.Name}, {port.Country}"
            : $"{port.Name} ({port.City}), {port.Country}";
    }

    private void RestoreFiltersFromQuery()
    {
        var query = QueryHelpers.ParseQuery(new Uri(Navigation.Uri).Query);

        if (query.TryGetValue("vessel", out var vesselValues)
            && Guid.TryParse(vesselValues, out var vesselId))
        {
            vesselFilter = vesselId;
        }

        if (query.TryGetValue("port", out var portValues)
            && Guid.TryParse(portValues, out var portId))
        {
            portFilter = portId;
        }

        if (query.TryGetValue("status", out var statusValues)
            && MaintenanceFormat.DryDockStatuses.Contains(statusValues.ToString(), StringComparer.Ordinal))
        {
            statusFilter = statusValues.ToString();
        }

        if (query.TryGetValue("overdue", out var overdueValues))
        {
            overdueOnlyFilter = bool.TryParse(overdueValues, out var overdue) && overdue;
        }

        if (query.TryGetValue("search", out var searchValues) && !string.IsNullOrWhiteSpace(searchValues))
        {
            searchFilter = searchValues.ToString();
        }

        if (query.TryGetValue("page", out var pageValues)
            && int.TryParse(pageValues, out var parsedPage)
            && parsedPage > 0)
        {
            pageNumber = parsedPage;
        }
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselFilter = null;
        portFilter = null;
        statusFilter = string.Empty;
        overdueOnlyFilter = false;
        searchFilter = null;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
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

    private string DetailsHref(Guid dryDockId)
    {
        return $"/maintenance/dry-docks/{dryDockId}?from={Uri.EscapeDataString(BoardQuery())}";
    }

    private string BoardQuery()
    {
        var parts = new List<string> { "tab=dry-docks" };

        if (vesselFilter is { } vesselId)
        {
            parts.Add($"vessel={vesselId}");
        }

        if (portFilter is { } portId)
        {
            parts.Add($"port={portId}");
        }

        if (!string.IsNullOrEmpty(statusFilter))
        {
            parts.Add($"status={Uri.EscapeDataString(statusFilter)}");
        }

        if (overdueOnlyFilter)
        {
            parts.Add("overdue=true");
        }

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            parts.Add($"search={Uri.EscapeDataString(searchFilter.Trim())}");
        }

        if (pageNumber > 1)
        {
            parts.Add($"page={pageNumber}");
        }

        return string.Join('&', parts);
    }

    private Task ToggleOverdueKpiAsync()
    {
        overdueOnlyFilter = !overdueOnlyFilter;

        return ApplyFiltersAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        var filter = new DryDockFilter
        {
            VesselId = vesselFilter,
            PortId = portFilter,
            Status = string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
            Overdue = overdueOnlyFilter ? true : null,
            Search = searchFilter,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

        MaintenanceApiResult<DryDocksPage> result;
        try
        {
            result = await MaintenanceApi.GetDryDocksAsync(filter, disposeCancellation.Token);
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
            overdueKpiCount = null;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;

        await Task.WhenAll(
            ComposeRowsAsync(page.Items, requestVersion),
            LoadOverdueKpiCountAsync(filter, requestVersion));
    }

    private async Task LoadOverdueKpiCountAsync(DryDockFilter filter, int requestVersion)
    {
        MaintenanceApiResult<DryDocksPage> result;
        try
        {
            result = await MaintenanceApi.GetDryDocksAsync(
                filter with { Overdue = true, PageNumber = 1, PageSize = 1 },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        overdueKpiCount = result.IsSuccess ? result.Value!.TotalCount : null;
    }

    private async Task ComposeRowsAsync(IReadOnlyList<DryDockRow> items, int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var portIds = items.Select(item => item.PortId).Distinct().ToArray();

        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(
            vesselIds,
            disposeCancellation.Token);
        var portNames = await VoyagesApi.LookupPortNamesAsync(portIds, disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new DryDockBoardRow(
                item.DryDockId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var vesselName)
                    ? vesselName
                    : MaintenanceFormat.UnknownVessel(),
                item.PortId,
                portNames.TryGetValue(item.PortId, out var portName)
                    ? portName
                    : MaintenanceFormat.ShortPortReference(item.PortId),
                item.StartDate,
                item.EndDate,
                item.PlannedTasks,
                item.ActualCost,
                item.Status,
                item.IsOverdue,
                item.DaysUntilStart))
            .ToList();
    }

    private void OpenScheduleForm()
    {
        scheduleFailure = null;
        scheduleModel.Reset();
        schedulePortPicker?.Reset();
        isScheduleFormOpen = true;
    }

    private void CloseScheduleForm()
    {
        if (isScheduling)
        {
            return;
        }

        scheduleFailure = null;
        isScheduleFormOpen = false;
    }

    private async Task SubmitScheduleAsync()
    {
        if (isScheduling)
        {
            return;
        }

        scheduleFailure = null;

        if (!scheduleModel.TryBuildRequest(out var request, out var localErrors))
        {
            scheduleFailure = new MaintenanceApiFailure(
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                "Please review the values and try again.",
                localErrors);

            return;
        }

        isScheduling = true;

        try
        {
            var result = await MaintenanceApi.ScheduleDryDockAsync(request, disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                scheduleFailure = result.Failure;

                return;
            }

            isScheduleFormOpen = false;
            scheduleModel.Reset();
            schedulePortPicker?.Reset();

            ShowSuccess("Dry dock scheduled.");

            pageNumber = 1;

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isScheduling = false;
        }
    }

    private void ShowSuccess(string message)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        scheduleSuccessMessage = message;

        _ = HideSuccessAfterDelayAsync(cancellation);
    }

    private async Task HideSuccessAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SuccessToastMilliseconds, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        scheduleSuccessMessage = null;
        StateHasChanged();
    }

    private sealed class ScheduleDryDockModel
    {
        internal Guid? VesselId { get; set; }

        internal Guid? PortId { get; set; }

        internal DateOnly? StartDate { get; set; }

        [StringLength(4000)]
        internal string? PlannedTasks { get; set; }

        internal void Reset()
        {
            VesselId = null;
            PortId = null;
            StartDate = null;
            PlannedTasks = null;
        }

        internal bool TryBuildRequest(
            out ScheduleDryDockWebRequest request,
            out IReadOnlyDictionary<string, string[]> errors)
        {
            var found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            if (VesselId is null || VesselId == Guid.Empty)
            {
                found["vesselId"] = ["Select a vessel."];
            }

            if (PortId is null || PortId == Guid.Empty)
            {
                found["portId"] = ["Select a port."];
            }

            if (StartDate is null)
            {
                found["startDate"] = ["Required."];
            }

            if (string.IsNullOrWhiteSpace(PlannedTasks))
            {
                found["plannedTasks"] = ["Required."];
            }

            errors = found;

            if (found.Count > 0)
            {
                request = null!;

                return false;
            }

            request = new ScheduleDryDockWebRequest(
                VesselId!.Value,
                PortId!.Value,
                StartDate!.Value,
                PlannedTasks!.Trim());

            return true;
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();
    }
}
