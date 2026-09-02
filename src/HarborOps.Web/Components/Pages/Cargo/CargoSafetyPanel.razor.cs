using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoSafetyPanel : IDisposable
{
    private const string SubmitAction = "submit";
    private const string ApproveAction = "approve";
    private const string RejectAction = "reject";

    private static readonly IReadOnlyDictionary<string, string> SafetyFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["imdgClass"] = nameof(SafetyFormModel.ImdgClass),
            ["unNumber"] = nameof(SafetyFormModel.UnNumber),
            ["storageConditions"] = nameof(SafetyFormModel.StorageConditions),
            ["handlingNotes"] = nameof(SafetyFormModel.HandlingNotes),
        };

    private readonly CancellationTokenSource disposeCancellation = new();

    private CargoSafetyInformationView? safety;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedBookingId;

    private bool isDeclaringSafety;
    private string? safetyError;
    private SafetyFormModel safetyModel = new();
    private EditContext safetyEditContext = null!;
    private ValidationMessageStore safetyMessages = null!;

    private Guid seededDeclarationId;
    private string? seededReviewStatus;

    private bool isEditing;
    private bool isHistoryOpen;

    private string? pendingAction;
    private bool isActing;
    private string? actionError;
    private string? actionNotes;
    private string? notesError;

    [Parameter]
    public Guid BookingId { get; set; }

    [Parameter]
    public bool IsHazardous { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    private CargoSafetyInformation? Declaration => safety?.Declaration;

    private IReadOnlyList<CargoSafetyReviewEntryRow> Timeline => safety?.Timeline ?? [];

    private bool CanEdit =>
        AuthorizationState.CanManageCargo && IsEditableStatus(Declaration?.ReviewStatus);

    private bool ShowsForm => CanEdit && (Declaration is null || isEditing);

    private bool CanStartEditing => CanEdit && Declaration is not null && !isEditing;

    private bool IsLocked =>
        AuthorizationState.CanManageCargo && Declaration is not null && !CanEdit;

    private string SubmitLabel => (isDeclaringSafety, Declaration) switch
    {
        (true, null) => "Declaring...",
        (true, _) => "Saving...",
        (false, null) => "Declare safety information",
        _ => "Save changes",
    };

    private static bool IsEditableStatus(string? reviewStatus)
    {
        return reviewStatus is null
            or CargoBookingFormat.DraftStatus
            or CargoBookingFormat.RejectedStatus;
    }

    private static string LockedReason(string? reviewStatus)
    {
        return reviewStatus == CargoBookingFormat.SubmittedStatus
            ? "This declaration is awaiting review and cannot be edited."
            : "This declaration is approved and can no longer be edited.";
    }

    private bool CanSubmit =>
        AuthorizationState.CanManageCargo
        && Declaration is { } declaration
        && IsEditableStatus(declaration.ReviewStatus);

    private bool CanReview =>
        AuthorizationState.CanManageCargo
        && Declaration?.ReviewStatus == CargoBookingFormat.SubmittedStatus;

    private bool ShowsNotes => pendingAction is ApproveAction or RejectAction;

    private bool NotesRequired => pendingAction == RejectAction;

    private string ConfirmationQuestion => pendingAction switch
    {
        SubmitAction => "Send this declaration for safety review?",
        ApproveAction => "Approve this declaration and allow the cargo to be loaded?",
        RejectAction => "Reject this declaration and send it back for correction?",
        _ => string.Empty,
    };

    private string ConfirmLabel => pendingAction switch
    {
        SubmitAction => "Yes, submit",
        ApproveAction => "Yes, approve",
        _ => "Yes, reject",
    };

    public void Dispose()
    {
        if (safetyEditContext is not null)
        {
            safetyEditContext.OnValidationRequested -= HandleSafetyValidationRequested;
        }

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override void OnInitialized()
    {
        ResetSafetyForm();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!IsHazardous)
        {
            safety = null;
            failure = null;
            isLoading = false;

            loadedBookingId = BookingId;

            return;
        }

        if (loadedBookingId == BookingId && !isLoading)
        {
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        loadedBookingId = BookingId;

        try
        {
            var result = await CargoApi.GetSafetyInformationAsync(
                BookingId,
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                safety = result.Value;
            }
            else
            {
                safety = null;

                if (!result.Failure!.IsNotFound)
                {
                    failure = result.Failure;
                }
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;

            SeedFormFromDeclaration();
        }
    }

    private async Task SubmitSafetyDeclarationAsync()
    {
        if (isDeclaringSafety)
        {
            return;
        }

        safetyMessages.Clear();
        safetyError = null;

        if (!safetyEditContext.Validate())
        {
            return;
        }

        isDeclaringSafety = true;

        var wasDeclared = Declaration is not null;

        try
        {
            var request = new UpdateCargoSafetyInfoWebRequest(
                safetyModel.ImdgClass!.Trim(),
                safetyModel.UnNumber!.Trim(),
                safetyModel.StorageConditions!.Trim(),
                string.IsNullOrWhiteSpace(safetyModel.HandlingNotes) ? null : safetyModel.HandlingNotes.Trim());

            var result = await CargoApi.SaveSafetyInformationAsync(
                BookingId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    safetyEditContext,
                    safetyMessages,
                    SafetyFieldMap,
                    message => safetyError = message);

                if (result.Failure!.IsConflict)
                {
                    await ReloadAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            isEditing = false;

            await ReloadAsync();

            if (!wasDeclared)
            {
                await OnChanged.InvokeAsync();
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isDeclaringSafety = false;
        }
    }

    private Task ReloadAsync()
    {
        seededDeclarationId = Guid.Empty;
        seededReviewStatus = null;

        return LoadAsync();
    }

    private void ShowHistory()
    {
        isHistoryOpen = true;
    }

    private void HideHistory()
    {
        isHistoryOpen = false;
    }

    private void StartEditing()
    {
        safetyError = null;
        isEditing = true;

        SeedFormFromDeclaration(force: true);
    }

    private void CancelEditing()
    {
        if (isDeclaringSafety)
        {
            return;
        }

        safetyError = null;
        isEditing = false;

        SeedFormFromDeclaration(force: true);
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

        if (NotesRequired && notes is null)
        {
            notesError = "Review notes are required when a declaration is rejected.";

            return;
        }

        isActing = true;
        actionError = null;
        notesError = null;

        try
        {
            var result = action == SubmitAction
                ? await CargoApi.SubmitSafetyInformationAsync(
                    BookingId,
                    disposeCancellation.Token)
                : await CargoApi.ReviewSafetyInformationAsync(
                    BookingId,
                    new ReviewCargoSafetyInfoWebRequest(
                        action == ApproveAction
                            ? CargoBookingFormat.ApprovedStatus
                            : CargoBookingFormat.RejectedStatus,
                        notes),
                    disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                var actionFailure = result.Failure!;

                if (actionFailure.Errors.TryGetValue("notes", out var notesMessages))
                {
                    notesError = string.Join(" ", notesMessages);
                }
                else
                {
                    actionError = $"{actionFailure.Title} {actionFailure.Message}".Trim();
                }

                if (actionFailure.IsConflict)
                {
                    await ReloadAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            pendingAction = null;
            actionNotes = null;

            await ReloadAsync();
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

    private void SeedFormFromDeclaration(bool force = false)
    {
        if (Declaration is not { } declaration)
        {
            if (seededDeclarationId != Guid.Empty)
            {
                seededDeclarationId = Guid.Empty;
                seededReviewStatus = null;

                ResetSafetyForm();
            }

            return;
        }

        if (!force
            && seededDeclarationId == declaration.CargoSafetyInfoId
            && seededReviewStatus == declaration.ReviewStatus)
        {
            return;
        }

        seededDeclarationId = declaration.CargoSafetyInfoId;
        seededReviewStatus = declaration.ReviewStatus;

        ResetSafetyForm();

        safetyModel.ImdgClass = declaration.ImdgClass;
        safetyModel.UnNumber = declaration.UnNumber;
        safetyModel.StorageConditions = declaration.StorageConditions;
        safetyModel.HandlingNotes = declaration.HandlingNotes;
    }
    private void ResetSafetyForm()
    {
        if (safetyEditContext is not null)
        {
            safetyEditContext.OnValidationRequested -= HandleSafetyValidationRequested;
        }

        safetyModel = new SafetyFormModel();
        safetyEditContext = new EditContext(safetyModel);
        safetyMessages = new ValidationMessageStore(safetyEditContext);
        safetyEditContext.OnValidationRequested += HandleSafetyValidationRequested;
    }

    private void HandleSafetyValidationRequested(object? sender, ValidationRequestedEventArgs args)
    {
        safetyMessages.Clear();
    }

    private sealed class SafetyFormModel
    {
        [Required(ErrorMessage = "IMDG class is required.")]
        [StringLength(10, ErrorMessage = "IMDG class must not exceed 10 characters.")]
        public string? ImdgClass { get; set; }

        [Required(ErrorMessage = "UN number is required.")]
        [StringLength(10, ErrorMessage = "UN number must not exceed 10 characters.")]
        public string? UnNumber { get; set; }

        [Required(ErrorMessage = "Storage conditions are required.")]
        [StringLength(500, ErrorMessage = "Storage conditions must not exceed 500 characters.")]
        public string? StorageConditions { get; set; }

        [StringLength(1000, ErrorMessage = "Handling notes must not exceed 1000 characters.")]
        public string? HandlingNotes { get; set; }
    }
}
