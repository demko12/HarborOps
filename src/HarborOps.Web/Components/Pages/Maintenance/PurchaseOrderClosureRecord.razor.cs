using System.Globalization;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PurchaseOrderClosureRecord : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    [Parameter, EditorRequired]
    public Guid PurchaseOrderId { get; set; }

    private Guid? loadedPurchaseOrderId;

    private PurchaseOrderClosureModel? closure;
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    protected override Task OnParametersSetAsync()
    {
        if (loadedPurchaseOrderId == PurchaseOrderId)
        {
            return Task.CompletedTask;
        }

        loadedPurchaseOrderId = PurchaseOrderId;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;

        MaintenanceApiResult<PurchaseOrderClosureModel> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrderClosureAsync(PurchaseOrderId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            closure = null;

            return;
        }

        closure = result.Value;
    }

    private static string ActorLabel(Guid userId)
    {
        return userId.ToString("N", CultureInfo.InvariantCulture)[..8];
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
