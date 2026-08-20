using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class EquipmentPanel : IDisposable
{
    internal const int SuccessToastMilliseconds = 3000;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterEquipmentModel registerModel = new();

    private CancellationTokenSource? toastCancellation;

    private int loadVersion;

    private IReadOnlyList<EquipmentRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private string? categoryFilter;
    private string? manufacturerFilter;
    private string? searchFilter;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool isRegisterFormOpen;
    private bool isRegistering;
    private MaintenanceApiFailure? registerFailure;
    private string? registerSuccessMessage;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int DistinctVesselCount => rows.Select(row => row.VesselId).Distinct().Count();

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
        vesselFilter = null;
        categoryFilter = null;
        manufacturerFilter = null;
        searchFilter = null;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
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
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var filter = new EquipmentFilter(
            vesselFilter,
            string.IsNullOrWhiteSpace(categoryFilter) ? null : categoryFilter,
            string.IsNullOrWhiteSpace(manufacturerFilter) ? null : manufacturerFilter,
            string.IsNullOrWhiteSpace(searchFilter) ? null : searchFilter,
            pageNumber,
            pageSize);

        MaintenanceApiResult<EquipmentPage> result;

        try
        {
            result = await MaintenanceApi.GetEquipmentAsync(filter, cancellationToken);
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

        await ComposeRowsAsync(page.Items, cancellationToken, requestVersion);
    }

    private async Task ComposeRowsAsync(
        IReadOnlyList<EquipmentListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new EquipmentRow(
                item.EquipmentId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : MaintenanceFormat.ShortVesselReference(item.VesselId),
                item.Name,
                item.Category,
                item.Manufacturer,
                item.SerialNumber))
            .ToList();
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

        var request = new RegisterEquipmentRequest
        {
            VesselId = registerModel.VesselId,
            Name = registerModel.Name,
            Category = registerModel.Category,
            Manufacturer = registerModel.Manufacturer,
            SerialNumber = registerModel.SerialNumber,
        };

        MaintenanceApiResult<RegisteredEquipment> result;

        try
        {
            result = await MaintenanceApi.RegisterEquipmentAsync(request, disposeCancellation.Token);
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

        ShowSuccess("Equipment was registered successfully.");

        await LoadAsync();
    }

    private void ShowSuccess(string message)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        registerSuccessMessage = message;

        _ = HideSuccessAfterDelayAsync(cancellation);
    }

    private async Task HideSuccessAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SuccessToastMilliseconds, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        registerSuccessMessage = null;

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class RegisterEquipmentModel
    {
        public Guid? VesselId { get; set; }

        public string? Name { get; set; }

        public string? Category { get; set; }

        public string? Manufacturer { get; set; }

        public string? SerialNumber { get; set; }

        public void Reset()
        {
            VesselId = null;
            Name = null;
            Category = null;
            Manufacturer = null;
            SerialNumber = null;
        }
    }
}
