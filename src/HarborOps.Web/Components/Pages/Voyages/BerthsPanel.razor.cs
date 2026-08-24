using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class BerthsPanel : IDisposable
{
    internal const string ManageVoyagesPermission = "voyages.manage";
    internal const int PortLookupTake = 50;

    internal const string AvailableStatus = "Available";
    internal const string OccupiedStatus = "Occupied";
    internal const string ReservedStatus = "Reserved";
    internal const string OutOfServiceStatus = "Out of service";

    private static readonly string[] StatusOptions =
        [AvailableStatus, OccupiedStatus, ReservedStatus, OutOfServiceStatus];

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterBerthModel registerModel = new();

    private int loadVersion;
    private int portLookupVersion;

    private IReadOnlyList<PortLookupItemModel> portOptions = [];
    private VoyagesApiFailure? portLookupFailure;
    private bool isPortLookupLoading = true;
    private string? portSearch;

    private Guid? selectedPortId;

    private IReadOnlyList<BerthListItemModel> rows = [];
    private VoyagesApiFailure? failure;
    private bool isLoading;

    private string? searchFilter;
    private string? statusFilter;

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

    internal static string StatusModifier(string status)
    {
        return status switch
        {
            OccupiedStatus => "occupied",
            ReservedStatus => "reserved",
            OutOfServiceStatus => "out-of-service",
            AvailableStatus => "available",
            _ => "neutral",
        };
    }

    protected override Task OnInitializedAsync()
    {
        return LoadPortsAsync();
    }

    private async Task LoadPortsAsync()
    {
        var requestVersion = ++portLookupVersion;
        isPortLookupLoading = true;
        portLookupFailure = null;

        VoyagesApiResult<PortLookupResult> result;

        try
        {
            result = await VoyagesApi.LookupPortsAsync(
                portSearch,
                PortLookupTake,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != portLookupVersion)
        {
            return;
        }

        isPortLookupLoading = false;

        if (!result.IsSuccess)
        {
            portLookupFailure = result.Failure;
            portOptions = [];

            return;
        }

        portOptions = result.Value!.Items;

        if (selectedPortId is { } portId && portOptions.All(option => option.PortId != portId))
        {
            return;
        }

        if (selectedPortId is null && portOptions.Count > 0)
        {
            selectedPortId = portOptions[0].PortId;

            await LoadAsync();
        }
    }

    private Task SearchPortsAsync()
    {
        return LoadPortsAsync();
    }

    private Task SelectPortAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();

        selectedPortId = Guid.TryParse(value, out var portId) ? portId : null;
        pageNumber = 1;
        rows = [];
        totalCount = 0;
        registerSuccessMessage = null;

        return selectedPortId is null ? Task.CompletedTask : LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        searchFilter = null;
        statusFilter = null;
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
        if (selectedPortId is not { } portId)
        {
            return;
        }

        var requestVersion = ++loadVersion;
        isLoading = true;
        failure = null;

        VoyagesApiResult<BerthPage> result;

        try
        {
            result = await VoyagesApi.GetBerthsAsync(
                portId,
                searchFilter,
                statusFilter,
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

        registerModel.PortId = selectedPortId;
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

        var request = new RegisterBerthWebRequest(
            registerModel.PortId,
            registerModel.Name,
            registerModel.Length,
            registerModel.MaxDraft,
            registerModel.Status);

        VoyagesApiResult<RegisteredBerth> result;

        try
        {
            result = await VoyagesApi.RegisterBerthAsync(request, disposeCancellation.Token);
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
        registerSuccessMessage = "The berth was registered successfully.";

        if (registerModel.PortId is { } registeredPortId && registeredPortId != selectedPortId)
        {
            selectedPortId = registeredPortId;
            pageNumber = 1;
        }

        registerModel.Reset();

        await LoadAsync();
    }

    private sealed class RegisterBerthModel
    {
        public Guid? PortId { get; set; }

        public string? Name { get; set; }

        public decimal? Length { get; set; }

        public decimal? MaxDraft { get; set; }

        public string? Status { get; set; }

        public void Reset()
        {
            PortId = null;
            Name = null;
            Length = null;
            MaxDraft = null;
            Status = null;
        }
    }
}
