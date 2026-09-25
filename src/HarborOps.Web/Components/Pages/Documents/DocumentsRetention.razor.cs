using System.Globalization;
using HarborOps.Web.Api.Documents;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class DocumentsRetention
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private IReadOnlyList<DocumentTypeListItem> documentTypeOptions = [];
    // ---- Retention, legal hold, and disposition tab ----
    private const string RetentionActiveState = "Active";
    private const string RetentionArchivedState = "Archived";
    private const string RetentionPendingDispositionState = "PendingDisposition";
    private const string RetentionDisposedState = "Disposed";

    private bool isRetentionQueueSubTab = true;

    private bool isLoadingRetentionPolicies;
    private IReadOnlyList<RetentionPolicyRowDto> retentionPolicies = [];
    private DocumentsApiFailure? retentionPoliciesFailure;
    private Dictionary<int, int?> retentionPolicyEdits = [];
    private Dictionary<int, bool> isSavingRetentionPolicy = [];
    private Dictionary<int, string?> retentionPolicySaveError = [];
    private string? retentionPolicySuccessMessage;

    // Unlike the Documents page grid's isLoading (which starts true because LoadAsync runs
    // unconditionally from OnInitializedAsync), the retention queue is loaded through
    // LoadRetentionTabAsync, gated on !isLoadingRetentionQueue - starting this true would make
    // that guard permanently false and the queue would never load.
    private bool isLoadingRetentionQueue;
    private IReadOnlyList<RetentionQueueRowDto> retentionQueueRows = [];
    private DocumentsApiFailure? retentionQueueFailure;
    private string? retentionStateFilter;
    private DateOnly? retentionDueBeforeFilter;
    private int retentionPageNumber = 1;
    private int retentionPageSize = 25;
    private int retentionTotalCount;

    // Per-document retention actions panel: only one document's panel can be open at a
    // time, same reasoning as expandedDocumentId on the Documents page.
    private Guid? retentionActionsDocumentId;
    private RetentionQueueRowDto? retentionActionsRow;
    private string? retentionActionsSuccessMessage;

    private bool isConfirmingArchive;
    private bool isArchiving;
    private DocumentsApiFailure? archiveFailure;

    private bool isConfirmingRestore;
    private bool isRestoring;
    private DocumentsApiFailure? restoreFailure;

    private string? holdReason;
    private string? holdReference;
    private string? holdReasonValidationMessage;
    private bool isPlacingHold;
    private DocumentsApiFailure? placeHoldFailure;

    private Guid? confirmingReleaseHoldId;
    private string? releaseReason;
    private string? releaseReasonValidationMessage;
    private bool isReleasingHold;
    private DocumentsApiFailure? releaseHoldFailure;

    private string? dispositionReason;
    private string? dispositionReasonValidationMessage;
    private bool isCreatingDisposition;
    private DocumentsApiFailure? createDispositionFailure;

    private bool isConfirmingApproveDisposition;
    private string? approveDecisionReason;
    private bool isApprovingDisposition;
    private DocumentsApiFailure? approveDispositionFailure;

    private bool isConfirmingRejectDisposition;
    private string? rejectDecisionReason;
    private string? rejectDecisionReasonValidationMessage;
    private bool isRejectingDisposition;
    private DocumentsApiFailure? rejectDispositionFailure;

    private bool CanManageRetention => AuthorizationState.HasPermission("documents.manage");

    private bool CanDisposeRetention => AuthorizationState.HasPermission("documents.dispose");

    private bool CanArchiveRetention => retentionActionsRow is { RetentionState: RetentionActiveState } && CanManageRetention;

    private bool CanRestoreRetention => retentionActionsRow is { RetentionState: RetentionArchivedState } && CanManageRetention;

    private bool CanRequestDisposition =>
        retentionActionsRow is { RetentionState: RetentionActiveState or RetentionArchivedState, PendingDisposition: null }
        && CanManageRetention;

    private IReadOnlyList<RetentionPolicyRow> RetentionPolicyRows =>
        documentTypeOptions
            .Select(type => new RetentionPolicyRow(
                type.DocumentTypeId,
                type.Name,
                retentionPolicies.FirstOrDefault(policy => policy.DocumentTypeId == type.DocumentTypeId)?.RetentionPeriodDays))
            .OrderBy(row => row.DocumentTypeName, StringComparer.Ordinal)
            .ToList();

    private string RetentionDocumentTypeName(int documentTypeId) =>
        documentTypeOptions.FirstOrDefault(type => type.DocumentTypeId == documentTypeId)?.Name
        ?? documentTypeId.ToString(CultureInfo.InvariantCulture);

    private int RetentionTotalPages => retentionTotalCount == 0 ? 1 : ((retentionTotalCount + retentionPageSize - 1) / retentionPageSize);
    protected override Task OnInitializedAsync() =>
        Task.WhenAll(LoadDocumentTypeOptionsAsync(), LoadRetentionTabAsync());

    private async Task LoadDocumentTypeOptionsAsync()
    {
        DocumentsApiResult<DocumentTypesPage> result;

        try
        {
            result = await DocumentsApi.GetDocumentTypesAsync(
                new GetDocumentTypesQuery(null, Documents.DocumentTypeOptionsPageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        documentTypeOptions = result.IsSuccess ? result.Value!.Items : [];
    }

    private async Task LoadRetentionTabAsync()
    {
        // Awaited, not fire-and-forget: this is a synchronous-looking void handler's async
        // counterpart, and Blazor only re-renders automatically when it can track the Task an
        // event handler returns. A detached `_ = LoadXAsync()` call here would run to
        // completion on its own with no render ever reflecting it - the loading spinner
        // would show (set synchronously before this method's first real await) and then
        // never clear, exactly the bug this fixes. See ToggleReviewHistoryAsync in DocumentFilePanel
        // for the same shape already used correctly elsewhere.
        if (isRetentionQueueSubTab && retentionQueueRows.Count == 0 && !isLoadingRetentionQueue)
        {
            await LoadRetentionQueueAsync();
        }

        if (!isRetentionQueueSubTab && retentionPolicies.Count == 0 && !isLoadingRetentionPolicies)
        {
            await LoadRetentionPoliciesAsync();
        }
    }

    private async Task SelectRetentionQueueSubTab()
    {
        isRetentionQueueSubTab = true;

        if (retentionQueueRows.Count == 0 && !isLoadingRetentionQueue)
        {
            await LoadRetentionQueueAsync();
        }
    }

    private async Task SelectRetentionPoliciesSubTab()
    {
        isRetentionQueueSubTab = false;

        if (retentionPolicies.Count == 0 && !isLoadingRetentionPolicies)
        {
            await LoadRetentionPoliciesAsync();
        }
    }

    private async Task LoadRetentionPoliciesAsync()
    {
        isLoadingRetentionPolicies = true;
        retentionPoliciesFailure = null;

        // finally, not a plain assignment after the await: DocumentsApiClient does not catch
        // every exception type (e.g. a malformed response triggers JsonException, which
        // propagates unhandled) - without this, such an exception would leave
        // isLoadingRetentionPolicies stuck true and the spinner would never clear, the same
        // class of bug as the fire-and-forget issue this checkpoint fixed for the queue tab.
        try
        {
            var result = await DocumentsApi.GetRetentionPoliciesAsync(disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                retentionPoliciesFailure = result.Failure;
                retentionPolicies = [];

                return;
            }

            retentionPolicies = result.Value!.Items;
            retentionPolicyEdits = documentTypeOptions.ToDictionary(
                type => type.DocumentTypeId,
                type => retentionPolicies.FirstOrDefault(policy => policy.DocumentTypeId == type.DocumentTypeId)?.RetentionPeriodDays);
        }
        finally
        {
            isLoadingRetentionPolicies = false;
        }
    }

    private async Task SaveRetentionPolicyAsync(int documentTypeId)
    {
        if (isSavingRetentionPolicy.GetValueOrDefault(documentTypeId))
        {
            return;
        }

        if (!retentionPolicyEdits.TryGetValue(documentTypeId, out var value) || value is null or <= 0)
        {
            retentionPolicySaveError[documentTypeId] = "Retention period must be greater than zero days.";

            return;
        }

        retentionPolicySaveError.Remove(documentTypeId);
        isSavingRetentionPolicy[documentTypeId] = true;
        retentionPolicySuccessMessage = null;

        var result = await DocumentsApi.UpdateRetentionPolicyAsync(
            documentTypeId, new UpdateRetentionPolicyRequest(value), disposeCancellation.Token);

        isSavingRetentionPolicy[documentTypeId] = false;

        if (!result.IsSuccess)
        {
            retentionPolicySaveError[documentTypeId] = result.Failure?.Message ?? "Unable to update the retention policy.";

            return;
        }

        retentionPolicySuccessMessage = "Retention policy updated.";

        await LoadRetentionPoliciesAsync();
    }

    private Task ApplyRetentionFiltersAsync()
    {
        retentionPageNumber = 1;

        return LoadRetentionQueueAsync();
    }

    private Task ClearRetentionFiltersAsync()
    {
        retentionStateFilter = null;
        retentionDueBeforeFilter = null;
        retentionPageNumber = 1;

        return LoadRetentionQueueAsync();
    }

    private Task PreviousRetentionPageAsync()
    {
        if (retentionPageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        retentionPageNumber--;

        return LoadRetentionQueueAsync();
    }

    private Task NextRetentionPageAsync()
    {
        if (retentionPageNumber >= RetentionTotalPages)
        {
            return Task.CompletedTask;
        }

        retentionPageNumber++;

        return LoadRetentionQueueAsync();
    }

    private Task RetryRetentionQueueAsync() => LoadRetentionQueueAsync();

    private async Task LoadRetentionQueueAsync()
    {
        isLoadingRetentionQueue = true;
        retentionQueueFailure = null;

        // finally, not a plain assignment after the await: DocumentsApiClient does not catch
        // every exception type (e.g. a malformed response triggers JsonException, which
        // propagates unhandled) - without this, such an exception would leave
        // isLoadingRetentionQueue stuck true and the spinner would never clear.
        try
        {
            var query = new GetRetentionQueueQuery(retentionStateFilter, retentionDueBeforeFilter, retentionPageNumber, retentionPageSize);
            var result = await DocumentsApi.GetRetentionQueueAsync(query, disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                retentionQueueFailure = result.Failure;
                retentionQueueRows = [];
                retentionTotalCount = 0;

                return;
            }

            var page = result.Value!;
            retentionPageNumber = page.PageNumber;
            retentionPageSize = page.PageSize;
            retentionTotalCount = page.TotalCount;
            retentionQueueRows = page.Items;
        }
        finally
        {
            isLoadingRetentionQueue = false;
        }
    }

    private void OpenRetentionActionsPanel(RetentionQueueRowDto row)
    {
        retentionActionsDocumentId = row.DocumentId;
        retentionActionsRow = row;
        ResetRetentionActionsState();
    }

    private void CloseRetentionActionsPanel()
    {
        retentionActionsDocumentId = null;
        retentionActionsRow = null;
        ResetRetentionActionsState();
    }

    private void ResetRetentionActionsState()
    {
        retentionActionsSuccessMessage = null;

        isConfirmingArchive = false;
        isArchiving = false;
        archiveFailure = null;

        isConfirmingRestore = false;
        isRestoring = false;
        restoreFailure = null;

        holdReason = null;
        holdReference = null;
        holdReasonValidationMessage = null;
        isPlacingHold = false;
        placeHoldFailure = null;

        confirmingReleaseHoldId = null;
        releaseReason = null;
        releaseReasonValidationMessage = null;
        isReleasingHold = false;
        releaseHoldFailure = null;

        dispositionReason = null;
        dispositionReasonValidationMessage = null;
        isCreatingDisposition = false;
        createDispositionFailure = null;

        isConfirmingApproveDisposition = false;
        approveDecisionReason = null;
        isApprovingDisposition = false;
        approveDispositionFailure = null;

        isConfirmingRejectDisposition = false;
        rejectDecisionReason = null;
        rejectDecisionReasonValidationMessage = null;
        isRejectingDisposition = false;
        rejectDispositionFailure = null;
    }

    // Shared by every retention/hold/disposition action on success: refresh the queue so
    // the row reflects its new state, then resync the open panel's snapshot from the
    // refreshed list - same "refresh the source list, then reload the panel" shape as
    // HandleReviewActionSuccessAsync in DocumentFilePanel. The document can legitimately drop out of the
    // currently filtered/paged view (e.g. its state changed away from an active State
    // filter); the success message stays visible even when that happens.
    private async Task RefreshAfterRetentionActionAsync(string message)
    {
        retentionActionsSuccessMessage = message;

        await LoadRetentionQueueAsync();

        retentionActionsRow = retentionQueueRows.FirstOrDefault(row => row.DocumentId == retentionActionsDocumentId);
    }

    private void OpenArchiveConfirmation()
    {
        isConfirmingArchive = true;
        archiveFailure = null;
    }

    private void CancelArchiveConfirmation()
    {
        isConfirmingArchive = false;
        archiveFailure = null;
    }

    private async Task ConfirmArchiveAsync()
    {
        if (retentionActionsDocumentId is not { } id || isArchiving)
        {
            return;
        }

        isArchiving = true;
        archiveFailure = null;

        var result = await DocumentsApi.ArchiveAsync(id, new ArchiveDocumentRequest(null), disposeCancellation.Token);

        isArchiving = false;

        if (!result.IsSuccess)
        {
            archiveFailure = result.Failure;

            return;
        }

        isConfirmingArchive = false;

        await RefreshAfterRetentionActionAsync("Document archived.");
    }

    private void OpenRestoreConfirmation()
    {
        isConfirmingRestore = true;
        restoreFailure = null;
    }

    private void CancelRestoreConfirmation()
    {
        isConfirmingRestore = false;
        restoreFailure = null;
    }

    private async Task ConfirmRestoreAsync()
    {
        if (retentionActionsDocumentId is not { } id || isRestoring)
        {
            return;
        }

        isRestoring = true;
        restoreFailure = null;

        var result = await DocumentsApi.RestoreAsync(id, new RestoreDocumentRequest(null), disposeCancellation.Token);

        isRestoring = false;

        if (!result.IsSuccess)
        {
            restoreFailure = result.Failure;

            return;
        }

        isConfirmingRestore = false;

        await RefreshAfterRetentionActionAsync("Document restored.");
    }

    private async Task PlaceHoldAsync()
    {
        if (retentionActionsDocumentId is not { } id || isPlacingHold)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(holdReason))
        {
            holdReasonValidationMessage = "A reason is required to place a legal hold.";

            return;
        }

        holdReasonValidationMessage = null;
        isPlacingHold = true;
        placeHoldFailure = null;

        var result = await DocumentsApi.PlaceLegalHoldAsync(
            id, new PlaceLegalHoldRequest(holdReason, holdReference), disposeCancellation.Token);

        isPlacingHold = false;

        if (!result.IsSuccess)
        {
            placeHoldFailure = result.Failure;

            return;
        }

        holdReason = null;
        holdReference = null;

        await RefreshAfterRetentionActionAsync("Legal hold placed.");
    }

    private void OpenReleaseHoldConfirmation(Guid holdId)
    {
        confirmingReleaseHoldId = holdId;
        releaseReason = null;
        releaseReasonValidationMessage = null;
        releaseHoldFailure = null;
    }

    private void CancelReleaseHoldConfirmation()
    {
        confirmingReleaseHoldId = null;
        releaseReason = null;
        releaseReasonValidationMessage = null;
        releaseHoldFailure = null;
    }

    private async Task ConfirmReleaseHoldAsync()
    {
        if (retentionActionsDocumentId is not { } id || confirmingReleaseHoldId is not { } holdId || isReleasingHold)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(releaseReason))
        {
            releaseReasonValidationMessage = "A reason is required to release a legal hold.";

            return;
        }

        releaseReasonValidationMessage = null;
        isReleasingHold = true;
        releaseHoldFailure = null;

        var result = await DocumentsApi.ReleaseLegalHoldAsync(
            id, holdId, new ReleaseLegalHoldRequest(releaseReason), disposeCancellation.Token);

        isReleasingHold = false;

        if (!result.IsSuccess)
        {
            releaseHoldFailure = result.Failure;

            return;
        }

        confirmingReleaseHoldId = null;
        releaseReason = null;

        await RefreshAfterRetentionActionAsync("Legal hold released.");
    }

    private async Task CreateDispositionRequestAsync()
    {
        if (retentionActionsDocumentId is not { } id || isCreatingDisposition)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dispositionReason))
        {
            dispositionReasonValidationMessage = "A reason is required to request disposition.";

            return;
        }

        dispositionReasonValidationMessage = null;
        isCreatingDisposition = true;
        createDispositionFailure = null;

        var result = await DocumentsApi.CreateDispositionRequestAsync(
            id, new CreateDispositionRequest(dispositionReason), disposeCancellation.Token);

        isCreatingDisposition = false;

        if (!result.IsSuccess)
        {
            createDispositionFailure = result.Failure;

            return;
        }

        dispositionReason = null;

        await RefreshAfterRetentionActionAsync("Disposition requested.");
    }

    private void OpenApproveDispositionConfirmation()
    {
        isConfirmingApproveDisposition = true;
        approveDecisionReason = null;
        approveDispositionFailure = null;
    }

    private void CancelApproveDispositionConfirmation()
    {
        isConfirmingApproveDisposition = false;
        approveDecisionReason = null;
        approveDispositionFailure = null;
    }

    private async Task ConfirmApproveDispositionAsync()
    {
        if (retentionActionsDocumentId is not { } id
            || retentionActionsRow?.PendingDisposition is not { } pending
            || isApprovingDisposition)
        {
            return;
        }

        isApprovingDisposition = true;
        approveDispositionFailure = null;

        var result = await DocumentsApi.ApproveDispositionRequestAsync(
            id, pending.Id, new ApproveDispositionRequestRequest(approveDecisionReason), disposeCancellation.Token);

        isApprovingDisposition = false;

        if (!result.IsSuccess)
        {
            approveDispositionFailure = result.Failure;

            return;
        }

        isConfirmingApproveDisposition = false;
        approveDecisionReason = null;

        await RefreshAfterRetentionActionAsync("Disposition approved.");
    }

    private void OpenRejectDispositionConfirmation()
    {
        isConfirmingRejectDisposition = true;
        rejectDecisionReason = null;
        rejectDecisionReasonValidationMessage = null;
        rejectDispositionFailure = null;
    }

    private void CancelRejectDispositionConfirmation()
    {
        isConfirmingRejectDisposition = false;
        rejectDecisionReason = null;
        rejectDecisionReasonValidationMessage = null;
        rejectDispositionFailure = null;
    }

    private async Task ConfirmRejectDispositionAsync()
    {
        if (retentionActionsDocumentId is not { } id
            || retentionActionsRow?.PendingDisposition is not { } pending
            || isRejectingDisposition)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rejectDecisionReason))
        {
            rejectDecisionReasonValidationMessage = "A decision reason is required to reject a disposition request.";

            return;
        }

        rejectDecisionReasonValidationMessage = null;
        isRejectingDisposition = true;
        rejectDispositionFailure = null;

        var result = await DocumentsApi.RejectDispositionRequestAsync(
            id, pending.Id, new RejectDispositionRequestRequest(rejectDecisionReason), disposeCancellation.Token);

        isRejectingDisposition = false;

        if (!result.IsSuccess)
        {
            rejectDispositionFailure = result.Failure;

            return;
        }

        isConfirmingRejectDisposition = false;
        rejectDecisionReason = null;

        await RefreshAfterRetentionActionAsync("Disposition rejected.");
    }

    private static string GetRetentionStateClass(string state) => state switch
    {
        RetentionActiveState => "documents-status-active",
        RetentionArchivedState => "documents-review-status-pending",
        RetentionPendingDispositionState => "documents-review-status-pending",
        RetentionDisposedState => "documents-status-expired",
        _ => "documents-review-status-draft",
    };

    private static string FormatRetentionDate(DateTime valueUtc) => $"{valueUtc:dd MMM yyyy}";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record RetentionPolicyRow(int DocumentTypeId, string DocumentTypeName, int? RetentionPeriodDays);
}
