using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class Maintenance
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private IReadOnlyList<MaintenanceWorkOrderRow> rows = [];
    private MaintenanceApiFailure? failure;

    private Guid? vesselIdFilter;
    private int filterVesselPickerResetToken;
    private string statusFilter = string.Empty;
    private string priorityFilter = string.Empty;
    private string overdueFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private bool isLoading = true;

    private readonly CreateWorkOrderModel createModel = new();
    private bool isCreating;
    private MaintenanceApiFailure? createFailure;
    private string? createSuccessMessage;

    private Guid? completingWorkOrderId;
    private readonly CompleteWorkOrderModel completionModel = new();
    private bool isCompleting;
    private MaintenanceApiFailure? completionFailure;
    private string? completionSuccessMessage;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private Guid? usageWorkOrderId;
    private string? inventorySearchText;
    private bool inventoryLowStockOnly;
    private IReadOnlyList<InventoryItemListItem> inventoryItems = [];
    private bool isInventoryLoading;
    private MaintenanceApiFailure? inventoryFailure;

    private Guid? selectedInventoryItemId;
    private string? selectedInventoryItemName;
    private decimal selectedInventoryItemQuantity;

    private readonly RecordUsageModel usageModel = new();
    private bool isRecordingUsage;
    private MaintenanceApiFailure? usageFailure;
    private string? usageSuccessMessage;

    private IReadOnlyList<WorkOrderPartUsageListItem> workOrderUsages = [];
    private bool isUsagesLoading;
    private MaintenanceApiFailure? usagesFailure;

    private bool CanRecordUsage => AuthorizationState.HasPermission("maintenance.manage");

    private bool isCreateFormOpen;

    private MaintenanceTab activeTab = MaintenanceTab.WorkOrders;

    private ReplenishmentOrderIntent? purchaseOrderSeed;

    private Guid? supplierReturnsPurchaseOrderId;

    private void SelectTab(MaintenanceTab tab)
    {
        purchaseOrderSeed = null;
        supplierReturnsPurchaseOrderId = null;
        activeTab = tab;
    }

    private Task StartPurchaseOrderAsync(ReplenishmentOrderIntent intent)
    {
        purchaseOrderSeed = intent;
        activeTab = MaintenanceTab.PurchaseOrders;

        return Task.CompletedTask;
    }

    private Task OpenSupplierReturnsAsync(Guid purchaseOrderId)
    {
        supplierReturnsPurchaseOrderId = purchaseOrderId;
        activeTab = MaintenanceTab.SupplierReturns;

        return Task.CompletedTask;
    }

    protected override Task OnInitializedAsync()
    {
        RestoreTabFromQuery();

        return LoadAsync();
    }

    private void RestoreTabFromQuery()
    {
        var query = QueryHelpers.ParseQuery(new Uri(Navigation.Uri).Query);

        if (!query.TryGetValue("tab", out var tabValues))
        {
            return;
        }

        activeTab = tabValues.ToString() switch
        {
            "dry-docks" => MaintenanceTab.DryDocks,
            "equipment" => MaintenanceTab.Equipment,
            "schedules" => MaintenanceTab.PreventiveSchedules,
            "preventive-work" => MaintenanceTab.PreventiveWork,
            "replenishment" => MaintenanceTab.Replenishment,
            "stocktakes" => MaintenanceTab.Stocktakes,
            "supplier-returns" => MaintenanceTab.SupplierReturns,
            "purchase-orders" => MaintenanceTab.PurchaseOrders,
            "suppliers" => MaintenanceTab.Suppliers,
            "defects" => MaintenanceTab.Defects,
            _ => MaintenanceTab.WorkOrders,
        };
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselIdFilter = vesselId;

        return ApplyFiltersAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselIdFilter = null;
        filterVesselPickerResetToken++;
        statusFilter = string.Empty;
        priorityFilter = string.Empty;
        overdueFilter = string.Empty;
        pageNumber = 1;

        return LoadAsync();
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

        var filter = new MaintenanceWorkOrderFilter(
            vesselIdFilter,
            string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
            string.IsNullOrEmpty(priorityFilter) ? null : priorityFilter,
            overdueFilter switch { "true" => true, "false" => false, _ => null },
            pageNumber,
            pageSize);

        MaintenanceApiResult<MaintenanceWorkOrdersPage> result;

        try
        {
            result = await MaintenanceApi.GetWorkOrdersAsync(filter, cancellationToken);
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
        IReadOnlyList<MaintenanceWorkOrderListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await FleetApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new MaintenanceWorkOrderRow(
                item.WorkOrderId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : MaintenanceFormat.UnknownVessel(),
                item.Title,
                item.Description,
                item.Priority,
                item.Status,
                item.DueUtc,
                item.AssignedEngineerId,
                item.CreatedUtc,
                item.CompletedUtc))
            .ToList();
    }

    private async Task SubmitCreateAsync()
    {
        if (isCreating)
        {
            return;
        }

        isCreating = true;
        createFailure = null;
        createSuccessMessage = null;

        var request = new CreateMaintenanceWorkOrderRequest
        {
            VesselId = createModel.VesselId,
            Title = createModel.Title,
            Description = createModel.Description,
            Priority = string.IsNullOrEmpty(createModel.Priority) ? null : createModel.Priority,
            DueUtc = createModel.DueUtcLocal is { } dueLocal
                ? DateTime.SpecifyKind(dueLocal, DateTimeKind.Utc)
                : null,
        };

        var result = await MaintenanceApi.CreateWorkOrderAsync(request, CancellationToken.None);

        isCreating = false;

        if (!result.IsSuccess)
        {
            createFailure = result.Failure;

            return;
        }

        createSuccessMessage = "Work order created.";
        isCreateFormOpen = false;
        createModel.Reset();

        await LoadAsync();
    }

    private void OpenCompletionForm(Guid workOrderId)
    {
        completingWorkOrderId = workOrderId;
        completionModel.Reset();
        completionFailure = null;
        completionSuccessMessage = null;
    }

    private void CloseCompletionForm()
    {
        completingWorkOrderId = null;
        completionFailure = null;
    }

    private async Task SubmitCompletionAsync()
    {
        if (isCompleting || completingWorkOrderId is not { } workOrderId)
        {
            return;
        }

        isCompleting = true;
        completionFailure = null;
        completionSuccessMessage = null;

        var request = new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.TryParse(completionModel.TechnicianId, out var technicianId) ? technicianId : null,
            CompletedUtc = completionModel.CompletedUtcLocal is { } completedLocal
                ? DateTime.SpecifyKind(completedLocal, DateTimeKind.Utc)
                : null,
            Notes = completionModel.Notes,
            Cost = completionModel.Cost,
            LaborHours = completionModel.LaborHours,
        };

        var result = await MaintenanceApi.CompleteWorkOrderAsync(workOrderId, request, CancellationToken.None);

        isCompleting = false;

        if (!result.IsSuccess)
        {
            completionFailure = result.Failure;

            if (result.Failure is { IsConflict: true } or { IsNotFound: true })
            {
                completingWorkOrderId = null;
                await LoadAsync();
            }

            return;
        }

        completionSuccessMessage = "Work order completed.";
        completingWorkOrderId = null;

        await LoadAsync();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class CreateWorkOrderModel
    {
        public Guid? VesselId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Priority { get; set; } = string.Empty;

        public DateTime? DueUtcLocal { get; set; }

        public void Reset()
        {
            VesselId = null;
            Title = null;
            Description = null;
            Priority = string.Empty;
            DueUtcLocal = null;
        }
    }

    private sealed class CompleteWorkOrderModel
    {
        public string? TechnicianId { get; set; }

        public DateTime? CompletedUtcLocal { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }

        public decimal? Cost { get; set; }

        public decimal? LaborHours { get; set; }

        public void Reset()
        {
            TechnicianId = null;
            CompletedUtcLocal = DateTime.UtcNow;
            Notes = null;
            Cost = null;
            LaborHours = null;
        }
    }

    private async Task OpenUsageForm(Guid workOrderId)
    {
        usageWorkOrderId = workOrderId;
        inventorySearchText = null;
        inventoryLowStockOnly = false;
        selectedInventoryItemId = null;
        selectedInventoryItemName = null;
        selectedInventoryItemQuantity = 0m;
        usageModel.Reset();
        usageFailure = null;
        usageSuccessMessage = null;

        await Task.WhenAll(SearchInventoryAsync(), LoadWorkOrderUsagesAsync(workOrderId));
    }

    private void CloseUsageForm()
    {
        usageWorkOrderId = null;
        usageFailure = null;
        inventoryItems = [];
        workOrderUsages = [];
    }

    private async Task SearchInventoryAsync()
    {
        if (usageWorkOrderId is null)
        {
            return;
        }

        isInventoryLoading = true;
        inventoryFailure = null;

        var filter = new InventoryItemFilter(
            string.IsNullOrWhiteSpace(inventorySearchText) ? null : inventorySearchText,
            inventoryLowStockOnly ? true : null,
            PageNumber: 1,
            PageSize: 25);

        var result = await MaintenanceApi.GetInventoryItemsAsync(filter, CancellationToken.None);

        isInventoryLoading = false;

        if (!result.IsSuccess)
        {
            inventoryFailure = result.Failure;
            inventoryItems = [];

            return;
        }

        inventoryItems = result.Value!.Items;
    }

    private void SelectInventoryItem(InventoryItemListItem item)
    {
        selectedInventoryItemId = item.InventoryItemId;
        selectedInventoryItemName = $"{item.Name} ({item.PartNumber})";
        selectedInventoryItemQuantity = item.Quantity;
        usageFailure = null;
    }

    private void ClearSelectedInventoryItem()
    {
        selectedInventoryItemId = null;
        selectedInventoryItemName = null;
        selectedInventoryItemQuantity = 0m;
    }

    private async Task SubmitUsageAsync()
    {
        if (isRecordingUsage || usageWorkOrderId is not { } workOrderId || selectedInventoryItemId is not { } partId)
        {
            return;
        }

        isRecordingUsage = true;
        usageFailure = null;
        usageSuccessMessage = null;

        var request = new RecordPartUsageRequest
        {
            QuantityUsed = usageModel.QuantityUsed,
        };

        var result = await MaintenanceApi.RecordPartUsageAsync(workOrderId, partId, request, CancellationToken.None);

        isRecordingUsage = false;

        if (!result.IsSuccess)
        {
            usageFailure = result.Failure;

            if (result.Failure is { IsNotFound: true })
            {
                usageWorkOrderId = null;
                await LoadAsync();
            }

            return;
        }

        usageSuccessMessage = "Part usage recorded.";
        usageModel.Reset();
        ClearSelectedInventoryItem();

        await LoadWorkOrderUsagesAsync(workOrderId);
        await SearchInventoryAsync();
    }

    private async Task LoadWorkOrderUsagesAsync(Guid workOrderId)
    {
        isUsagesLoading = true;
        usagesFailure = null;

        var result = await MaintenanceApi.GetPartUsagesAsync(workOrderId, CancellationToken.None);

        isUsagesLoading = false;

        if (!result.IsSuccess)
        {
            usagesFailure = result.Failure;
            workOrderUsages = [];

            return;
        }

        workOrderUsages = result.Value!.Items;
    }

    private sealed class RecordUsageModel
    {
        public decimal? QuantityUsed { get; set; }

        public void Reset()
        {
            QuantityUsed = null;
        }
    }

    private void OpenCreateForm()
    {
        isCreateFormOpen = true;
        createModel.Reset();
        createFailure = null;
        createSuccessMessage = null;
    }

    private void CloseCreateForm()
    {
        isCreateFormOpen = false;
        createFailure = null;
    }

    private string DetailsHref(Guid workOrderId)
    {
        return $"/maintenance/work-orders/{workOrderId}?from={Uri.EscapeDataString(BoardQuery())}";
    }

    private string BoardQuery()
    {
        var parts = new List<string> { "tab=work-orders" };

        if (vesselIdFilter is { } vesselId)
        {
            parts.Add($"vessel={vesselId}");
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            parts.Add($"status={Uri.EscapeDataString(statusFilter)}");
        }

        if (!string.IsNullOrWhiteSpace(priorityFilter))
        {
            parts.Add($"priority={Uri.EscapeDataString(priorityFilter)}");
        }

        if (!string.IsNullOrWhiteSpace(overdueFilter))
        {
            parts.Add($"overdue={Uri.EscapeDataString(overdueFilter)}");
        }

        return string.Join('&', parts);
    }

    private void OpenWorkOrderDetails(Guid workOrderId)
    {
        Navigation.NavigateTo(DetailsHref(workOrderId));
    }

    private void HandleWorkOrderRowKeyDown(KeyboardEventArgs args, Guid workOrderId)
    {
        if (args.Key is "Enter" or " ")
        {
            OpenWorkOrderDetails(workOrderId);
        }
    }
}
