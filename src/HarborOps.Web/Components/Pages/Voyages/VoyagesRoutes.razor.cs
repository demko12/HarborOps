using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class VoyagesRoutes : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private int? routesKpiCount;
    private decimal? avgRouteDistanceKpi;

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

        var summary = result.IsSuccess ? result.Value! : null;
        routesKpiCount = summary?.RouteCount;
        avgRouteDistanceKpi = summary?.AverageRouteDistance;
    }

    internal static string FormatAvgDistance(decimal? value)
    {
        return value is { } distance ? MasterDataFormat.DistanceNm(distance) : Voyages.MissingValue;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
