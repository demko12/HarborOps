using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class EditCrewMemberProfileForm
{
    private const int PositionOptionsPageSize = 100;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter, EditorRequired]
    public string FirstName { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string LastName { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public DateOnly DateOfBirth { get; set; }

    [Parameter, EditorRequired]
    public string Nationality { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string PassportNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public DateOnly PassportExpiry { get; set; }

    [Parameter, EditorRequired]
    public string CdcNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public DateOnly HireDate { get; set; }

    [Parameter, EditorRequired]
    public Guid BaseRankId { get; set; }

    [Parameter]
    public EventCallback OnUpdated { get; set; }

    private FormModel model = new();
    private bool isSubmitting;
    private string? successMessage;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    private IReadOnlyList<CrewPositionListItemResponse> positionOptions = [];
    private bool isLoadingPositions = true;

    private string RankPlaceholder => isLoadingPositions
        ? "Loading ranks..."
        : positionOptions.Count == 0
            ? "No ranks available"
            : "Select...";

    protected override async Task OnInitializedAsync()
    {
        model = new FormModel
        {
            FirstName = FirstName,
            LastName = LastName,
            DateOfBirth = DateOfBirth,
            Nationality = Nationality,
            PassportNumber = PassportNumber,
            PassportExpiry = PassportExpiry,
            CdcNumber = CdcNumber,
            HireDate = HireDate,
            BaseRankId = BaseRankId,
        };

        await LoadPositionsAsync();
    }

    private async Task LoadPositionsAsync()
    {
        isLoadingPositions = true;

        var result = await CrewApi.GetPositionsAsync(null, PositionOptionsPageSize, CancellationToken.None);

        isLoadingPositions = false;
        positionOptions = result.IsSuccess ? result.Value!.Items : [];
    }

    private void OnRankChanged(ChangeEventArgs e)
    {
        model.BaseRankId = Guid.TryParse(e.Value?.ToString(), out var rankId) ? rankId : null;
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        conflictMessage = null;
        generalError = null;
        fieldErrors = [];
        successMessage = null;
        isSubmitting = true;

        var request = new UpdateCrewMemberRequest
        {
            FirstName = model.FirstName,
            LastName = model.LastName,
            DateOfBirth = model.DateOfBirth,
            Nationality = model.Nationality,
            PassportNumber = model.PassportNumber,
            PassportExpiry = model.PassportExpiry,
            CdcNumber = model.CdcNumber,
            HireDate = model.HireDate,
            BaseRankId = model.BaseRankId,
        };

        var result = await CrewApi.UpdateProfileAsync(CrewMemberId, request, CancellationToken.None);

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsConflict)
            {
                conflictMessage = failure.Message;
            }
            else if (failure.IsValidation)
            {
                fieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                generalError = failure.Message;
            }

            return;
        }

        successMessage = "Profile updated.";

        await OnUpdated.InvokeAsync();
    }

    private sealed class FormModel
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Nationality { get; set; }
        public string? PassportNumber { get; set; }
        public DateOnly? PassportExpiry { get; set; }
        public string? CdcNumber { get; set; }
        public DateOnly? HireDate { get; set; }
        public Guid? BaseRankId { get; set; }
    }
}
