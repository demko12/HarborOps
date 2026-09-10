using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class ReplenishmentPanel : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    /// <summary>
    /// Raised when the planner wants to order a short part through the purchase order form.
    /// </summary>
    [Parameter]
    public EventCallback<ReplenishmentOrderIntent> OnOrderRequested { get; set; }

    private int loadVersion;

    private IReadOnlyList<InventoryReplenishmentRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int CriticalCount => rows.Count(row => string.Equals(
        row.Classification,
        MaintenanceFormat.CriticalReplenishment,
        StringComparison.Ordinal));

    private int CoveredCount => rows.Count(row => string.Equals(
        row.Classification,
        MaintenanceFormat.CoveredReplenishment,
        StringComparison.Ordinal));

    private decimal PageShortage => rows.Sum(row => row.ShortageQuantity);

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private Task RequestOrderAsync(InventoryReplenishmentRow row)
    {
        return OnOrderRequested.InvokeAsync(new ReplenishmentOrderIntent(
            row.InventoryItemId,
            row.PartNumber,
            row.ShortageQuantity,
            row.SupplierId));
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private Task ApplyPageSizeAsync()
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

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<InventoryReplenishmentPage> result;

        try
        {
            result = await MaintenanceApi.GetInventoryReplenishmentAsync(
                new InventoryReplenishmentFilter(pageNumber, pageSize),
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
