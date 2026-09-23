using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoContainerControlPanel : IDisposable
{
    private const string ReleaseAction = "release";
    private const string HoldAction = "hold";
    private const string BreakSealAction = "break-seal";
    private const string IntactSealStatus = "Intact";
    private const string PassedInspectionResult = "Passed";
    private const string FailedInspectionResult = "Failed";

    private static readonly IReadOnlyDictionary<string, string> SealFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sealNumber"] = nameof(SealFormModel.SealNumber),
        };

    private static readonly IReadOnlyDictionary<string, string> InspectionFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["result"] = nameof(InspectionFormModel.Result),
            ["conditionNotes"] = nameof(InspectionFormModel.ConditionNotes),
            ["temperatureCelsius"] = nameof(InspectionFormModel.TemperatureCelsius),
        };

    private readonly CancellationTokenSource disposeCancellation = new();

    private ContainerControlView? view;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedContainerId;

    private SealFormModel sealModel = new();
    private EditContext sealEditContext = null!;
    private ValidationMessageStore sealMessages = null!;
    private bool isSealing;
    private string? sealError;

    private InspectionFormModel inspectionModel = new();
    private EditContext inspectionEditContext = null!;
    private ValidationMessageStore inspectionMessages = null!;
    private bool isInspecting;
    private string? inspectionError;

    private bool isReplacingSeal;
    private bool isHistoryOpen;

    private string? pendingAction;
    private bool isActing;
    private string? actionError;
    private string? actionNotes;
    private string? notesError;

    [Parameter]
    public Guid ContainerId { get; set; }

    [Parameter]
    public string ContainerNumber { get; set; } = string.Empty;

    [Parameter]
    public bool HideHeading { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    private ContainerControlRow? Control => view?.Control;

    private IReadOnlyList<ContainerControlEventRow> History => view?.History ?? [];

    private bool CanManage => AuthorizationState.CanManageCargo;

    private bool IsHeld => Control?.State == CargoBookingFormat.HeldControlState;

    private ControlStepState SealStep =>
        Control?.SealStatus == IntactSealStatus
            ? ControlStepState.Done
            : ControlStepState.Active;

    private ControlStepState InspectionStep => SealStep is not ControlStepState.Done
        ? ControlStepState.Locked
        : Control?.LatestInspectionResult switch
        {
            PassedInspectionResult => ControlStepState.Done,
            FailedInspectionResult => ControlStepState.Attention,
            _ => ControlStepState.Active,
        };

    private ControlStepState ReleaseStep => Control switch
    {
        null => ControlStepState.Locked,
        { State: CargoBookingFormat.ReleasedControlState } => ControlStepState.Done,
        { State: CargoBookingFormat.HeldControlState } => ControlStepState.Attention,
        { CanRelease: true } => ControlStepState.Active,
        _ => ControlStepState.Locked,
    };

    private int CompletedStepCount =>
        new[] { SealStep, InspectionStep, ReleaseStep }.Count(state => state is ControlStepState.Done);

    private bool ShowSealForm =>
        CanManage && (SealStep is not ControlStepState.Done || isReplacingSeal);

    private bool ReasonRequired => pendingAction == HoldAction;

    private string ConfirmationQuestion => pendingAction switch
    {
        ReleaseAction => "Release this container so cargo operations can proceed?",
        HoldAction => "Put this container on hold and block it from cargo operations?",
        BreakSealAction =>
            "Record this seal as broken? The container will need a new seal before it can be released.",
        _ => string.Empty,
    };

    private string ConfirmLabel => pendingAction switch
    {
        ReleaseAction => "Yes, release",
        HoldAction => "Yes, hold",
        BreakSealAction => "Yes, break the seal",
        _ => string.Empty,
    };

    private string NotesLabel => pendingAction switch
    {
        HoldAction => "Hold reason",
        BreakSealAction => "Seal break notes (optional)",
        _ => "Release notes (optional)",
    };

    public void Dispose()
    {
        DetachValidation();

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override void OnInitialized()
    {
        ResetSealForm();
        ResetInspectionForm();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (loadedContainerId == ContainerId && !isLoading)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        loadedContainerId = ContainerId;

        try
        {
            var result = await CargoApi.GetContainerControlAsync(
                ContainerId,
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                view = result.Value;
            }
            else
            {
                view = null;
                failure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SubmitSealAsync()
    {
        if (isSealing)
        {
            return;
        }

        sealMessages.Clear();
        sealError = null;

        if (!sealEditContext.Validate())
        {
            return;
        }

        isSealing = true;

        try
        {
            var result = await CargoApi.AssignContainerSealAsync(
                ContainerId,
                new AssignContainerSealWebRequest(sealModel.SealNumber!.Trim()),
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    sealEditContext,
                    sealMessages,
                    SealFieldMap,
                    message => sealError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            ResetSealForm();
            isReplacingSeal = false;

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSealing = false;
        }
    }

    private async Task SubmitInspectionAsync()
    {
        if (isInspecting)
        {
            return;
        }

        inspectionMessages.Clear();
        inspectionError = null;

        if (!inspectionEditContext.Validate())
        {
            return;
        }

        isInspecting = true;

        try
        {
            var request = new RecordContainerInspectionWebRequest(
                inspectionModel.Result!,
                string.IsNullOrWhiteSpace(inspectionModel.ConditionNotes)
                    ? null
                    : inspectionModel.ConditionNotes.Trim(),
                Control?.IsReefer is true ? inspectionModel.TemperatureCelsius : null);

            var result = await CargoApi.RecordContainerInspectionAsync(
                ContainerId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    inspectionEditContext,
                    inspectionMessages,
                    InspectionFieldMap,
                    message => inspectionError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            ResetInspectionForm();

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isInspecting = false;
        }
    }

    private static string TimelineActionModifier(ContainerControlEventRow entry)
    {
        return entry.Action switch
        {
            "SealAssigned" => "control-timeline-action-info",
            "SealBroken" => "control-timeline-action-danger",
            "InspectionRecorded" => entry.InspectionResult switch
            {
                PassedInspectionResult => "control-timeline-action-success",
                FailedInspectionResult => "control-timeline-action-danger",
                _ => "control-timeline-action-neutral",
            },
            "Released" => "control-timeline-action-success",
            "Held" => "control-timeline-action-danger",
            "HoldLifted" => "control-timeline-action-success",
            _ => "control-timeline-action-neutral",
        };
    }

    private static string StepClass(ControlStepState state)
    {
        return state switch
        {
            ControlStepState.Done => "control-step control-step-done",
            ControlStepState.Active => "control-step control-step-active",
            ControlStepState.Attention => "control-step control-step-attention",
            _ => "control-step control-step-locked",
        };
    }

    private void StartSealReplacement()
    {
        ResetSealForm();
        sealError = null;
        isReplacingSeal = true;
    }

    private void CancelSealReplacement()
    {
        if (isSealing)
        {
            return;
        }

        ResetSealForm();
        sealError = null;
        isReplacingSeal = false;
    }

    private void ShowHistory()
    {
        isHistoryOpen = true;
    }

    private void HideHistory()
    {
        isHistoryOpen = false;
    }

    private void AskToConfirm(string action)
    {
        pendingAction = action;
        actionError = null;
        notesError = null;
        actionNotes = null;
    }

    private void DismissConfirmation()
    {
        if (isActing)
        {
            return;
        }

        pendingAction = null;
        actionError = null;
        notesError = null;
        actionNotes = null;
    }

    private async Task ConfirmPendingActionAsync()
    {
        if (isActing || pendingAction is not { } action)
        {
            return;
        }

        var notes = string.IsNullOrWhiteSpace(actionNotes) ? null : actionNotes.Trim();

        if (ReasonRequired && notes is null)
        {
            notesError = "A hold reason is required.";

            return;
        }

        isActing = true;
        actionError = null;
        notesError = null;

        try
        {
            var result = action switch
            {
                ReleaseAction => await CargoApi.ReleaseContainerAsync(
                    ContainerId,
                    new ReleaseContainerWebRequest(notes),
                    disposeCancellation.Token),
                BreakSealAction => await CargoApi.BreakContainerSealAsync(
                    ContainerId,
                    new BreakContainerSealWebRequest(notes),
                    disposeCancellation.Token),
                _ => await CargoApi.HoldContainerAsync(
                    ContainerId,
                    new HoldContainerWebRequest(notes!),
                    disposeCancellation.Token),
            };

            if (!result.IsSuccess)
            {
                var actionFailure = result.Failure!;

                if (actionFailure.Errors.TryGetValue("reason", out var reasonMessages))
                {
                    notesError = string.Join(" ", reasonMessages);
                }
                else
                {
                    actionError = $"{actionFailure.Title} {actionFailure.Message}".Trim();
                }

                if (actionFailure.IsConflict || actionFailure.IsNotFound)
                {
                    await LoadAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            pendingAction = null;
            actionNotes = null;

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isActing = false;
        }
    }

    private void DetachValidation()
    {
        if (sealEditContext is not null)
        {
            sealEditContext.OnValidationRequested -= HandleSealValidationRequested;
        }

        if (inspectionEditContext is not null)
        {
            inspectionEditContext.OnValidationRequested -= HandleInspectionValidationRequested;
        }
    }

    private void ResetSealForm()
    {
        if (sealEditContext is not null)
        {
            sealEditContext.OnValidationRequested -= HandleSealValidationRequested;
        }

        sealModel = new SealFormModel();
        sealEditContext = new EditContext(sealModel);
        sealMessages = new ValidationMessageStore(sealEditContext);
        sealEditContext.OnValidationRequested += HandleSealValidationRequested;
    }

    private void ResetInspectionForm()
    {
        if (inspectionEditContext is not null)
        {
            inspectionEditContext.OnValidationRequested -= HandleInspectionValidationRequested;
        }

        inspectionModel = new InspectionFormModel();
        inspectionEditContext = new EditContext(inspectionModel);
        inspectionMessages = new ValidationMessageStore(inspectionEditContext);
        inspectionEditContext.OnValidationRequested += HandleInspectionValidationRequested;
    }

    private void HandleSealValidationRequested(object? sender, ValidationRequestedEventArgs args)
    {
        sealMessages.Clear();
    }

    private void HandleInspectionValidationRequested(
        object? sender,
        ValidationRequestedEventArgs args)
    {
        inspectionMessages.Clear();
    }

    private enum ControlStepState
    {
        Locked,
        Active,
        Attention,
        Done,
    }

    private sealed class SealFormModel
    {
        [Required(ErrorMessage = "A seal number is required.")]
        [StringLength(30, ErrorMessage = "Seal number must not exceed 30 characters.")]
        public string? SealNumber { get; set; }
    }

    private sealed class InspectionFormModel
    {
        [Required(ErrorMessage = "An inspection result is required.")]
        public string? Result { get; set; }

        [StringLength(1000, ErrorMessage = "Condition notes must not exceed 1000 characters.")]
        public string? ConditionNotes { get; set; }

        [Range(-60, 60, ErrorMessage = "Temperature must be between -60 and 60 degrees Celsius.")]
        public decimal? TemperatureCelsius { get; set; }
    }
}
