using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class SuppliersPanel : IDisposable
{
    internal const int SuccessToastMilliseconds = 3000;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterSupplierModel registerModel = new();

    private int loadVersion;

    private IReadOnlyList<SupplierListItem> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private string searchText = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool isRegisterFormOpen;
    private bool isRegistering;
    private MaintenanceApiFailure? registerFailure;
    private string? registerSuccessMessage;

    private CancellationTokenSource? toastCancellation;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

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
        searchText = string.Empty;
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

        var filter = new SupplierFilter(
            string.IsNullOrWhiteSpace(searchText) ? null : searchText,
            pageNumber,
            pageSize);

        MaintenanceApiResult<SuppliersPage> result;

        try
        {
            result = await MaintenanceApi.GetSuppliersAsync(filter, disposeCancellation.Token);
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

        var request = new RegisterSupplierRequest
        {
            Name = registerModel.Name,
            ContactPerson = registerModel.ContactPerson,
            PhoneEmail = registerModel.PhoneEmail,
            Address = registerModel.Address,
            PaymentTerms = registerModel.PaymentTerms,
        };

        MaintenanceApiResult<RegisteredSupplier> result;

        try
        {
            result = await MaintenanceApi.RegisterSupplierAsync(request, disposeCancellation.Token);
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

        ShowToast("Supplier was registered successfully.");

        await LoadAsync();
    }

    private void ShowToast(string message)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        registerSuccessMessage = message;

        _ = HideToastAfterDelayAsync(cancellation);
    }

    private async Task HideToastAfterDelayAsync(CancellationTokenSource cancellation)
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

    private sealed class RegisterSupplierModel
    {
        public string? Name { get; set; }

        public string? ContactPerson { get; set; }

        public string? PhoneEmail { get; set; }

        public string? Address { get; set; }

        public string? PaymentTerms { get; set; }

        public void Reset()
        {
            Name = null;
            ContactPerson = null;
            PhoneEmail = null;
            Address = null;
            PaymentTerms = null;
        }
    }
}
