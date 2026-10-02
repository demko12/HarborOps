using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class SupplierReturnsPanel : IDisposable
{
    internal const int OrderOptionLimit = 10;

    private readonly CancellationTokenSource disposeCancellation = new();

    /// <summary>
    /// Narrows the list to one purchase order, set when the planner arrives from the order details.
    /// </summary>
    [Parameter]
    public Guid? PurchaseOrderId { get; set; }

    private int loadVersion;
    private int ordersVersion;
    private bool hasAppliedParameters;
    private Guid? appliedPurchaseOrderId;

    private IReadOnlyList<SupplierReturnSummary> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private string statusFilter = string.Empty;
    private Guid? purchaseOrderFilter;
    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private EditorTarget? editorTarget;

    private bool isOrderPickerOpen;
    private IReadOnlyList<PurchaseOrderListItem> orderOptions = [];
    private bool isOrdersLoading;
    private MaintenanceApiFailure? ordersFailure;
    private int orderPageNumber = 1;
    private string orderStatusFilter = MaintenanceFormat.ReceivedPurchaseOrder;
    private int orderTotalCount;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int? draftKpiCount;

    private bool IsDraftKpiSelected => statusFilter == MaintenanceFormat.DraftSupplierReturn;

    private bool IsPostedKpiSelected => statusFilter == MaintenanceFormat.PostedSupplierReturn;

    private decimal PostedQuantity => rows
        .Where(row => string.Equals(row.Status, MaintenanceFormat.PostedSupplierReturn, StringComparison.Ordinal))
        .Sum(row => row.TotalQuantity);

    private int TotalOrderPages => orderTotalCount == 0
        ? 1
        : (orderTotalCount + OrderOptionLimit - 1) / OrderOptionLimit;

    protected override Task OnParametersSetAsync()
    {
        // A new order from the purchase order details replaces the filter; other re-renders keep the page.
        if (hasAppliedParameters && appliedPurchaseOrderId == PurchaseOrderId)
        {
            return Task.CompletedTask;
        }

        hasAppliedParameters = true;
        appliedPurchaseOrderId = PurchaseOrderId;
        purchaseOrderFilter = PurchaseOrderId;
        pageNumber = 1;
        editorTarget = null;

        return LoadAsync();
    }

    private static string ClosedAt(SupplierReturnSummary row)
    {
        if (row.PostedUtc is { } postedUtc)
        {
            return MaintenanceFormat.CreatedUtc(postedUtc);
        }

        return row.CancelledUtc is { } cancelledUtc
            ? MaintenanceFormat.CreatedUtc(cancelledUtc)
            : MaintenanceFormat.MissingValue;
    }

    private void OpenReturn(SupplierReturnSummary row)
    {
        editorTarget = new EditorTarget(row.SupplierReturnId, row.PurchaseOrderId);
    }

    private void StartReturn(Guid purchaseOrderId)
    {
        isOrderPickerOpen = false;
        editorTarget = new EditorTarget(null, purchaseOrderId);
    }

    private Task CloseEditorAsync()
    {
        editorTarget = null;

        return LoadAsync();
    }

    private Task NewReturnAsync()
    {
        if (purchaseOrderFilter is { } purchaseOrderId)
        {
            StartReturn(purchaseOrderId);

            return Task.CompletedTask;
        }

        isOrderPickerOpen = !isOrderPickerOpen;

        if (!isOrderPickerOpen)
        {
            orderOptions = [];
            orderPageNumber = 1;
            orderTotalCount = 0;

            return Task.CompletedTask;
        }

        return LoadOrdersAsync();
    }

    private Task ShowAllOrdersAsync()
    {
        purchaseOrderFilter = null;
        pageNumber = 1;

        return LoadAsync();
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

    private Task ToggleDraftKpiAsync()
    {
        statusFilter = IsDraftKpiSelected ? string.Empty : MaintenanceFormat.DraftSupplierReturn;

        return ApplyFiltersAsync();
    }

    private Task TogglePostedKpiAsync()
    {
        statusFilter = IsPostedKpiSelected ? string.Empty : MaintenanceFormat.PostedSupplierReturn;

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

    private Task PreviousOrderPageAsync()
    {
        if (orderPageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        orderPageNumber--;

        return LoadOrdersAsync();
    }

    private Task NextOrderPageAsync()
    {
        if (orderPageNumber >= TotalOrderPages)
        {
            return Task.CompletedTask;
        }

        orderPageNumber++;

        return LoadOrdersAsync();
    }

    private Task ApplyOrderStatusAsync()
    {
        orderPageNumber = 1;

        return LoadOrdersAsync();
    }

    private async Task LoadOrdersAsync()
    {
        var requestVersion = ++ordersVersion;
        isOrdersLoading = true;
        ordersFailure = null;

        MaintenanceApiResult<PurchaseOrdersPage> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrdersAsync(
                new PurchaseOrderFilter(
                    null,
                    orderStatusFilter,
                    null,
                    null,
                    null,
                    null,
                    null,
                    orderPageNumber,
                    OrderOptionLimit),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != ordersVersion)
        {
            return;
        }

        isOrdersLoading = false;

        if (!result.IsSuccess)
        {
            ordersFailure = result.Failure;
            orderOptions = [];
            orderTotalCount = 0;

            return;
        }

        var page = result.Value!;
        orderPageNumber = page.PageNumber;
        orderTotalCount = page.TotalCount;
        orderOptions = page.Items;
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<SupplierReturnsPage> result;

        try
        {
            result = await MaintenanceApi.GetSupplierReturnsAsync(
                new SupplierReturnFilter(
                    string.IsNullOrWhiteSpace(statusFilter) ? null : statusFilter,
                    purchaseOrderFilter,
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
            draftKpiCount = null;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;
        rows = page.Items;

        await LoadDraftKpiCountAsync(requestVersion);
    }

    private async Task LoadDraftKpiCountAsync(int requestVersion)
    {
        MaintenanceApiResult<SupplierReturnsPage> result;

        try
        {
            result = await MaintenanceApi.GetSupplierReturnsAsync(
                new SupplierReturnFilter(MaintenanceFormat.DraftSupplierReturn, purchaseOrderFilter, 1, 1),
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

        draftKpiCount = result.IsSuccess ? result.Value!.TotalCount : null;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record EditorTarget(Guid? SupplierReturnId, Guid PurchaseOrderId);
}
