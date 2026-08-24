using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Shared;

public partial class PortPicker : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 10;
    private const string DefaultPlaceholder = "Click to browse ports, or type to search";

    private string displayText = string.Empty;
    private IReadOnlyList<PortLookupItemModel> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
    private CancellationTokenSource? searchCts;

    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public EventCallback<string?> SelectedLabelChanged { get; set; }

    [Parameter]
    public string? InputId { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    public void Dispose()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
    }

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        displayText = e.Value?.ToString() ?? string.Empty;

        if (Value is not null)
        {
            Value = null;
            await ValueChanged.InvokeAsync(null);
            await SelectedLabelChanged.InvokeAsync(null);
        }

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

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
        VoyagesApiResult<PortLookupResult> result;

        try
        {
            result = await VoyagesApi.LookupPortsAsync(search, MaxResults, cts.Token);
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

    private async Task SelectPortAsync(PortLookupItemModel port)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        Value = port.PortId;
        displayText = Label(port);
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
        results = [];

        await ValueChanged.InvokeAsync(Value);
        await SelectedLabelChanged.InvokeAsync(displayText);
    }

    private async Task OnBlurAsync()
    {
        await Task.Delay(BlurCloseDelayMilliseconds);
        isDropdownOpen = false;
        StateHasChanged();
    }

    internal void Reset()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;
        displayText = string.Empty;
        results = [];
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
    }

    private static string Label(PortLookupItemModel port)
    {
        return string.IsNullOrWhiteSpace(port.UnLocode)
            ? port.Name
            : $"{port.Name} ({port.UnLocode})";
    }
}
