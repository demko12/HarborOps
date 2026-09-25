using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class VoyagesPorts : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private int? portsKpiCount;

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

        portsKpiCount = result.IsSuccess ? result.Value!.PortCount : null;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
