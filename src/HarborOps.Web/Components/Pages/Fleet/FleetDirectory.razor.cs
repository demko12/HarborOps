using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class FleetDirectory : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private FleetDirectoryWebModel? directory;
    private FleetApiFailure? failure;
    private FleetApiFailure? createFailure;
    private string? search;
    private string newFleetName = string.Empty;
    private string newFleetDescription = string.Empty;
    private bool isLoading = true;
    private bool isCreating;
    private bool cardView;
    private bool CanManage => AuthorizationState.HasPermission(CurrentUserAuthorizationState.FleetManagePermission);
    protected override async Task OnInitializedAsync() => await LoadAsync();
    private async Task SearchAsync() => await LoadAsync();
    private async Task ResetAsync() { search = null; await LoadAsync(); }
    private async Task LoadAsync()
    {
        isLoading = true; failure = null;
        try { var result = await FleetApi.GetFleetsAsync(search, 1, 25, cancellation.Token); if (result.IsSuccess) directory = result.Value; else failure = result.Failure; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        finally { isLoading = false; }
    }
    private async Task CreateAsync()
    {
        if (isCreating) return;
        isCreating = true; createFailure = null;
        try { var result = await FleetApi.CreateFleetAsync(new CreateFleetWebRequest(newFleetName, newFleetDescription), cancellation.Token); if (!result.IsSuccess) { createFailure = result.Failure; return; } newFleetName = string.Empty; newFleetDescription = string.Empty; await LoadAsync(); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { isCreating = false; }
    }
    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); }
}
