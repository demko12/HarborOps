using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class RegisterRoute : IDisposable
{
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const int MinimumStops = 2;

    private readonly CancellationTokenSource pageCancellation = new();
    private readonly List<RouteStopDraft> stops = [new(), new()];

    private string? name;
    private string? description;

    private VoyagesApiFailure? failure;
    private bool isSubmitting;

    private bool CanManageVoyages => AuthorizationState.HasPermission(ManageVoyagesPermission);

    private decimal TotalDistance => stops.Sum(stop => stop.DistanceFromPrevious ?? 0m);

    private int TotalDuration => stops.Sum(stop => stop.EstimatedDurationFromPrevious ?? 0);

    public void Dispose()
    {
        pageCancellation.Cancel();
        pageCancellation.Dispose();
    }

    private void AddStop()
    {
        stops.Add(new RouteStopDraft());
    }

    private void RemoveStop(int index)
    {
        if (stops.Count <= MinimumStops)
        {
            return;
        }

        stops.RemoveAt(index);
        ClearFirstStopLeg();
    }

    private void MoveStopUp(int index)
    {
        if (index <= 0)
        {
            return;
        }

        SwapStops(index, index - 1);
    }

    private void MoveStopDown(int index)
    {
        if (index >= stops.Count - 1)
        {
            return;
        }

        SwapStops(index, index + 1);
    }

    private void SwapStops(int first, int second)
    {
        (stops[first], stops[second]) = (stops[second], stops[first]);

        ClearFirstStopLeg();
    }

    private void ClearFirstStopLeg()
    {
        var firstStop = stops[0];

        firstStop.DistanceFromPrevious = 0m;
        firstStop.EstimatedDurationFromPrevious = 0;
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        failure = null;

        var request = new RegisterRouteWebRequest(
            name,
            description,
            TotalDistance,
            TotalDuration,
            [.. stops.Select((stop, index) => new RegisterRouteStopWebRequest(
                stop.PortId,
                index + 1,
                index == 0 ? 0m : stop.DistanceFromPrevious,
                index == 0 ? 0 : stop.EstimatedDurationFromPrevious))]);

        VoyagesApiResult<RegisteredRoute> result;

        try
        {
            result = await VoyagesApi.RegisterRouteAsync(request, pageCancellation.Token);
        }
        catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
        {
            return;
        }

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;

            return;
        }

        Navigation.NavigateTo(Voyages.RoutesTabUrl);
    }

    private sealed class RouteStopDraft
    {
        public Guid? PortId { get; set; }

        public decimal? DistanceFromPrevious { get; set; }

        public int? EstimatedDurationFromPrevious { get; set; }
    }
}
