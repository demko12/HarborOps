using HarborOps.Web.Api.Cargo;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoBookings
{
    private CancellationTokenSource loadCancellation = new();
    private IReadOnlyList<CargoBookingRow> rows = [];
    private CargoApiFailure? failure;
    private string? search;
    private string status = string.Empty;
    private bool hazardousOnly;
    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private bool isLoading = true;

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
        search = null;
        status = string.Empty;
        hazardousOnly = false;
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
        loadCancellation.Cancel();
        loadCancellation.Dispose();
        loadCancellation = new CancellationTokenSource();

        var cancellationToken = loadCancellation.Token;
        isLoading = true;
        failure = null;

        var filter = new CargoBookingFilter(
            search,
            string.IsNullOrEmpty(status) ? null : status,
            hazardousOnly ? true : null,
            pageNumber,
            pageSize);

        var result = await CargoApi.SearchAsync(filter, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
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
        rows = page.Items;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;
    }

    private static string? StatusModifier(string status)
    {
        if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return "done";
        }

        return CargoBookingFormat.LifecycleStep(status) > 1 ? "active" : null;
    }

    public void Dispose()
    {
        loadCancellation.Cancel();
        loadCancellation.Dispose();
    }
}
