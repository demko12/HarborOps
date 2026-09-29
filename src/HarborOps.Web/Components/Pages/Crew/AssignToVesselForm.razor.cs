using System.Globalization;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class AssignToVesselForm
{
    private const int PositionOptionsPageSize = 100;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Today);

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter, EditorRequired]
    public string CrewMemberName { get; set; } = string.Empty;

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private FormModel model = new();
    private bool isSubmitting;

    public bool IsSubmitting => isSubmitting;
    private string? successMessage;
    private string? conflictMessage;
    private string? generalError;
    private string? availabilityHint;
    private IReadOnlyList<string> fieldErrors = [];

    private IReadOnlyList<CrewPositionListItemResponse> positionOptions = [];
    private bool isLoadingPositions = true;

    // Bumped on every successful assignment and bound to VesselPicker's @key, forcing
    // Blazor to discard the old instance and mount a fresh one - same pattern as
    // Documents.razor's vesselPickerResetToken, since VesselPicker does not resync its
    // internal display text when Value is set back to null from outside.
    private int vesselPickerResetToken;

    private string RankPlaceholder => isLoadingPositions
        ? "Loading ranks..."
        : positionOptions.Count == 0
            ? "No ranks available"
            : "Select...";

    protected override async Task OnInitializedAsync()
    {
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
        model.RankId = Guid.TryParse(e.Value?.ToString(), out var rankId) ? rankId : null;
    }

    private async Task CheckAvailabilityConflictsAsync()
    {
        availabilityHint = null;

        if (model.SignOnDate is not { } signOn || model.PlannedSignOffDate is not { } plannedSignOff || plannedSignOff < signOn)
        {
            return;
        }

        var result = await CrewApi.GetAvailabilityConflictsAsync(CrewMemberId, signOn, plannedSignOff, CancellationToken.None);
        if (!result.IsSuccess || result.Value is null)
        {
            return;
        }

        var unavailability = result.Value.Conflicts
            .Where(conflict => conflict.RecordType == "ApprovedUnavailability")
            .ToList();

        if (unavailability.Count == 0)
        {
            return;
        }

        var period = unavailability[0];
        availabilityHint =
            $"{CrewMemberName} has approved unavailability "
            + $"{period.StartDate:dd MMM yyyy} – {period.EndDate:dd MMM yyyy}"
            + $"{(string.IsNullOrWhiteSpace(period.Detail) ? string.Empty : $" ({AvailabilityTypeLabel(period.Detail!)})")}. "
            + "Assigning over these dates will be rejected.";
    }

    private static string AvailabilityTypeLabel(string type) => type switch
    {
        "AnnualLeave" => "annual leave",
        "SickLeave" => "sick leave",
        "Training" => "training",
        "OtherUnavailable" => "other",
        _ => type,
    };

    internal static string FormatIsoDate(DateOnly value)
    {
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting || model.VesselId is null)
        {
            return;
        }

        conflictMessage = null;
        generalError = null;
        fieldErrors = [];
        successMessage = null;
        availabilityHint = null;
        isSubmitting = true;

        var request = new RegisterCrewAssignmentRequest
        {
            CrewMemberId = CrewMemberId,
            VesselId = model.VesselId,
            RankId = model.RankId,
            SignOnDate = model.SignOnDate,
            PlannedSignOffDate = model.PlannedSignOffDate,
        };

        var result = await CrewApi.RegisterAssignmentAsync(request, CancellationToken.None);

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsConflict)
            {
                // The server reports the conflict by crew member ID; the page already
                // knows the member's name, so show that instead of a raw GUID. A 409 can
                // also mean the member has approved unavailability over the chosen dates.
                conflictMessage = failure.Title == "Crew member is unavailable."
                    ? $"{CrewMemberName} has approved unavailability over the selected dates. Choose dates that do not overlap."
                    : $"{CrewMemberName} already has an open assignment.";
            }
            else if (failure.IsValidation)
            {
                fieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                generalError = failure.Message;
            }

            // Entered values (including the selected vessel/rank) are intentionally
            // preserved on every failure branch above.
            return;
        }

        successMessage = "Crew member assigned to vessel.";
        model = new FormModel();
        vesselPickerResetToken++;

        await OnRegistered.InvokeAsync();
    }

    private sealed class FormModel
    {
        public Guid? VesselId { get; set; }
        public Guid? RankId { get; set; }
        public DateOnly? SignOnDate { get; set; }
        public DateOnly? PlannedSignOffDate { get; set; }
    }
}
