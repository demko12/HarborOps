using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class DocumentFilePanel
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private bool isLoadingFileDetails;
    private DocumentDetailsDto? fileDetails;
    private DocumentsApiFailure? fileDetailsFailure;

    private bool isConfirmingReplace;
    private bool isReplaceFormOpen;
    private IBrowserFile? selectedUploadFile;
    private bool isUploading;
    private double uploadProgress;
    private DocumentsApiFailure? uploadFailure;
    private string? uploadSuccessMessage;
    private bool isEditingMetadata;
    private bool isRenewingDocument;
    private int? lifecycleTypeId;
    private string? lifecycleNumber;
    private DateOnly? lifecycleIssueDate;
    private DateOnly? lifecycleExpiryDate;
    private string? lifecycleAuthority;
    private IReadOnlyList<DocumentRenewalHistoryItemDto> renewalHistory = [];
    private DocumentsApiFailure? lifecycleFailure;

    // Review workflow: submit-for-review and approve act directly (no dialog of their
    // own), so they share lifecycleFailure/reviewActionSuccessMessage with metadata
    // correction/renewal above - all four are simple button actions in the same panel
    // area, same reasoning as lifecycleFailure already being shared between two actions.
    private bool isSubmittingForReview;
    private bool isApproving;
    private string? reviewActionSuccessMessage;

    // Reject: own confirmation dialog + required notes textarea, 1:1 structure with
    // isConfirmingReplace/OpenReplaceConfirmation/ConfirmReplace/CancelReplaceConfirmation.
    private bool isRejectingDocument;
    private string? rejectNotes;
    private string? rejectNotesValidationMessage;
    private bool isSubmittingRejection;
    private DocumentsApiFailure? rejectFailure;

    // Revoke: same 1:1 confirmation-dialog structure as Reject, own required reason textarea.
    private bool isConfirmingRevoke;
    private string? revokeReason;
    private string? revokeReasonValidationMessage;
    private bool isRevoking;
    private DocumentsApiFailure? revokeFailure;

    private bool isDistributionFormOpen;
    private bool isIssuingDistribution;
    private DateOnly? distributionDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
    private string? distributionInstruction;
    private string? distributionRecipientSearch;
    private IReadOnlyList<DocumentDistributionRecipientPickerDto> distributionRecipients = [];
    private HashSet<Guid> selectedDistributionRecipients = [];
    private DocumentsApiFailure? distributionFailure;
    private string? distributionSuccessMessage;

    // Review history: a nested expandable section within the file panel (distinct from
    // the always-visible, eagerly-loaded renewalHistory above), lazy-loaded only on first
    // expand rather than on every panel open.
    private bool isReviewHistoryExpanded;
    private bool isLoadingReviewHistory;
    private IReadOnlyList<DocumentReviewEventResponseDto> reviewHistory = [];
    private DocumentsApiFailure? reviewHistoryFailure;

    [Parameter, EditorRequired]
    public Guid DocumentId { get; set; }

    [Parameter]
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; set; } = [];

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public EventCallback OnDocumentChanged { get; set; }

    private bool IsUploadValidationFailure => uploadFailure is { IsValidation: true };

    private bool ShowUploadForm =>
        fileDetails is not null
        && ((!fileDetails.HasFile && AuthorizationState.HasPermission("documents.manage"))
            || (fileDetails.HasFile && isReplaceFormOpen));

    private IEnumerable<string> UploadValidationMessages =>
        uploadFailure?.Errors.SelectMany(entry => entry.Value) ?? [];

    private bool CanSubmitForReview =>
        fileDetails is { HasFile: true, ReviewStatus: Documents.DraftReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    private bool CanApproveOrReject =>
        fileDetails is { ReviewStatus: Documents.PendingReviewReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    private bool CanRevoke =>
        fileDetails is { Status: "Active", ReviewStatus: Documents.ApprovedReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    protected override Task OnInitializedAsync() => LoadFileDetailsAsync(DocumentId);

    private async Task CloseFilePanel()
    {
        if (isUploading)
        {
            return;
        }

        await OnClose.InvokeAsync();
    }

    private async Task LoadFileDetailsAsync(Guid documentId)
    {
        isLoadingFileDetails = true;
        fileDetailsFailure = null;
        fileDetails = null;

        var result = await DocumentsApi.GetDocumentByIdAsync(documentId, disposeCancellation.Token);

        isLoadingFileDetails = false;

        if (!result.IsSuccess)
        {
            fileDetailsFailure = result.Failure;

            return;
        }

        fileDetails = result.Value;
        lifecycleTypeId = fileDetails?.DocumentTypeId;
        lifecycleNumber = fileDetails?.Number;
        lifecycleIssueDate = fileDetails?.IssueDate;
        lifecycleExpiryDate = fileDetails?.ExpiryDate;
        lifecycleAuthority = fileDetails?.IssuingAuthority;
        if (fileDetails is not null)
        {
            var history = await DocumentsApi.GetRenewalHistoryAsync(documentId, disposeCancellation.Token);
            if (history.IsSuccess && history.Value is not null) renewalHistory = history.Value;
        }
    }

    private async Task OpenDistributionFormAsync()
    {
        isDistributionFormOpen = !isDistributionFormOpen;
        distributionFailure = null;
        distributionSuccessMessage = null;
        if (isDistributionFormOpen) await SearchDistributionRecipientsAsync();
    }

    private async Task SearchDistributionRecipientsAsync()
    {
        var result = await DocumentsApi.SearchDistributionRecipientsAsync(distributionRecipientSearch, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null) distributionRecipients = result.Value;
        else distributionFailure = result.Failure;
    }

    private async Task IssueDistributionAsync()
    {
        var documentId = DocumentId;
        isIssuingDistribution = true;
        distributionFailure = null;
        var result = await DocumentsApi.IssueDistributionAsync(documentId, new IssueDocumentDistributionRequest(distributionDueDate, distributionInstruction, selectedDistributionRecipients.ToArray()), disposeCancellation.Token);
        isIssuingDistribution = false;
        if (!result.IsSuccess) { distributionFailure = result.Failure; return; }
        distributionSuccessMessage = $"Issued to {selectedDistributionRecipients.Count} recipient(s).";
        selectedDistributionRecipients = [];
    }

    private async Task SaveMetadataAsync()
    {
        var id = DocumentId;
        lifecycleFailure = null;
        var result = await DocumentsApi.UpdateMetadataAsync(id, LifecycleRequest(), disposeCancellation.Token);
        if (!result.IsSuccess) { lifecycleFailure = result.Failure; return; }
        isEditingMetadata = false;
        await LoadFileDetailsAsync(id);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task RenewDocumentAsync()
    {
        var id = DocumentId;
        lifecycleFailure = null;
        var result = await DocumentsApi.RenewAsync(id, LifecycleRequest(), disposeCancellation.Token);
        if (!result.IsSuccess) { lifecycleFailure = result.Failure; return; }
        isRenewingDocument = false;
        await LoadFileDetailsAsync(id);
        await OnDocumentChanged.InvokeAsync();
    }

    private DocumentMetadataRequest LifecycleRequest() => new(lifecycleTypeId, lifecycleNumber, lifecycleIssueDate, lifecycleExpiryDate, lifecycleAuthority);

    private async Task SubmitForReviewAsync()
    {
        var id = DocumentId;

        if (isSubmittingForReview)
        {
            return;
        }

        isSubmittingForReview = true;
        lifecycleFailure = null;
        reviewActionSuccessMessage = null;

        var result = await DocumentsApi.SubmitForReviewAsync(id, disposeCancellation.Token);

        isSubmittingForReview = false;

        if (!result.IsSuccess)
        {
            lifecycleFailure = result.Failure;
            await RefreshListIfConcurrencyConflictAsync(result.Failure);

            return;
        }

        await HandleReviewActionSuccessAsync(id, "Document submitted for review.");
    }

    private async Task ApproveAsync()
    {
        var id = DocumentId;

        if (isApproving)
        {
            return;
        }

        isApproving = true;
        lifecycleFailure = null;
        reviewActionSuccessMessage = null;

        var result = await DocumentsApi.ReviewAsync(id, new ReviewDocumentRequest(Documents.ApprovedReviewStatus, null), disposeCancellation.Token);

        isApproving = false;

        if (!result.IsSuccess)
        {
            lifecycleFailure = result.Failure;
            await RefreshListIfConcurrencyConflictAsync(result.Failure);

            return;
        }

        await HandleReviewActionSuccessAsync(id, "Document approved.");
    }

    private void OpenRejectConfirmation()
    {
        isRejectingDocument = true;
        rejectNotes = null;
        rejectNotesValidationMessage = null;
        rejectFailure = null;
    }

    private void CancelRejectConfirmation()
    {
        isRejectingDocument = false;
        rejectNotes = null;
        rejectNotesValidationMessage = null;
        rejectFailure = null;
    }

    private async Task ConfirmRejectAsync()
    {
        var id = DocumentId;

        if (isSubmittingRejection)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rejectNotes))
        {
            rejectNotesValidationMessage = "Notes are required when rejecting a document.";

            return;
        }

        rejectNotesValidationMessage = null;
        isSubmittingRejection = true;
        rejectFailure = null;
        reviewActionSuccessMessage = null;

        var result = await DocumentsApi.ReviewAsync(id, new ReviewDocumentRequest(Documents.RejectedReviewStatus, rejectNotes), disposeCancellation.Token);

        isSubmittingRejection = false;

        if (!result.IsSuccess)
        {
            rejectFailure = result.Failure;
            await RefreshListIfConcurrencyConflictAsync(result.Failure);

            return;
        }

        isRejectingDocument = false;
        rejectNotes = null;

        await HandleReviewActionSuccessAsync(id, "Document rejected.");
    }

    private void OpenRevokeConfirmation()
    {
        isConfirmingRevoke = true;
        revokeReason = null;
        revokeReasonValidationMessage = null;
        revokeFailure = null;
    }

    private void CancelRevokeConfirmation()
    {
        isConfirmingRevoke = false;
        revokeReason = null;
        revokeReasonValidationMessage = null;
        revokeFailure = null;
    }

    private async Task ConfirmRevokeAsync()
    {
        var id = DocumentId;

        if (isRevoking)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(revokeReason))
        {
            revokeReasonValidationMessage = "A reason is required to revoke a document.";

            return;
        }

        revokeReasonValidationMessage = null;
        isRevoking = true;
        revokeFailure = null;
        reviewActionSuccessMessage = null;

        var result = await DocumentsApi.RevokeAsync(id, new RevokeDocumentRequest(revokeReason), disposeCancellation.Token);

        isRevoking = false;

        if (!result.IsSuccess)
        {
            revokeFailure = result.Failure;
            await RefreshListIfConcurrencyConflictAsync(result.Failure);

            return;
        }

        isConfirmingRevoke = false;
        revokeReason = null;

        await HandleReviewActionSuccessAsync(id, "Document revoked.");
    }

    // Shared by all four review-workflow actions on success: refresh the panel's own
    // details, refresh the already-expanded review-history timeline (if open) so it
    // includes the event this action just created, then refresh the main grid - same
    // three-step refresh SaveMetadataAsync/RenewDocumentAsync already perform for the
    // first and third steps.
    private async Task HandleReviewActionSuccessAsync(Guid documentId, string message)
    {
        reviewActionSuccessMessage = message;

        await LoadFileDetailsAsync(documentId);

        if (isReviewHistoryExpanded)
        {
            await LoadReviewHistoryAsync(documentId);
        }

        await OnDocumentChanged.InvokeAsync();
    }

    // A 409 caused by DbUpdateConcurrencyException on the server carries this exact title
    // (see DocumentsController.Review/Revoke) - refreshing the grid on this specific
    // conflict surfaces the document's current real state, unlike other 409s (self-review,
    // wrong lifecycle state) which are user-actionable as-is and do not need a refresh.
    private static bool IsConcurrencyConflict(DocumentsApiFailure failure) =>
        string.Equals(failure.Title, "Document was modified concurrently.", StringComparison.Ordinal);

    private Task RefreshListIfConcurrencyConflictAsync(DocumentsApiFailure? failure) =>
        failure is not null && IsConcurrencyConflict(failure) ? OnDocumentChanged.InvokeAsync() : Task.CompletedTask;

    private async Task ToggleReviewHistoryAsync()
    {
        isReviewHistoryExpanded = !isReviewHistoryExpanded;

        if (isReviewHistoryExpanded && reviewHistory.Count == 0 && reviewHistoryFailure is null)
        {
            await LoadReviewHistoryAsync(DocumentId);
        }
    }

    private async Task LoadReviewHistoryAsync(Guid documentId)
    {
        isLoadingReviewHistory = true;
        reviewHistoryFailure = null;

        var result = await DocumentsApi.GetReviewHistoryAsync(documentId, disposeCancellation.Token);

        isLoadingReviewHistory = false;

        if (!result.IsSuccess)
        {
            reviewHistoryFailure = result.Failure;

            return;
        }

        reviewHistory = result.Value ?? [];
    }

    private void OpenReplaceConfirmation()
    {
        isConfirmingReplace = true;
    }

    private void ConfirmReplace()
    {
        isConfirmingReplace = false;
        isReplaceFormOpen = true;
        selectedUploadFile = null;
        uploadFailure = null;
        uploadSuccessMessage = null;
    }

    private void CancelReplaceConfirmation()
    {
        isConfirmingReplace = false;
    }

    private void OnFileSelected(InputFileChangeEventArgs e)
    {
        selectedUploadFile = e.File;
        uploadFailure = null;
        uploadSuccessMessage = null;
    }

    private async Task SubmitFileAsync(Guid documentId, bool isReplace)
    {
        if (selectedUploadFile is null || isUploading)
        {
            return;
        }

        // IBrowserFile.OpenReadStream enforces maxAllowedSize by throwing IOException
        // synchronously as soon as it's called, which DocumentsApiClient's
        // Upload/ReplaceDocumentFileAsync do not catch (Api/Documents/* is frozen for this
        // step). Reject an oversized file here first so that path is never reached.
        if (selectedUploadFile.Size > Documents.MaxUploadSizeBytesForDisplay)
        {
            uploadFailure = new DocumentsApiFailure(
                StatusCodes.Status413PayloadTooLarge,
                "File is too large.",
                "File is too large.",
                Documents.EmptyFileErrors);

            return;
        }

        isUploading = true;
        uploadProgress = 0;
        uploadFailure = null;
        uploadSuccessMessage = null;

        var progress = new Progress<double>(p =>
        {
            uploadProgress = p;
            InvokeAsync(StateHasChanged);
        });

        try
        {
            var result = isReplace
                ? await DocumentsApi.ReplaceDocumentFileAsync(documentId, selectedUploadFile, progress, CancellationToken.None)
                : await DocumentsApi.UploadDocumentFileAsync(documentId, selectedUploadFile, progress, CancellationToken.None);

            if (!result.IsSuccess)
            {
                uploadFailure = result.Failure;

                return;
            }

            uploadSuccessMessage = isReplace ? "File replaced." : "File uploaded.";
            selectedUploadFile = null;
            isReplaceFormOpen = false;
            isConfirmingReplace = false;

            await LoadFileDetailsAsync(documentId);
        }
        finally
        {
            isUploading = false;
        }
    }

    private string? UploadFailureMessageText(bool isReplace)
    {
        if (uploadFailure is null || uploadFailure.IsValidation)
        {
            return null;
        }

        if (uploadFailure.IsPayloadTooLarge)
        {
            return $"File is too large. Maximum allowed size is {FormatBytes(Documents.MaxUploadSizeBytesForDisplay)}.";
        }

        if (!isReplace && uploadFailure.IsConflict)
        {
            return "Document already has a file.";
        }

        if (isReplace && uploadFailure.IsNotFound)
        {
            return "No file to replace.";
        }

        return uploadFailure.Message;
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is not { } value || value < 0)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        double size = value;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size:0.#} {units[unitIndex]}";
    }

    private static string FormatUploadedAt(DateTime? uploadedAtUtc) =>
        uploadedAtUtc is { } value ? $"{value:dd MMM yyyy HH:mm} UTC" : "-";

    private static string FormatOccurredAt(DateTimeOffset occurredAtUtc) =>
        $"{occurredAtUtc:dd MMM yyyy HH:mm} UTC";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
