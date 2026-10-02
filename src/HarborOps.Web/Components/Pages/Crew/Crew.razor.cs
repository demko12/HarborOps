using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class Crew
{
    private const int PageSize = 24;
    private const string ActiveStatus = "Active";
    private const string ExpiredPassportFilter = "Expired";
    private const string ExpiringSoonPassportFilter = "ExpiringSoon";

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;
    private readonly DateOnly currentDate = DateOnly.FromDateTime(DateTime.Today);
    private CrewMemberPageResponse? roster;
    private CrewApiFailure? failure;
    private string? search;
    private string? employmentStatus;
    private string? passportExpiry;
    private CrewMembersSummaryResponse? summary;
    private bool isLoading = true;
    private bool cardView = false;

    private int TotalPages => roster is null || roster.TotalCount == 0 ? 1 : (int)Math.Ceiling((double)roster.TotalCount / roster.PageSize);
    private int FirstRowNumber => roster is null || roster.TotalCount == 0 ? 0 : ((roster.PageNumber - 1) * roster.PageSize) + 1;
    private int LastRowNumber => roster is null ? 0 : Math.Min(roster.PageNumber * roster.PageSize, roster.TotalCount);
    private bool IsActiveKpiSelected => employmentStatus == ActiveStatus;
    private bool IsExpiredKpiSelected => passportExpiry == ExpiredPassportFilter;
    private bool IsExpiringSoonKpiSelected => passportExpiry == ExpiringSoonPassportFilter;
    private IReadOnlyList<RankSummary> RankDistribution
    {
        get
        {
            if (roster is null || roster.Items.Count == 0)
            {
                return [];
            }

            return roster.Items
                .GroupBy(member => member.BaseRankName)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new RankSummary(group.Key, group.Count(), Math.Max(8, group.Count() * 100 / roster.Items.Count)))
                .ToList();
        }
    }
    private IReadOnlyList<CrewMemberListItemResponse> PassportAttention => roster?.Items
        .Where(member => member.PassportExpiry <= currentDate.AddDays(90))
        .OrderBy(member => member.PassportExpiry)
        .Take(5)
        .ToList() ?? [];

    protected override async Task OnInitializedAsync() => await Task.WhenAll(LoadRosterAsync(), LoadSummaryAsync());

    private async Task LoadSummaryAsync()
    {
        CrewApiResult<CrewMembersSummaryResponse> result;
        try
        {
            result = await CrewApi.GetMembersSummaryAsync(disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        summary = result.IsSuccess ? result.Value : null;
    }

    private static string FormatCount(int? count) => count?.ToString("N0") ?? "—";

    private async Task ToggleActiveKpiAsync()
    {
        employmentStatus = IsActiveKpiSelected ? null : ActiveStatus;
        await SearchAsync();
    }

    private async Task ToggleExpiredKpiAsync()
    {
        passportExpiry = IsExpiredKpiSelected ? null : ExpiredPassportFilter;
        await SearchAsync();
    }

    private async Task ToggleExpiringSoonKpiAsync()
    {
        passportExpiry = IsExpiringSoonKpiSelected ? null : ExpiringSoonPassportFilter;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (roster is not null) roster = roster with { PageNumber = 1 };
        await LoadRosterAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (roster is not null && roster.PageNumber > 1) { roster = roster with { PageNumber = roster.PageNumber - 1 }; await LoadRosterAsync(); }
    }

    private async Task ResetFiltersAsync()
    {
        search = null;
        employmentStatus = null;
        passportExpiry = null;
        await SearchAsync();
    }

    private async Task NextPageAsync()
    {
        if (roster is not null && roster.PageNumber < TotalPages) { roster = roster with { PageNumber = roster.PageNumber + 1 }; await LoadRosterAsync(); }
    }

    private async Task LoadRosterAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        CrewApiResult<CrewMemberPageResponse> result;
        try
        {
            result = await CrewApi.GetMembersAsync(search, employmentStatus, passportExpiry, roster?.PageNumber ?? 1, PageSize, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (result.IsSuccess) roster = result.Value; else failure = result.Failure;
        isLoading = false;
    }

    internal static PassportState GetPassportState(DateOnly expiry, DateOnly currentDate) => expiry < currentDate ? new(true, "passport-expired") : new(false, "");
    internal static string GetEmploymentStatusClass(string status)
    {
        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return "employment-active";
        if (string.Equals(status, "OnLeave", StringComparison.OrdinalIgnoreCase)) return "employment-onleave";
        if (string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)) return "employment-suspended";
        if (string.Equals(status, "Terminated", StringComparison.OrdinalIgnoreCase)) return "employment-terminated";
        return "employment-inactive";
    }
    public void Dispose() { disposeCancellation.Cancel(); disposeCancellation.Dispose(); }
    internal sealed record PassportState(bool IsExpired, string Class);
    private sealed record RankSummary(string Name, int Count, int Percentage);
}
