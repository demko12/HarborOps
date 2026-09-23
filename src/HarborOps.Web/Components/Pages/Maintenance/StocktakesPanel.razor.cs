using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class StocktakesPanel : IDisposable
{
    internal const int MaxLines = 200;
    internal const int PartOptionLimit = 25;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly List<InventoryItemListItem> selectedParts = [];

    private int loadVersion;
    private int partsVersion;

    private IReadOnlyList<StocktakeSummary> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private string statusFilter = string.Empty;
    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private Guid? selectedStocktakeId;

    private bool isCreateFormOpen;
    private bool isCreating;
    private string? notes;
    private MaintenanceApiFailure? createFailure;
    private string? successMessage;

    private string? partSearchText;
    private IReadOnlyList<InventoryItemListItem> partOptions = [];
    private bool isPartsLoading;
    private MaintenanceApiFailure? partsFailure;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int DraftCount => rows.Count(row => string.Equals(
        row.Status,
        MaintenanceFormat.DraftStocktake,
        StringComparison.Ordinal));

    private int VarianceLineCount => rows.Sum(row => row.VarianceLineCount);

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private static string ClosedAt(StocktakeSummary row)
    {
        if (row.PostedUtc is { } postedUtc)
        {
            return MaintenanceFormat.CreatedUtc(postedUtc);
        }

        return row.CancelledUtc is { } cancelledUtc
            ? MaintenanceFormat.CreatedUtc(cancelledUtc)
            : MaintenanceFormat.MissingValue;
    }

    private bool IsSelected(Guid inventoryItemId)
    {
        return selectedParts.Exists(part => part.InventoryItemId == inventoryItemId);
    }

    private void OpenStocktake(Guid stocktakeId)
    {
        successMessage = null;
        selectedStocktakeId = stocktakeId;
    }

    private Task CloseEditorAsync()
    {
        selectedStocktakeId = null;

        return LoadAsync();
    }

    private Task ToggleCreateFormAsync()
    {
        isCreateFormOpen = !isCreateFormOpen;
        createFailure = null;
        successMessage = null;

        if (!isCreateFormOpen)
        {
            selectedParts.Clear();
            partOptions = [];
            partSearchText = null;
            notes = null;

            return Task.CompletedTask;
        }

        // Show the first parts straight away so the picker is never empty on open.
        return SearchPartsAsync();
    }

    private void SelectPart(InventoryItemListItem part)
    {
        if (selectedParts.Count >= MaxLines || IsSelected(part.InventoryItemId))
        {
            return;
        }

        selectedParts.Add(part);
    }

    private void RemovePart(Guid inventoryItemId)
    {
        selectedParts.RemoveAll(part => part.InventoryItemId == inventoryItemId);
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

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

    private async Task SearchPartsAsync()
    {
        var requestVersion = ++partsVersion;
        isPartsLoading = true;
        partsFailure = null;

        MaintenanceApiResult<InventoryItemsPage> result;

        try
        {
            result = await MaintenanceApi.GetInventoryItemsAsync(
                new InventoryItemFilter(partSearchText, null, 1, PartOptionLimit),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != partsVersion)
        {
            return;
        }

        isPartsLoading = false;

        if (!result.IsSuccess)
        {
            partsFailure = result.Failure;
            partOptions = [];

            return;
        }

        partOptions = result.Value!.Items;
    }

    private async Task SubmitCreateAsync()
    {
        if (isCreating || selectedParts.Count == 0)
        {
            return;
        }

        isCreating = true;
        createFailure = null;

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.CreateStocktakeAsync(
                new CreateStocktakeRequest
                {
                    InventoryItemIds = selectedParts.Select(part => part.InventoryItemId).ToList(),
                    Notes = notes,
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isCreating = false;

        if (!result.IsSuccess)
        {
            createFailure = result.Failure;

            return;
        }

        var created = result.Value!;
        selectedParts.Clear();
        partOptions = [];
        partSearchText = null;
        notes = null;
        isCreateFormOpen = false;
        successMessage = $"Stocktake created with {created.Lines.Count} lines.";
        selectedStocktakeId = created.StocktakeId;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<StocktakesPage> result;

        try
        {
            result = await MaintenanceApi.GetStocktakesAsync(
                new StocktakeFilter(
                    string.IsNullOrWhiteSpace(statusFilter) ? null : statusFilter,
                    pageNumber,
                    pageSize),
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
        rows = page.Items;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
