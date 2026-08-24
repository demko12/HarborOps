using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class PortsPanel : IDisposable
{
    internal const string ManageVoyagesPermission = "voyages.manage";

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterPortModel registerModel = new();

    private int loadVersion;

    private IReadOnlyList<PortListItemModel> rows = [];
    private VoyagesApiFailure? failure;
    private bool isLoading = true;

    private string? searchFilter;
    private string? countryFilter;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool isRegisterFormOpen;
    private bool isRegistering;
    private VoyagesApiFailure? registerFailure;
    private string? registerSuccessMessage;

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
        countryFilter = null;
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

        VoyagesApiResult<PortPage> result;

        try
        {
            result = await VoyagesApi.GetPortsAsync(
                searchFilter,
                countryFilter,
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

    private void OpenRegisterForm()
    {
        isRegisterFormOpen = true;
        registerModel.Reset();
        registerFailure = null;
        registerSuccessMessage = null;
    }

    private void CloseRegisterForm()
    {
        isRegisterFormOpen = false;
        registerFailure = null;
    }

    private async Task SubmitRegisterAsync()
    {
        if (isRegistering)
        {
            return;
        }

        isRegistering = true;
        registerFailure = null;
        registerSuccessMessage = null;

        var request = new RegisterPortWebRequest(
            registerModel.Name,
            registerModel.Country,
            registerModel.City,
            registerModel.UnLocode,
            registerModel.Latitude,
            registerModel.Longitude);

        VoyagesApiResult<RegisteredPort> result;

        try
        {
            result = await VoyagesApi.RegisterPortAsync(request, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isRegistering = false;

        if (!result.IsSuccess)
        {
            registerFailure = result.Failure;

            return;
        }

        isRegisterFormOpen = false;
        registerModel.Reset();
        registerSuccessMessage = "The port was registered successfully.";

        await LoadAsync();
    }

    private sealed class RegisterPortModel
    {
        public string? Name { get; set; }

        public string? Country { get; set; }

        public string? City { get; set; }

        public string? UnLocode { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public void Reset()
        {
            Name = null;
            Country = null;
            City = null;
            UnLocode = null;
            Latitude = null;
            Longitude = null;
        }
    }
}
