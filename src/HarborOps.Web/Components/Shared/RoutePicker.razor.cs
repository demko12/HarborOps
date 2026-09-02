using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Shared;

public partial class RoutePicker : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 25;
    private const string DefaultPlaceholder = "Search routes by name...";

    private string displayText = string.Empty;
    private IReadOnlyList<RouteListItemModel> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
    private bool hasLoadedOnce;
    private CancellationTokenSource? searchCts;

    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public string? InputId { get; set; }

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        displayText = e.Value?.ToString() ?? string.Empty;

        if (Value is not null)
        {
            Value = null;
            await ValueChanged.InvokeAsync(null);
        }

        CancelSearch();

        isDropdownOpen = true;
        isSearching = true;
        searchFailed = false;
        results = [];

        var cts = new CancellationTokenSource();
        searchCts = cts;
        var search = displayText;

        try
        {
            await Task.Delay(DebounceMilliseconds, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await SearchAsync(search, cts);
    }

    private async Task SearchAsync(string search, CancellationTokenSource cts)
    {
        VoyagesApiResult<RoutePage> result;

        try
        {
            result = await VoyagesApi.GetRoutesAsync(search, pageNumber: 1, pageSize: MaxResults, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(searchCts, cts))
        {
            return;
        }

        isSearching = false;
        hasLoadedOnce = true;

        if (!result.IsSuccess)
        {
            searchFailed = true;
            results = [];
        }
        else
        {
            searchFailed = false;
            results = result.Value?.Items ?? [];
        }

        StateHasChanged();
    }

    private async Task SelectRouteAsync(RouteListItemModel route)
    {
        CancelSearch();

        Value = route.RouteId;
        displayText = route.Name;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;

        await ValueChanged.InvokeAsync(Value);
    }

    private async Task OnFocusAsync()
    {
        if (Disabled)
        {
            return;
        }

        isDropdownOpen = true;

        if (hasLoadedOnce)
        {
            return;
        }

        CancelSearch();

        isSearching = true;
        searchFailed = false;

        var cts = new CancellationTokenSource();
        searchCts = cts;

        await SearchAsync(displayText, cts);
    }

    private async Task OnBlurAsync()
    {
        await Task.Delay(BlurCloseDelayMilliseconds);
        isDropdownOpen = false;
        StateHasChanged();
    }

    private void CancelSearch()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;
    }

    internal void Reset()
    {
        CancelSearch();
        displayText = string.Empty;
        results = [];
        hasLoadedOnce = false;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
    }

    public void Dispose()
    {
        CancelSearch();
    }
}
