using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class BillOfLadingPrint : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private BillOfLadingPrintRow? model;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedBillOfLadingId;

    [Parameter]
    public Guid BookingId { get; set; }

    [Parameter]
    public Guid BillOfLadingId { get; set; }

    private string BookingHref => $"/cargo/{BookingId}";

    private string PageHeading =>
        model is { } bill ? $"Bill of lading {bill.BillNumber}" : "Bill of lading";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (loadedBillOfLadingId == BillOfLadingId && !isLoading)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        loadedBillOfLadingId = BillOfLadingId;

        try
        {
            var result = await CargoApi.GetBillOfLadingPrintAsync(
                BillOfLadingId,
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                model = result.Value;
            }
            else
            {
                model = null;
                failure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;
        }
    }
}
