using System.Globalization;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PurchaseOrdersPanel : IDisposable
{
    /// <summary>
    /// A part carried over from the replenishment board, used to open a prefilled purchase order.
    /// </summary>
    [Parameter]
    public ReplenishmentOrderIntent? Seed { get; set; }

    internal const int SuccessToastMilliseconds = 3000;

    private const int SupplierOptionsPageSize = 100;
    private const int InventoryOptionsPageSize = 100;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly CreatePurchaseOrderModel createModel = new();

    private int loadVersion;

    private IReadOnlyList<PurchaseOrderListItem> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private string supplierFilterText = string.Empty;
    private string statusFilter = string.Empty;
    private DateOnly? orderDateFromFilter;
    private DateOnly? orderDateToFilter;
    private DateOnly? deliveryFromFilter;
    private DateOnly? deliveryToFilter;
    private string overdueFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private IReadOnlyList<SupplierListItem> supplierOptions = [];
    private MaintenanceApiFailure? supplierOptionsFailure;

    private bool isCreateFormOpen;
    private bool isCreating;
    private MaintenanceApiFailure? createFailure;
    private string? createSuccessMessage;

    private IReadOnlyList<InventoryItemListItem> inventoryOptions = [];
    private string inventorySearchText = string.Empty;
    private bool isInventoryLoading;
    private MaintenanceApiFailure? inventoryFailure;

    private Guid? detailsPurchaseOrderId;
    private PurchaseOrderDetails? details;
    private bool isDetailsLoading;
    private MaintenanceApiFailure? detailsFailure;

    private string? pendingTransition;
    private bool isChangingStatus;
    private MaintenanceApiFailure? statusFailure;
    private string? statusSuccessMessage;

    private CancellationTokenSource? toastCancellation;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int OverdueCount => rows.Count(row => row.IsOverdue);

    private decimal PageTotalCost => rows.Sum(row => row.TotalCost);

    private decimal CreateTotalCost => createModel.Lines
        .Sum(line => ProcurementFormat.LineTotal(line.QuantityOrdered, line.UnitCost));

    private ReplenishmentOrderIntent? consumedSeed;

    protected override async Task OnInitializedAsync()
    {
        await LoadSupplierOptionsAsync();
        await LoadAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Seed is not { } seed || ReferenceEquals(seed, consumedSeed))
        {
            return;
        }

        consumedSeed = seed;

        await OpenCreateFormAsync();

        createModel.SupplierIdText = seed.SupplierId.ToString();
        inventorySearchText = seed.PartNumber;

        await SearchInventoryAsync();

        var line = createModel.Lines[0];
        line.InventoryItemIdText = seed.InventoryItemId.ToString();
        line.QuantityOrdered = seed.Quantity;
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        supplierFilterText = string.Empty;
        statusFilter = string.Empty;
        orderDateFromFilter = null;
        orderDateToFilter = null;
        deliveryFromFilter = null;
        deliveryToFilter = null;
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
        isLoading = true;
        failure = null;

        var filter = new PurchaseOrderFilter(
            ParseGuid(supplierFilterText),
            string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
            orderDateFromFilter,
            orderDateToFilter,
            deliveryFromFilter,
            deliveryToFilter,
            overdueFilter switch { "true" => true, "false" => false, _ => null },
            pageNumber,
            pageSize);

        MaintenanceApiResult<PurchaseOrdersPage> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrdersAsync(filter, disposeCancellation.Token);
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

    private async Task LoadSupplierOptionsAsync()
    {
        supplierOptionsFailure = null;

        MaintenanceApiResult<SuppliersPage> result;

        try
        {
            result = await MaintenanceApi.GetSuppliersAsync(
                new SupplierFilter(null, 1, SupplierOptionsPageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            supplierOptionsFailure = result.Failure;
            supplierOptions = [];

            return;
        }

        supplierOptions = result.Value!.Items;
    }

    private async Task OpenCreateFormAsync()
    {
        isCreateFormOpen = true;
        createModel.Reset();
        createFailure = null;
        createSuccessMessage = null;
        inventoryOptions = [];
        inventorySearchText = string.Empty;
        inventoryFailure = null;

        if (supplierOptions.Count == 0)
        {
            await LoadSupplierOptionsAsync();
        }
    }

    private void CloseCreateForm()
    {
        isCreateFormOpen = false;
        createFailure = null;
    }

    private void AddLine()
    {
        createModel.Lines.Add(new PurchaseOrderLineModel());
    }

    private void RemoveLine(PurchaseOrderLineModel line)
    {
        createModel.Lines.Remove(line);
    }

    private bool IsDuplicateLine(PurchaseOrderLineModel line)
    {
        return line.InventoryItemId is { } inventoryItemId
            && createModel.Lines.Count(candidate => candidate.InventoryItemId == inventoryItemId) > 1;
    }

    private async Task SearchInventoryAsync()
    {
        isInventoryLoading = true;
        inventoryFailure = null;

        var filter = new InventoryItemFilter(
            string.IsNullOrWhiteSpace(inventorySearchText) ? null : inventorySearchText,
            null,
            1,
            InventoryOptionsPageSize);

        MaintenanceApiResult<InventoryItemsPage> result;

        try
        {
            result = await MaintenanceApi.GetInventoryItemsAsync(filter, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isInventoryLoading = false;

        if (!result.IsSuccess)
        {
            inventoryFailure = result.Failure;
            inventoryOptions = [];

            return;
        }

        inventoryOptions = result.Value!.Items;
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

        var request = new CreatePurchaseOrderRequest
        {
            SupplierId = createModel.SupplierId,
            OrderDate = createModel.OrderDate,
            ExpectedDelivery = createModel.ExpectedDelivery,
            Lines = createModel.Lines
                .Select(line => new CreatePurchaseOrderLineRequest
                {
                    InventoryItemId = line.InventoryItemId,
                    QuantityOrdered = line.QuantityOrdered,
                    UnitCost = line.UnitCost,
                })
                .ToList(),
        };

        MaintenanceApiResult<CreatedPurchaseOrder> result;

        try
        {
            result = await MaintenanceApi.CreatePurchaseOrderAsync(request, disposeCancellation.Token);
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

        isCreateFormOpen = false;
        createModel.Reset();

        ShowToast(
            $"Draft purchase order was created with a total of {ProcurementFormat.Money(result.Value!.TotalCost)}.",
            value => createSuccessMessage = value);

        await LoadAsync();
    }

    private async Task OpenDetailsAsync(Guid purchaseOrderId)
    {
        detailsPurchaseOrderId = purchaseOrderId;
        details = null;
        detailsFailure = null;
        statusFailure = null;
        pendingTransition = null;

        await LoadDetailsAsync(purchaseOrderId);
    }

    private async Task LoadDetailsAsync(Guid purchaseOrderId)
    {
        isDetailsLoading = true;
        detailsFailure = null;

        MaintenanceApiResult<PurchaseOrderDetails> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrderDetailsAsync(purchaseOrderId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isDetailsLoading = false;

        if (!result.IsSuccess)
        {
            detailsFailure = result.Failure;
            details = null;

            return;
        }

        details = result.Value!;
    }

    private void CloseDetails()
    {
        detailsPurchaseOrderId = null;
        details = null;
        detailsFailure = null;
        statusFailure = null;
        pendingTransition = null;
    }

    private void RequestTransition(string transition)
    {
        pendingTransition = transition;
        statusFailure = null;
    }

    private void CancelTransition()
    {
        pendingTransition = null;
    }

    private async Task ConfirmTransitionAsync()
    {
        if (isChangingStatus
            || pendingTransition is not { } transition
            || detailsPurchaseOrderId is not { } purchaseOrderId)
        {
            return;
        }

        isChangingStatus = true;
        statusFailure = null;
        statusSuccessMessage = null;

        MaintenanceApiResult<ChangePurchaseOrderStatusResult> result;

        try
        {
            if (string.Equals(transition, ProcurementFormat.OrderedStatus, StringComparison.Ordinal))
            {
                result = await MaintenanceApi.MarkPurchaseOrderOrderedAsync(purchaseOrderId, disposeCancellation.Token);
            }
            else if (string.Equals(transition, ProcurementFormat.ReceivedStatus, StringComparison.Ordinal))
            {
                result = await MaintenanceApi.ReceivePurchaseOrderAsync(purchaseOrderId, disposeCancellation.Token);
            }
            else if (string.Equals(transition, ProcurementFormat.CancelledStatus, StringComparison.Ordinal))
            {
                result = await MaintenanceApi.CancelPurchaseOrderAsync(purchaseOrderId, disposeCancellation.Token);
            }
            else
            {
                isChangingStatus = false;
                pendingTransition = null;

                return;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isChangingStatus = false;
        pendingTransition = null;

        if (!result.IsSuccess)
        {
            statusFailure = result.Failure;

            if (result.Failure is { IsNotFound: true })
            {
                CloseDetails();
            }
            else
            {
                await LoadDetailsAsync(purchaseOrderId);
            }

            await LoadAsync();

            return;
        }

        var changed = result.Value!;

        ShowToast(
            changed.StockAdjustments.Count > 0
                ? $"Purchase order is {changed.Status.ToLowerInvariant()} and {changed.StockAdjustments.Count} inventory quantities were increased."
                : $"Purchase order moved from {changed.PreviousStatus.ToLowerInvariant()} to {changed.Status.ToLowerInvariant()}.",
            value => statusSuccessMessage = value);

        await LoadDetailsAsync(purchaseOrderId);
        await LoadAsync();
    }

    private void ShowToast(string message, Action<string?> apply)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        apply(message);

        _ = HideToastAfterDelayAsync(cancellation, apply);
    }

    private async Task HideToastAfterDelayAsync(CancellationTokenSource cancellation, Action<string?> apply)
    {
        try
        {
            await Task.Delay(SuccessToastMilliseconds, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        apply(null);

        await InvokeAsync(StateHasChanged);
    }

    private static Guid? ParseGuid(string? value)
    {
        return Guid.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    public void Dispose()
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class CreatePurchaseOrderModel
    {
        public string SupplierIdText { get; set; } = string.Empty;

        public Guid? SupplierId => ParseGuid(SupplierIdText);

        public DateOnly? OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public DateOnly? ExpectedDelivery { get; set; }

        public List<PurchaseOrderLineModel> Lines { get; } = [new PurchaseOrderLineModel()];

        public void Reset()
        {
            SupplierIdText = string.Empty;
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow);
            ExpectedDelivery = null;
            Lines.Clear();
            Lines.Add(new PurchaseOrderLineModel());
        }
    }

    private sealed class PurchaseOrderLineModel
    {
        public Guid Key { get; } = Guid.NewGuid();

        public string InventoryItemIdText { get; set; } = string.Empty;

        public Guid? InventoryItemId => ParseGuid(InventoryItemIdText);

        public decimal? QuantityOrdered { get; set; }

        public decimal? UnitCost { get; set; }
    }
}
