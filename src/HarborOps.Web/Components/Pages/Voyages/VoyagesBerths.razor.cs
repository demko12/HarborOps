using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class VoyagesBerths : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private int? berthsKpiCount;

    protected override async Task OnInitializedAsync()
    {
        VoyagesApiResult<MasterDataSummary> result;

        try
        {
            result = await VoyagesApi.GetMasterDataSummaryAsync(disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        berthsKpiCount = result.IsSuccess ? result.Value!.BerthCount : null;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
