using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class AssignToVesselForm
{
    private const int PositionOptionsPageSize = 100;
    private const string RankFieldKey = "rankId";

    // Field names exactly as sent by RegisterCrewAssignmentValidator (Application layer).
    private const string CrewMemberIdFieldKey = "crewMemberId";
    private const string VesselIdFieldKey = "vesselId";
    private const string SignOnDateFieldKey = "signOnDate";
    private const string PlannedSignOffDateFieldKey = "plannedSignOffDate";

    private static readonly IReadOnlyDictionary<string, string> ServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VesselIdFieldKey] = nameof(FormModel.VesselId),
            [RankFieldKey] = nameof(FormModel.RankId),
            [SignOnDateFieldKey] = nameof(FormModel.SignOnDate),
            [PlannedSignOffDateFieldKey] = nameof(FormModel.PlannedSignOffDate),
        };

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private FormModel model = new();
    private EditContext editContext = null!;
    private ValidationMessageStore messageStore = null!;
    private bool isSubmitting;
    private string? successMessage;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> unmappedErrors = [];

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

    protected override void OnInitialized()
    {
        editContext = new EditContext(model);
        messageStore = new ValidationMessageStore(editContext);
    }

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

    private async Task SubmitAsync()
    {
        messageStore.Clear();
        conflictMessage = null;
        generalError = null;
        unmappedErrors = [];
        successMessage = null;

        if (!editContext.Validate())
        {
            return;
        }

        if (isSubmitting)
        {
            return;
        }

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
                conflictMessage = failure.Errors is not null
                    && failure.Errors.TryGetValue(CrewMemberIdFieldKey, out var messages)
                    && messages.Length > 0
                    ? messages[0]
                    : failure.Message;
            }
            else if (failure.IsNotFound)
            {
                generalError = failure.Message;
            }
            else if (failure.IsValidation)
            {
                unmappedErrors = ApplyServerValidationErrors(failure.Errors ?? EmptyErrors);
                editContext.NotifyValidationStateChanged();
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
        editContext = new EditContext(model);
        messageStore = new ValidationMessageStore(editContext);
        vesselPickerResetToken++;

        await OnRegistered.InvokeAsync();
    }
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> ApplyServerValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var unmapped = new List<string>();

        foreach (var (key, messages) in errors)
        {
            if (ServerFieldMap.TryGetValue(key, out var propertyName))
            {
                var identifier = new FieldIdentifier(model, propertyName);
                foreach (var message in messages)
                {
                    messageStore.Add(identifier, message);
                }
            }
            else
            {
                unmapped.AddRange(messages);
            }
        }

        return unmapped;
    }

    private sealed class FormModel : IValidatableObject
    {
        [Required(ErrorMessage = "Vessel is required.")]
        public Guid? VesselId { get; set; }

        [Required(ErrorMessage = "Rank is required.")]
        public Guid? RankId { get; set; }

        [Required(ErrorMessage = "Sign-on date is required.")]
        public DateOnly? SignOnDate { get; set; }

        [Required(ErrorMessage = "Planned sign-off date is required.")]
        public DateOnly? PlannedSignOffDate { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (SignOnDate is { } signOn && PlannedSignOffDate is { } plannedOff && plannedOff <= signOn)
            {
                yield return new ValidationResult(
                    "Planned sign-off date must be later than the sign-on date.",
                    [nameof(PlannedSignOffDate)]);
            }
        }
    }
}
