using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Shared;

public partial class DocumentPicker : IDisposable
{
    private const int MinimumSearchLength = 2;
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 10;
    private const string DefaultPlaceholder = "Search document by number...";

    private string displayText = string.Empty;
    private IReadOnlyList<DocumentListItem> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
    private CancellationTokenSource? searchCts;

    // Required context: the document search is always scoped to a single vessel, never
    // a free cross-vessel search. No built-in reset when this changes - matches
    // VesselPicker's design where callers force remount via @key when needed.
    [Parameter]
    public Guid? VesselId { get; set; }

    // If Value is set from outside with no prior selection made through this picker
    // (an edit scenario, unused by the current create-only callers), the component has
    // no number to resolve it to and intentionally leaves the input text empty rather
    // than guessing or failing.
    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    // No search should be attempted without a vessel context to scope it to, so the
    // picker behaves as effectively disabled whenever VesselId is unset - combined
    // with, not replacing, the explicit Disabled parameter.
    private bool EffectiveDisabled => Disabled || VesselId is null;

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        displayText = e.Value?.ToString() ?? string.Empty;

        // Any manual edit invalidates a previously selected document so Value never
        // stays bound to a number the user has since typed over.
        if (Value is not null)
        {
            Value = null;
            await ValueChanged.InvokeAsync(null);
        }

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        if (VesselId is not { } vesselId || displayText.Length < MinimumSearchLength)
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

        await SearchAsync(vesselId, search, cts);
    }

    private async Task SearchAsync(Guid vesselId, string search, CancellationTokenSource cts)
    {
        DocumentsApiResult<DocumentsPage> result;

        try
        {
            result = await DocumentsApi.GetDocumentsAsync(
                new GetDocumentsQuery(vesselId, search, 1, MaxResults), cts.Token);
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

    private async Task SelectDocument(DocumentListItem document)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        Value = document.DocumentId;
        displayText = document.Number;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
        results = [];

        await ValueChanged.InvokeAsync(Value);
    }

    private void OnFocus()
    {
        if (EffectiveDisabled)
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
