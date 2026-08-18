using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewMemberProfile : IDisposable
{
    [Parameter]
    public Guid CrewMemberId { get; set; }

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly DateOnly currentDate = DateOnly.FromDateTime(DateTime.Today);

    private CrewMemberProfileResponse? profile;
    private CrewApiFailure? failure;
    private bool isLoading = true;
    private bool isSensitiveDataRevealed;

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    protected override async Task OnInitializedAsync() => await LoadProfileAsync();

    protected override async Task OnParametersSetAsync()
    {
        // The [crewMemberId:guid] route parameter can change when the user navigates
        // directly between two profile URLs (e.g. from a search result link) without the
        // component being torn down and recreated - reload whenever it changes.
        if (profile is not null && profile.CrewMemberId != CrewMemberId)
        {
            await LoadProfileAsync();
        }
    }

    private async Task LoadProfileAsync()
    {
        isLoading = true;
        failure = null;

        CrewApiResult<CrewMemberProfileResponse> result;
        try
        {
            result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (result.IsSuccess)
        {
            profile = result.Value;
        }
        else
        {
            failure = result.Failure;
        }
    }

    private void ToggleSensitiveData() => isSensitiveDataRevealed = !isSensitiveDataRevealed;

    private async Task OnCertificateRegisteredAsync()
    {
        // "Refresh only affected profile sections": re-query the profile but only replace
        // the certificates collection on the currently displayed model, leaving personal/
        // employment fields and the assignments list untouched.
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Certificates = result.Value.Certificates };
        }
    }

    private async Task OnAssignmentRegisteredAsync()
    {
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = result.Value.Assignments };
        }
    }

    private CertificateState GetCertificateState(DateOnly expiryDate)
    {
        if (expiryDate < currentDate)
        {
            return new CertificateState("Expired", "crew-profile-indicator-expired");
        }

        return expiryDate <= currentDate.AddDays(30)
            ? new CertificateState("Expiring soon", "crew-profile-indicator-warning")
            : new CertificateState("Valid", "crew-profile-indicator-valid");
    }

    private static AssignmentState GetAssignmentState(CrewAssignmentProfileItemResponse assignment)
    {
        if (assignment.ActualSignOffDate is not null)
        {
            return new AssignmentState("Closed", "crew-profile-indicator-neutral");
        }

        return string.Equals(assignment.Status, "Active", StringComparison.OrdinalIgnoreCase)
            ? new AssignmentState("Active", "crew-profile-indicator-valid")
            : new AssignmentState("Planned", "crew-profile-indicator-warning");
    }

    private static string GetEmploymentStatusClass(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ? "employment-active" : "employment-inactive";

    private static string ShortId(Guid id) => id.ToString()[..8];

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record CertificateState(string Label, string Class);

    private sealed record AssignmentState(string Label, string Class);
}
