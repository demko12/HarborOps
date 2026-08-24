using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.WebUtilities;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class Fleet : IDisposable
{

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;
    private VesselPageResponse? page;
    private FleetApiFailure? failure;
    private string? search;
    private int vesselPickerResetToken;
    private string? status;
    private int pageNumber = 1;
    private bool isLoading = true;

    private int TotalPages => page is null || page.TotalCount == 0 ? 1 : (int)Math.Ceiling((double)page.TotalCount / page.PageSize);
    private int FirstRowNumber => page is null || page.TotalCount == 0 ? 0 : ((page.PageNumber - 1) * page.PageSize) + 1;
    private int LastRowNumber => page is null ? 0 : Math.Min(page.PageNumber * page.PageSize, page.TotalCount);
    private int OperationalVessels => page?.Items.Count(vessel =>
    !string.Equals(vessel.CurrentStatus, "Inactive", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(vessel.CurrentStatus, "Maintenance", StringComparison.OrdinalIgnoreCase)) ?? 0;

    private int EnRouteVessels => page?.Items.Count(vessel =>
        string.Equals(vessel.CurrentStatus, "En Route", StringComparison.OrdinalIgnoreCase)) ?? 0;

    private int AtPortVessels => page?.Items.Count(vessel =>
        string.Equals(vessel.CurrentStatus, "At Port", StringComparison.OrdinalIgnoreCase)) ?? 0;

    private int MaintenanceVessels => page?.Items.Count(vessel =>
        string.Equals(vessel.CurrentStatus, "Maintenance", StringComparison.OrdinalIgnoreCase)) ?? 0;
    private int VesselTypeCount => page?.Items.Select(vessel => vessel.VesselTypeId).Distinct().Count() ?? 0;
    private decimal TotalDwt => page?.Items.Sum(vessel => vessel.Dwt ?? 0) ?? 0;

    protected override async Task OnInitializedAsync()
    {
        RestoreFiltersFromQuery();
        await LoadVesselsAsync();
    }

    private void RestoreFiltersFromQuery()
    {
        var query = QueryHelpers.ParseQuery(new Uri(Navigation.Uri).Query);

        if (query.TryGetValue("search", out var searchValues) && !string.IsNullOrWhiteSpace(searchValues))
        {
            search = searchValues.ToString();
        }

        if (query.TryGetValue("status", out var statusValues) && !string.IsNullOrWhiteSpace(statusValues))
        {
            status = statusValues.ToString();
        }

        if (query.TryGetValue("page", out var pageValues)
            && int.TryParse(pageValues, out var parsedPage)
            && parsedPage > 0)
        {
            pageNumber = parsedPage;
        }
    }

    private async Task SearchAsync()
    {
        pageNumber = 1;
        await LoadVesselsAsync();
    }

    private async Task SearchByTextAsync(string value)
    {
        search = value;
        await SearchAsync();
    }

    private async Task SelectVesselFromSearchAsync(string vesselName)
    {
        search = vesselName;
        await SearchAsync();
    }

    private async Task ResetFiltersAsync()
    {
        search = null;
        vesselPickerResetToken++;
        status = null;
        await SearchAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (pageNumber > 1)
        {
            pageNumber--;
            await LoadVesselsAsync();
        }
    }

    private async Task NextPageAsync()
    {
        if (pageNumber < TotalPages)
        {
            pageNumber++;
            await LoadVesselsAsync();
        }
    }

    private async Task LoadVesselsAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        FleetApiResult<VesselPageResponse> result;
        try
        {
            result = await FleetApi.GetVesselsAsync(search, status, pageNumber, 25, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (result.IsSuccess && result.Value is not null)
        {
            page = result.Value;
            pageNumber = page.PageNumber;
            Navigation.NavigateTo(BuildCurrentUri(), replace: true);
        }
        else
        {
            failure = result.Failure;
        }

        isLoading = false;
    }

    private string BuildCurrentUri()
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (pageNumber > 1)
        {
            parameters.Add($"page={pageNumber}");
        }

        return parameters.Count == 0 ? "/fleet" : $"/fleet?{string.Join('&', parameters)}";
    }

    private string DetailsUrl(Guid vesselId)
    {
        return $"/fleet/{vesselId}?returnTo={Uri.EscapeDataString(BuildCurrentUri())}";
    }

    internal static string GetStatusClass(string status) => status.ToLowerInvariant() switch
    {
        "active" => "vessel-status-active",
        "in transit" => "vessel-status-transit",
        "en route" => "vessel-status-transit",
        "under maintenance" => "vessel-status-maintenance",
        "maintenance" => "vessel-status-maintenance",
        "at port" => "vessel-status-port",
        _ => "vessel-status-neutral",
    };

    internal static string FormatDwt(decimal dwt) => dwt >= 1_000_000
        ? $"{dwt / 1_000_000m:0.#}M"
        : dwt >= 1_000
            ? $"{dwt / 1_000m:0.#}K"
            : $"{dwt:0}";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
