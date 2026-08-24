using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class RoutesPanel : IDisposable
{
    internal const string ManageVoyagesPermission = "voyages.manage";

    private readonly CancellationTokenSource disposeCancellation = new();

    private int loadVersion;
    private int detailsVersion;

    private IReadOnlyList<RouteListItemModel> rows = [];
    private VoyagesApiFailure? failure;
    private bool isLoading = true;

    private string? searchFilter;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private Guid? selectedRouteId;
    private RouteDetails? details;
    private VoyagesApiFailure? detailsFailure;
    private bool isDetailsLoading;

    private bool CanManage => AuthorizationState.HasPermission(ManageVoyagesPermission);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        searchFilter = null;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
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

        VoyagesApiResult<RoutePage> result;

        try
        {
            result = await VoyagesApi.GetRoutesAsync(
                searchFilter,
                pageNumber,
                pageSize,
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

    private async Task SelectRouteAsync(Guid routeId)
    {
        var requestVersion = ++detailsVersion;
        selectedRouteId = routeId;
        isDetailsLoading = true;
        detailsFailure = null;
        details = null;

        VoyagesApiResult<RouteDetails> result;

        try
        {
            result = await VoyagesApi.GetRouteDetailsAsync(routeId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != detailsVersion)
        {
            return;
        }

        isDetailsLoading = false;

        if (!result.IsSuccess)
        {
            detailsFailure = result.Failure;

            return;
        }

        details = result.Value;
    }

    private void CloseDetails()
    {
        selectedRouteId = null;
        details = null;
        detailsFailure = null;
    }

    private Task RetryDetailsAsync()
    {
        return selectedRouteId is { } routeId
            ? SelectRouteAsync(routeId)
            : Task.CompletedTask;
    }
}
