using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class FleetDetails : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private FleetDetailsWebModel? fleet;
    private FleetApiFailure? failure;
    private bool isLoading = true;
    [Parameter] public Guid FleetId { get; set; }
    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        isLoading = true;
        try { var result = await FleetApi.GetFleetDetailsAsync(FleetId, cancellation.Token); if (result.IsSuccess) fleet = result.Value; else failure = result.Failure; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { isLoading = false; }
    }

    private static string GetStatusClass(string status) => status.ToLowerInvariant() switch
    {
        "active" => "vessel-status-active",
        "en route" or "in transit" => "vessel-status-transit",
        "at port" => "vessel-status-port",
        "maintenance" or "under maintenance" => "vessel-status-maintenance",
        _ => "vessel-status-neutral",
    };

    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); }
}
