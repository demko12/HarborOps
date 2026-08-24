using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Shared;

public partial class PortPicker : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 10;
    private const string DefaultPlaceholder = "Click to browse ports, or type to search";

    private string displayText = string.Empty;
    private IReadOnlyList<PortListItemModel> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
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

    [Parameter]
    public bool ShipyardsOnly { get; set; }

    private static string PortLocation(PortListItemModel port)
    {
        return string.IsNullOrWhiteSpace(port.City)
            ? port.Country
            : $"{port.City}, {port.Country}";
    }

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

        await StartSearchAsync(displayText, debounce: true);
    }

    private async Task OnFocusAsync()
    {
        if (Disabled)
        {
            return;
        }

        isDropdownOpen = true;

        if (results.Count > 0 || isSearching || searchFailed)
        {
            return;
        }

        await StartSearchAsync(string.Empty, debounce: false);
    }

    private async Task StartSearchAsync(string search, bool debounce)
    {
        isDropdownOpen = true;
        isSearching = true;
        searchFailed = false;
        results = [];

        var cts = new CancellationTokenSource();
        searchCts = cts;

        if (debounce)
        {
            try
            {
                await Task.Delay(DebounceMilliseconds, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        await SearchAsync(search, cts);
    }

    private async Task SearchAsync(string search, CancellationTokenSource cts)
    {
        VoyagesApiResult<PortPage> result;

        try
        {
            result = await VoyagesApi.SearchPortsAsync(
                search,
                ShipyardsOnly,
                pageNumber: 1,
                pageSize: MaxResults,
                cts.Token);
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

    private async Task SelectPortAsync(PortListItemModel port)
    {
        CancelSearch();

        Value = port.PortId;
        displayText = port.Name;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
        results = [];

        await ValueChanged.InvokeAsync(Value);
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
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
    }

    public void Dispose()
    {
        CancelSearch();
    }
}
