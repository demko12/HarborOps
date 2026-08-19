using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Shared;

public partial class VesselPicker : IDisposable
{
    private const int MinimumSearchLength = 2;
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 10;
    private const string DefaultPlaceholder = "Search vessel by name or IMO...";

    private string displayText = string.Empty;
    private IReadOnlyList<VesselListItemResponse> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
    private CancellationTokenSource? searchCts;

    // If Value is set from outside with no prior selection made through this picker
    // (an edit scenario, unused by the current create-only callers), the component has
    // no name to resolve it to and intentionally leaves the input text empty rather
    // than guessing or failing.
    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        displayText = e.Value?.ToString() ?? string.Empty;

        // Any manual edit invalidates a previously selected vessel so Value never
        // stays bound to a name the user has since typed over.
        if (Value is not null)
        {
            Value = null;
            await ValueChanged.InvokeAsync(null);
        }

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        if (displayText.Length < MinimumSearchLength)
        {
            isDropdownOpen = false;
            isSearching = false;
            searchFailed = false;
            results = [];
            return;
        }

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
        FleetApiResult<VesselPageResponse> result;

        try
        {
            result = await FleetApi.GetVesselsAsync(search, null, 1, MaxResults, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }

        // A newer keystroke may have replaced searchCts while this call was in flight.
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

    private async Task SelectVessel(VesselListItemResponse vessel)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        Value = vessel.VesselId;
        displayText = vessel.Name;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
        results = [];

        await ValueChanged.InvokeAsync(Value);
    }

    private void OnFocus()
    {
        if (Disabled)
        {
            return;
        }

        if (displayText.Length >= MinimumSearchLength && (results.Count > 0 || isSearching || searchFailed))
        {
            isDropdownOpen = true;
        }
    }

    private async Task OnBlurAsync()
    {
        // Delay the close so a click on a dropdown option is dispatched before the
        // list is removed from the DOM (no JS interop click-outside handler needed).
        await Task.Delay(BlurCloseDelayMilliseconds);
        isDropdownOpen = false;
        StateHasChanged();
    }

    public void Dispose()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
    }
}
