using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PurchaseOrderReceiptHistory : IDisposable
{
    internal const int PageSize = 5;

    private readonly CancellationTokenSource disposeCancellation = new();

    [Parameter, EditorRequired]
    public Guid PurchaseOrderId { get; set; }

    /// <summary>
    /// Quantity recorded as received before deliveries were tracked; it has no delivery rows of its own.
    /// </summary>
    [Parameter]
    public decimal LegacyReceivedQuantity { get; set; }

    /// <summary>
    /// Changes whenever a receipt was posted, so the history reloads from its first page.
    /// </summary>
    [Parameter]
    public int RefreshKey { get; set; }

    private (Guid PurchaseOrderId, int RefreshKey)? loadedKey;
    private int loadVersion;

    private IReadOnlyList<PurchaseOrderReceiptModel> receipts = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;
    private int pageNumber = 1;
    private int totalCount;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + PageSize - 1) / PageSize;

    protected override Task OnParametersSetAsync()
    {
        var key = (PurchaseOrderId, RefreshKey);

        if (loadedKey == key)
        {
            return Task.CompletedTask;
        }

        loadedKey = key;
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

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<PurchaseOrderReceiptsPage> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrderReceiptsAsync(
                PurchaseOrderId,
                pageNumber,
                PageSize,
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
            receipts = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        receipts = page.Items;
    }

    private static string StatusChange(PurchaseOrderReceiptModel receipt)
    {
        return string.Equals(receipt.OrderStatusBefore, receipt.OrderStatusAfter, StringComparison.Ordinal)
            ? ProcurementFormat.StatusLabel(receipt.OrderStatusAfter)
            : $"{ProcurementFormat.StatusLabel(receipt.OrderStatusBefore)} → {ProcurementFormat.StatusLabel(receipt.OrderStatusAfter)}";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
