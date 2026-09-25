using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class Documents
{
    // Mirrors HarborOps.Modules.Documents.Domain.DocumentType.NameMaxLength /
    // IssuingAuthorityMaxLength and RegisterDocumentTypeValidator's day-range constants,
    // so the client-side DataAnnotations reject the same inputs the backend would.
    private const int DocumentTypeNameMaxLength = 200;
    private const int DocumentTypeIssuingAuthorityMaxLength = 200;
    private const int MinMandatoryValidityPeriodDays = 1;
    private const int MaxMandatoryValidityPeriodDays = 3650;

    // Mirrors HarborOps.Modules.Documents.Domain.Document.NumberMaxLength /
    // IssuingAuthorityMaxLength (kept as its own constant even though the issuing-authority
    // limit happens to equal the document type's today - they are separate backend
    // constants on separate domain types and could diverge later).
    private const int DocumentNumberMaxLength = 100;
    private const int DocumentIssuingAuthorityMaxLength = 200;

    // RegisterDocumentRequest.Status is required server-side (RegisterDocumentValidator
    // rejects a missing/blank Status) but the Domain does not compute or default it - the
    // caller must supply one. The form does not expose Status as an editable field, so a
    // fixed value is sent instead; "Active" matches the value already used as the
    // conventional initial status in the backend's own tests
    // (RegisterDocumentServiceTests, RegisterDocumentEndpointTests).
    private const string DefaultDocumentStatus = "Active";

    // Mirrors HarborOps.Modules.Documents.Domain.Document's review-status constants.
    // Duplicated here for the same reason as DefaultDocumentStatus above: Web has no
    // reference to the Application/Domain assembly, so these are the client's own copy.
    private const string DraftReviewStatus = "Draft";
    private const string PendingReviewReviewStatus = "PendingReview";
    private const string ApprovedReviewStatus = "Approved";
    private const string RejectedReviewStatus = "Rejected";

    private const int DocumentTypeOptionsPageSize = 100;

    // Same known, documented coupling as DocumentsApiClient.MaxUploadSizeBytes - mirrors
    // DocumentFileValidationOptions.MaxSizeBytes (server default, 25 MB), duplicated here
    // for display purposes only ("Maximum allowed size is X") since Api/Documents/* is out
    // of scope for this step. Also used to reject an oversized file before it ever reaches
    // DocumentsApiClient - see the comment in SubmitFileAsync for why that pre-check exists.
    private const long MaxUploadSizeBytesForDisplay = 26_214_400;

    // Field names exactly as sent by RegisterDocumentTypeValidator (Application layer) -
    // case-sensitive, matched with StringComparer.Ordinal below.
    private const string NameFieldKey = "name";
    private const string IssuingAuthorityFieldKey = "issuingAuthority";
    private const string MandatoryValidityPeriodDaysFieldKey = "mandatoryValidityPeriodDays";
    private const string RequiresSurveyFieldKey = "requiresSurvey";

    // Field names exactly as sent by RegisterDocumentValidator (Application layer) -
    // case-sensitive, matched with StringComparer.Ordinal below. IssuingAuthorityFieldKey
    // above is reused as-is: both validators literally send the same key ("issuingAuthority").
    private const string VesselIdFieldKey = "vesselId";
    private const string DocumentTypeIdFieldKey = "documentTypeId";
    private const string DocumentNumberFieldKey = "number";
    private const string IssueDateFieldKey = "issueDate";
    private const string ExpiryDateFieldKey = "expiryDate";

    private static readonly IReadOnlyDictionary<string, string> RegisterTypeServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NameFieldKey] = nameof(RegisterDocumentTypeFormModel.Name),
            [IssuingAuthorityFieldKey] = nameof(RegisterDocumentTypeFormModel.IssuingAuthority),
            [MandatoryValidityPeriodDaysFieldKey] = nameof(RegisterDocumentTypeFormModel.MandatoryValidityPeriodDays),
            [RequiresSurveyFieldKey] = nameof(RegisterDocumentTypeFormModel.RequiresSurvey),
        };

    private static readonly IReadOnlyDictionary<string, string> RegisterDocumentServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VesselIdFieldKey] = nameof(RegisterDocumentFormModel.VesselId),
            [DocumentTypeIdFieldKey] = nameof(RegisterDocumentFormModel.DocumentTypeId),
            [DocumentNumberFieldKey] = nameof(RegisterDocumentFormModel.Number),
            [IssueDateFieldKey] = nameof(RegisterDocumentFormModel.IssueDate),
            [ExpiryDateFieldKey] = nameof(RegisterDocumentFormModel.ExpiryDate),
            [IssuingAuthorityFieldKey] = nameof(RegisterDocumentFormModel.IssuingAuthority),
        };

    private static readonly IReadOnlyDictionary<string, string[]> EmptyFileErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private RegisterDocumentTypeFormModel registerTypeModel = new();

    // C# field initializers cannot reference another instance field (CS0236), so the
    // EditContext/ValidationMessageStore pairs that must wrap registerTypeModel /
    // registerDocumentModel are built in OnInitialized() instead.
    private EditContext registerTypeEditContext = null!;
    private ValidationMessageStore registerTypeMessageStore = null!;
    private bool isRegisteringType;
    private bool isRegisterTypeFormOpen;
    private string? registerTypeSuccessMessage;
    private string? registerTypeConflictMessage;
    private string? registerTypeGeneralError;
    private IReadOnlyList<string> registerTypeUnmappedErrors = [];

    private IReadOnlyList<DocumentTypeListItem> documentTypeOptions = [];
    private bool isLoadingDocumentTypes = true;

    private RegisterDocumentFormModel registerDocumentModel = new();
    private EditContext registerDocumentEditContext = null!;
    private ValidationMessageStore registerDocumentMessageStore = null!;
    private bool isRegisteringDocument;
    private bool isRegisterDocumentFormOpen;
    private string? registerDocumentSuccessMessage;
    private string? registerDocumentConflictMessage;
    private string? registerDocumentGeneralError;
    private IReadOnlyList<string> registerDocumentUnmappedErrors = [];

    // Bumped on every successful document registration and bound to VesselPicker's @key,
    // forcing Blazor to discard the old instance and mount a fresh one. VesselPicker has no
    // reset method of its own and does not resync its internal display text when its Value
    // parameter is set back to null from outside (see the comment on VesselPicker.Value) -
    // @key is the only reliable way to blank it from a parent.
    private int vesselPickerResetToken;

    internal const string RequirementsTab = "requirements";
    internal const string ReadinessTab = "readiness";
    internal const string RetentionTab = "retention";

    private DocumentsTab activeTab = DocumentsTab.Expiring;

    [SupplyParameterFromQuery(Name = "tab")]
    private string? RequestedTab { get; set; }

    [SupplyParameterFromQuery(Name = "vesselId")]
    private Guid? RequestedVesselId { get; set; }
    private int readinessRefreshToken;
    private string? registerDocumentVesselLabel;

    private string DocumentTypePlaceholder => isLoadingDocumentTypes
        ? "Loading document types..."
        : documentTypeOptions.Count == 0
            ? "No document types available - register one first"
            : "Select...";

    private readonly CancellationTokenSource disposeCancellation = new();
    private CancellationTokenSource? filterSearchCancellation;
    private int loadVersion;

    private IReadOnlyList<ExpiringDocumentRow> rows = [];
    private DocumentsApiFailure? failure;
    private string? filterValidationMessage;

    private DateOnly? fromFilter;
    private DateOnly? toFilter;
    private Guid? vesselIdFilter;
    private string? pendingReviewStatus;
    private int filterVesselPickerResetToken;

    private DateOnly? appliedFrom;
    private DateOnly? appliedTo;
    private Guid? appliedVesselId;
    private string? appliedReviewStatus;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private bool isLoading = true;

    // File detail panel: only one row's panel can be open at a time, so all of this state
    // is shared/reset per toggle rather than tracked per-document-id.
    private Guid? expandedDocumentId;
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

    // Unlike the main grid's isLoading (which starts true because LoadAsync runs
    // unconditionally from OnInitializedAsync), the retention queue is loaded lazily on
    // first tab-open, gated on !isLoadingRetentionQueue - starting this true would make
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
    // time, same reasoning as the file panel's expandedDocumentId above.
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

    private int RetentionTotalPages => retentionTotalCount == 0 ? 1 : ((retentionTotalCount + retentionPageSize - 1) / retentionPageSize);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private bool IsUploadValidationFailure => uploadFailure is { IsValidation: true };

    private bool ShowUploadForm =>
        fileDetails is not null
        && ((!fileDetails.HasFile && AuthorizationState.HasPermission("documents.manage"))
            || (fileDetails.HasFile && isReplaceFormOpen));

    private IEnumerable<string> UploadValidationMessages =>
        uploadFailure?.Errors.SelectMany(entry => entry.Value) ?? [];

    private bool CanSubmitForReview =>
        fileDetails is { HasFile: true, ReviewStatus: DraftReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    private bool CanApproveOrReject =>
        fileDetails is { ReviewStatus: PendingReviewReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    private bool CanRevoke =>
        fileDetails is { Status: "Active", ReviewStatus: ApprovedReviewStatus }
        && AuthorizationState.HasPermission("documents.manage");

    protected override void OnInitialized()
    {
        registerTypeEditContext = new EditContext(registerTypeModel);
        registerTypeMessageStore = new ValidationMessageStore(registerTypeEditContext);

        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
    }

    protected override Task OnInitializedAsync()
    {
        activeTab = RequestedTab?.ToLowerInvariant() switch
        {
            RequirementsTab => DocumentsTab.Requirements,
            ReadinessTab => DocumentsTab.Readiness,
            RetentionTab when CanOpenRetentionTab => DocumentsTab.Retention,
            _ => DocumentsTab.Expiring,
        };

        return activeTab == DocumentsTab.Retention
            ? Task.WhenAll(LoadAsync(), LoadDocumentTypeOptionsAsync(), LoadRetentionTabAsync())
            : Task.WhenAll(LoadAsync(), LoadDocumentTypeOptionsAsync());
    }

    private bool CanOpenRetentionTab =>
        AuthorizationState.HasPermission("documents.read") || AuthorizationState.HasPermission("documents.manage");

    private Task SelectFilterVesselAsync(Guid? vesselId)
    {
        vesselIdFilter = vesselId;

        return ScheduleFilterSearchAsync();
    }

    private async Task ScheduleFilterSearchAsync()
    {
        var previousCancellation = filterSearchCancellation;
        previousCancellation?.Cancel();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        filterSearchCancellation = cancellation;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation.Token);

            if (!cancellation.IsCancellationRequested)
            {
                await InvokeAsync(ApplyFiltersAsync);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(filterSearchCancellation, cancellation))
            {
                filterSearchCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private Task ApplyFiltersAsync()
    {
        var validationMessage = ValidateFilterRange(fromFilter, toFilter);

        if (validationMessage is not null)
        {
            filterValidationMessage = validationMessage;

            return Task.CompletedTask;
        }

        filterValidationMessage = null;
        appliedFrom = fromFilter;
        appliedTo = toFilter;
        appliedVesselId = vesselIdFilter;
        // The "All" option posts an empty string (Blazor's @bind on <select> cannot bind
        // a null option value), so it is normalized to null here rather than sent to the
        // API as reviewStatus= - an empty string would fail the backend's known-value check.
        appliedReviewStatus = string.IsNullOrEmpty(pendingReviewStatus) ? null : pendingReviewStatus;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        filterSearchCancellation?.Cancel();
        fromFilter = null;
        toFilter = null;
        vesselIdFilter = null;
        pendingReviewStatus = null;
        filterVesselPickerResetToken++;
        appliedFrom = null;
        appliedTo = null;
        appliedVesselId = null;
        appliedReviewStatus = null;
        filterValidationMessage = null;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private Task PreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        pageNumber--;

        return LoadAsync();
    }

    private Task NextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return Task.CompletedTask;
        }

        pageNumber++;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var query = new GetExpiringDocumentsQuery(appliedFrom, appliedTo, appliedVesselId, pageNumber, pageSize, appliedReviewStatus);

        DocumentsApiResult<ExpiringDocumentsPage> result;

        try
        {
            result = await DocumentsApi.GetExpiringDocumentsAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            rows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;

        await ComposeRowsAsync(page.Items, cancellationToken, requestVersion);
    }

    private async Task ComposeRowsAsync(
        IReadOnlyList<ExpiringDocumentListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await FleetApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new ExpiringDocumentRow(
                item.DocumentId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : "Unknown vessel",
                item.Number,
                item.DocumentTypeName,
                item.IssuingAuthority,
                item.IssueDate,
                item.ExpiryDate,
                item.Status,
                item.ReviewStatus,
                item.DaysUntilExpiry))
            .ToList();
    }

    private static string? ValidateFilterRange(DateOnly? from, DateOnly? to)
    {
        return from is { } fromDate && to is { } toDate && toDate < fromDate
            ? "\"To\" date must be on or after the \"From\" date."
            : null;
    }

    private static string GetDocumentStatusClass(string status) => status.Trim().ToUpperInvariant() switch
    {
        "ACTIVE" => "documents-status-active",
        "VALID" => "documents-status-valid",
        "EXPIRED" => "documents-status-expired",
        "SUSPENDED" or "INACTIVE" => "documents-status-neutral",
        _ => "documents-status-neutral",
    };

    private static string GetReviewStatusClass(string reviewStatus) => reviewStatus.Trim().ToUpperInvariant() switch
    {
        "DRAFT" => "documents-review-status-draft",
        "PENDINGREVIEW" => "documents-review-status-pending",
        "APPROVED" => "documents-review-status-approved",
        "REJECTED" => "documents-review-status-rejected",
        _ => "documents-review-status-draft",
    };

    private async Task LoadDocumentTypeOptionsAsync()
    {
        isLoadingDocumentTypes = true;

        DocumentsApiResult<DocumentTypesPage> result;

        try
        {
            result = await DocumentsApi.GetDocumentTypesAsync(
                new GetDocumentTypesQuery(null, DocumentTypeOptionsPageSize),
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

        isLoadingDocumentTypes = false;
        documentTypeOptions = result.IsSuccess ? result.Value!.Items : [];
    }

    private void ToggleRegisterTypeForm()
    {
        isRegisterTypeFormOpen = !isRegisterTypeFormOpen;

        if (isRegisterTypeFormOpen)
        {
            isRegisterDocumentFormOpen = false;
        }
    }

    private void ToggleRegisterDocumentForm()
    {
        isRegisterDocumentFormOpen = !isRegisterDocumentFormOpen;

        if (isRegisterDocumentFormOpen)
        {
            isRegisterTypeFormOpen = false;
        }
    }

    private async Task SelectTab(DocumentsTab tab)
    {
        activeTab = tab;

        if (tab == DocumentsTab.Retention)
        {
            await LoadRetentionTabAsync();
        }
    }

    private string AriaSelected(DocumentsTab tab)
    {
        return activeTab == tab ? "true" : "false";
    }

    private void OpenRegisterFormForMissing((Guid VesselId, string VesselName, int DocumentTypeId) missing)
    {
        registerDocumentModel = new RegisterDocumentFormModel
        {
            VesselId = missing.VesselId,
            DocumentTypeId = missing.DocumentTypeId,
        };
        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
        registerDocumentSuccessMessage = null;
        registerDocumentConflictMessage = null;
        registerDocumentGeneralError = null;
        registerDocumentUnmappedErrors = [];
        registerDocumentVesselLabel = missing.VesselName;
        vesselPickerResetToken++;

        isRegisterDocumentFormOpen = true;
        isRegisterTypeFormOpen = false;
    }

    private void CloseRegisterTypeForm() => isRegisterTypeFormOpen = false;

    private void CloseRegisterDocumentForm() => isRegisterDocumentFormOpen = false;

    private async Task SubmitDocumentTypeAsync()
    {
        // Clear stale state before re-validating, not inside the success branch of
        // Validate(): if a previous submit left custom (server) messages in the store,
        // EditContext.Validate() would see them and never report success again.
        registerTypeMessageStore.Clear();
        registerTypeConflictMessage = null;
        registerTypeGeneralError = null;
        registerTypeUnmappedErrors = [];
        registerTypeSuccessMessage = null;

        if (!registerTypeEditContext.Validate())
        {
            return;
        }

        if (isRegisteringType)
        {
            return;
        }

        isRegisteringType = true;

        var request = new RegisterDocumentTypeRequest
        {
            Name = registerTypeModel.Name,
            MandatoryValidityPeriodDays = registerTypeModel.MandatoryValidityPeriodDays,
            IssuingAuthority = registerTypeModel.IssuingAuthority,
            RequiresSurvey = registerTypeModel.RequiresSurvey,
        };

        var result = await DocumentsApi.RegisterDocumentTypeAsync(request, CancellationToken.None);

        isRegisteringType = false;

        if (!result.IsSuccess)
        {
            var typeFailure = result.Failure!;

            if (typeFailure.IsConflict)
            {
                registerTypeConflictMessage = ExtractConflictMessage(typeFailure, NameFieldKey);
            }
            else if (typeFailure.IsValidation)
            {
                registerTypeUnmappedErrors = ApplyServerValidationErrors(
                    typeFailure.Errors, RegisterTypeServerFieldMap, registerTypeModel, registerTypeMessageStore);
                registerTypeEditContext.NotifyValidationStateChanged();
            }
            else
            {
                registerTypeGeneralError = typeFailure.Message;
            }

            // Entered values are intentionally preserved on every failure branch above -
            // formModel is only replaced below, on success.
            return;
        }

        registerTypeSuccessMessage = "Document type registered.";

        registerTypeModel = new RegisterDocumentTypeFormModel();
        registerTypeEditContext = new EditContext(registerTypeModel);
        registerTypeMessageStore = new ValidationMessageStore(registerTypeEditContext);

        // The Register Document form's document-type dropdown is populated from the same
        // endpoint - refresh it now so the newly registered type is selectable immediately,
        // without a manual page reload.
        await LoadDocumentTypeOptionsAsync();
    }

    private async Task SubmitDocumentAsync()
    {
        registerDocumentMessageStore.Clear();
        registerDocumentConflictMessage = null;
        registerDocumentGeneralError = null;
        registerDocumentUnmappedErrors = [];
        registerDocumentSuccessMessage = null;

        if (!registerDocumentEditContext.Validate())
        {
            return;
        }

        if (isRegisteringDocument)
        {
            return;
        }

        isRegisteringDocument = true;

        var request = new RegisterDocumentRequest
        {
            VesselId = registerDocumentModel.VesselId,
            DocumentTypeId = registerDocumentModel.DocumentTypeId,
            Number = registerDocumentModel.Number,
            IssueDate = registerDocumentModel.IssueDate,
            ExpiryDate = registerDocumentModel.ExpiryDate,
            IssuingAuthority = registerDocumentModel.IssuingAuthority,
            Status = DefaultDocumentStatus,
        };

        var result = await DocumentsApi.RegisterDocumentAsync(request, CancellationToken.None);

        isRegisteringDocument = false;

        if (!result.IsSuccess)
        {
            var documentFailure = result.Failure!;

            if (documentFailure.IsConflict)
            {
                registerDocumentConflictMessage = ExtractConflictMessage(documentFailure, DocumentNumberFieldKey);
            }
            else if (documentFailure.IsValidation)
            {
                registerDocumentUnmappedErrors = ApplyServerValidationErrors(
                    documentFailure.Errors, RegisterDocumentServerFieldMap, registerDocumentModel, registerDocumentMessageStore);
                registerDocumentEditContext.NotifyValidationStateChanged();
            }
            else
            {
                registerDocumentGeneralError = documentFailure.Message;
            }

            // Entered values (including the selected vessel/document type) are intentionally
            // preserved on every failure branch above - the form is only reset below, on success.
            return;
        }

        registerDocumentSuccessMessage = "Document registered.";

        registerDocumentModel = new RegisterDocumentFormModel();
        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
        vesselPickerResetToken++;
        readinessRefreshToken++;
        registerDocumentVesselLabel = null;

        // Refresh the grid so a newly registered document appears immediately if it falls
        // within the currently applied date/vessel filters.
        await LoadAsync();
    }

    private static string ExtractConflictMessage(DocumentsApiFailure failure, string conflictFieldKey)
    {
        return failure.Errors.TryGetValue(conflictFieldKey, out var messages) && messages.Length > 0
            ? messages[0]
            : failure.Message;
    }

    private static IReadOnlyList<string> ApplyServerValidationErrors(
        IReadOnlyDictionary<string, string[]> errors,
        IReadOnlyDictionary<string, string> fieldMap,
        object model,
        ValidationMessageStore messageStore)
    {
        var unmapped = new List<string>();

        foreach (var (key, messages) in errors)
        {
            if (fieldMap.TryGetValue(key, out var propertyName))
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

    private async Task OpenFilePanelAsync(Guid documentId)
    {
        expandedDocumentId = documentId;
        ResetFilePanelState();

        await LoadFileDetailsAsync(documentId);
    }

    private void CloseFilePanel()
    {
        if (isUploading)
        {
            return;
        }

        expandedDocumentId = null;
        ResetFilePanelState();
    }

    private void ResetFilePanelState()
    {
        fileDetails = null;
        fileDetailsFailure = null;
        isConfirmingReplace = false;
        isReplaceFormOpen = false;
        selectedUploadFile = null;
        uploadFailure = null;
        uploadSuccessMessage = null;
        isUploading = false;
        uploadProgress = 0;
        isEditingMetadata = false;
        isRenewingDocument = false;
        lifecycleFailure = null;
        renewalHistory = [];

        isSubmittingForReview = false;
        isApproving = false;
        reviewActionSuccessMessage = null;

        isRejectingDocument = false;
        rejectNotes = null;
        rejectNotesValidationMessage = null;
        isSubmittingRejection = false;
        rejectFailure = null;

        isConfirmingRevoke = false;
        revokeReason = null;
        revokeReasonValidationMessage = null;
        isRevoking = false;
        revokeFailure = null;

        isDistributionFormOpen = false;
        isIssuingDistribution = false;
        distributionInstruction = null;
        distributionRecipientSearch = null;
        distributionRecipients = [];
        selectedDistributionRecipients = [];
        distributionFailure = null;
        distributionSuccessMessage = null;

        isReviewHistoryExpanded = false;
        isLoadingReviewHistory = false;
        reviewHistory = [];
        reviewHistoryFailure = null;
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
        if (expandedDocumentId is not { } documentId) return;
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
        if (expandedDocumentId is not { } id) return;
        lifecycleFailure = null;
        var result = await DocumentsApi.UpdateMetadataAsync(id, LifecycleRequest(), disposeCancellation.Token);
        if (!result.IsSuccess) { lifecycleFailure = result.Failure; return; }
        isEditingMetadata = false;
        await LoadFileDetailsAsync(id);
        await LoadAsync();
    }

    private async Task RenewDocumentAsync()
    {
        if (expandedDocumentId is not { } id) return;
        lifecycleFailure = null;
        var result = await DocumentsApi.RenewAsync(id, LifecycleRequest(), disposeCancellation.Token);
        if (!result.IsSuccess) { lifecycleFailure = result.Failure; return; }
        isRenewingDocument = false;
        await LoadFileDetailsAsync(id);
        await LoadAsync();
    }

    private DocumentMetadataRequest LifecycleRequest() => new(lifecycleTypeId, lifecycleNumber, lifecycleIssueDate, lifecycleExpiryDate, lifecycleAuthority);

    private async Task SubmitForReviewAsync()
    {
        if (expandedDocumentId is not { } id || isSubmittingForReview)
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
        if (expandedDocumentId is not { } id || isApproving)
        {
            return;
        }

        isApproving = true;
        lifecycleFailure = null;
        reviewActionSuccessMessage = null;

        var result = await DocumentsApi.ReviewAsync(id, new ReviewDocumentRequest(ApprovedReviewStatus, null), disposeCancellation.Token);

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
        if (expandedDocumentId is not { } id || isSubmittingRejection)
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

        var result = await DocumentsApi.ReviewAsync(id, new ReviewDocumentRequest(RejectedReviewStatus, rejectNotes), disposeCancellation.Token);

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
        if (expandedDocumentId is not { } id || isRevoking)
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

        await LoadAsync();
    }

    // A 409 caused by DbUpdateConcurrencyException on the server carries this exact title
    // (see DocumentsController.Review/Revoke) - refreshing the grid on this specific
    // conflict surfaces the document's current real state, unlike other 409s (self-review,
    // wrong lifecycle state) which are user-actionable as-is and do not need a refresh.
    private static bool IsConcurrencyConflict(DocumentsApiFailure failure) =>
        string.Equals(failure.Title, "Document was modified concurrently.", StringComparison.Ordinal);

    private Task RefreshListIfConcurrencyConflictAsync(DocumentsApiFailure? failure) =>
        failure is not null && IsConcurrencyConflict(failure) ? LoadAsync() : Task.CompletedTask;

    private async Task ToggleReviewHistoryAsync()
    {
        isReviewHistoryExpanded = !isReviewHistoryExpanded;

        if (isReviewHistoryExpanded && expandedDocumentId is { } id && reviewHistory.Count == 0 && reviewHistoryFailure is null)
        {
            await LoadReviewHistoryAsync(id);
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
        if (selectedUploadFile.Size > MaxUploadSizeBytesForDisplay)
        {
            uploadFailure = new DocumentsApiFailure(
                StatusCodes.Status413PayloadTooLarge,
                "File is too large.",
                "File is too large.",
                EmptyFileErrors);

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
            return $"File is too large. Maximum allowed size is {FormatBytes(MaxUploadSizeBytesForDisplay)}.";
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

    private async Task LoadRetentionTabAsync()
    {
        // Awaited, not fire-and-forget: this is a synchronous-looking void handler's async
        // counterpart, and Blazor only re-renders automatically when it can track the Task an
        // event handler returns. A detached `_ = LoadXAsync()` call here would run to
        // completion on its own with no render ever reflecting it - the loading spinner
        // would show (set synchronously before this method's first real await) and then
        // never clear, exactly the bug this fixes. See ToggleReviewHistoryAsync above for the
        // same shape already used correctly elsewhere on this page.
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
    // HandleReviewActionSuccessAsync above. The document can legitimately drop out of the
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
        filterSearchCancellation?.Cancel();
        filterSearchCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record RetentionPolicyRow(int DocumentTypeId, string DocumentTypeName, int? RetentionPeriodDays);

    private sealed record ExpiringDocumentRow(
        Guid DocumentId,
        Guid VesselId,
        string VesselDisplayName,
        string Number,
        string DocumentTypeName,
        string IssuingAuthority,
        DateOnly IssueDate,
        DateOnly ExpiryDate,
        string Status,
        string ReviewStatus,
        int DaysUntilExpiry);

    private sealed class RegisterDocumentTypeFormModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(DocumentTypeNameMaxLength, ErrorMessage = "Name must not exceed 200 characters.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Issuing authority is required.")]
        [StringLength(DocumentTypeIssuingAuthorityMaxLength, ErrorMessage = "Issuing authority must not exceed 200 characters.")]
        public string? IssuingAuthority { get; set; }

        [Required(ErrorMessage = "Mandatory validity period is required.")]
        [Range(MinMandatoryValidityPeriodDays, MaxMandatoryValidityPeriodDays,
            ErrorMessage = "Mandatory validity period must be between 1 and 3650 days.")]
        public int? MandatoryValidityPeriodDays { get; set; }

        [Required(ErrorMessage = "Requires survey is required.")]
        public bool? RequiresSurvey { get; set; }
    }

    private sealed class RegisterDocumentFormModel : IValidatableObject
    {
        [Required(ErrorMessage = "Document number is required.")]
        [StringLength(DocumentNumberMaxLength, ErrorMessage = "Document number must not exceed 100 characters.")]
        public string? Number { get; set; }

        [Required(ErrorMessage = "Vessel is required.")]
        public Guid? VesselId { get; set; }

        [Required(ErrorMessage = "Document type is required.")]
        public int? DocumentTypeId { get; set; }

        [Required(ErrorMessage = "Issue date is required.")]
        public DateOnly? IssueDate { get; set; }

        [Required(ErrorMessage = "Expiry date is required.")]
        public DateOnly? ExpiryDate { get; set; }

        [Required(ErrorMessage = "Issuing authority is required.")]
        [StringLength(DocumentIssuingAuthorityMaxLength, ErrorMessage = "Issuing authority must not exceed 200 characters.")]
        public string? IssuingAuthority { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ExpiryDate is { } expiry && IssueDate is { } issue && expiry < issue)
            {
                yield return new ValidationResult(
                    "Expiry date must not be earlier than the issue date.",
                    [nameof(ExpiryDate)]);
            }
        }
    }
}
