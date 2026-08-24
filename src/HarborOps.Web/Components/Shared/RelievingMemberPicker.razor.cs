using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Shared;

public partial class RelievingMemberPicker : IDisposable
{
    private const int DebounceMilliseconds = 300;
    private const int BlurCloseDelayMilliseconds = 200;
    private const int MaxResults = 10;
    private const string DefaultPlaceholder = "Click to browse crew members, or type to search";

    private string displayText = string.Empty;
    private IReadOnlyList<CrewMemberListItemResponse> results = [];
    private bool isDropdownOpen;
    private bool isSearching;
    private bool searchFailed;
    private CancellationTokenSource? searchCts;

    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public Guid? ExcludeCrewMemberId { get; set; }

    [Parameter]
    public string? Placeholder { get; set; }

    [Parameter]
    public string? InputId { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        displayText = e.Value?.ToString() ?? string.Empty;

        if (Value is not null)
        {
            Value = null;
            await ValueChanged.InvokeAsync(null);
        }

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        await StartSearchAsync(displayText, debounce: true);
    }

    private async Task SearchAsync(string search, CancellationTokenSource cts)
    {
        CrewApiResult<CrewMemberPageResponse> result;

        try
        {
            result = await CrewApi.GetMembersAsync(search, "Active", 1, MaxResults, cts.Token);
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
            var items = result.Value?.Items ?? [];
            results = ExcludeCrewMemberId is { } excludedId
                ? items.Where(member => member.CrewMemberId != excludedId).ToList()
                : items;
        }

        StateHasChanged();
    }

    private async Task SelectMember(CrewMemberListItemResponse member)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        Value = member.CrewMemberId;
        displayText = member.Name;
        isDropdownOpen = false;
        isSearching = false;
        searchFailed = false;
        results = [];

        await ValueChanged.InvokeAsync(Value);
    }

    private async Task OnFocus()
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

    private async Task OnBlurAsync()
    {
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
