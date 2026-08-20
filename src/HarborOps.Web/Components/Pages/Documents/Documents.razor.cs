using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
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
    private int filterVesselPickerResetToken;

    private DateOnly? appliedFrom;
    private DateOnly? appliedTo;
    private Guid? appliedVesselId;

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

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private bool IsUploadValidationFailure => uploadFailure is { IsValidation: true };

    private IEnumerable<string> UploadValidationMessages =>
        uploadFailure?.Errors.SelectMany(entry => entry.Value) ?? [];

    protected override void OnInitialized()
    {
        registerTypeEditContext = new EditContext(registerTypeModel);
        registerTypeMessageStore = new ValidationMessageStore(registerTypeEditContext);

        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
    }

    protected override Task OnInitializedAsync()
    {
        return Task.WhenAll(LoadAsync(), LoadDocumentTypeOptionsAsync());
    }

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
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        filterSearchCancellation?.Cancel();
        fromFilter = null;
        toFilter = null;
        vesselIdFilter = null;
        filterVesselPickerResetToken++;
        appliedFrom = null;
        appliedTo = null;
        appliedVesselId = null;
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

        var query = new GetExpiringDocumentsQuery(appliedFrom, appliedTo, appliedVesselId, pageNumber, pageSize);

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
                    : ShortVesselReference(item.VesselId),
                item.Number,
                item.DocumentTypeName,
                item.IssuingAuthority,
                item.IssueDate,
                item.ExpiryDate,
                item.Status,
                item.DaysUntilExpiry))
            .ToList();
    }

    private static string? ValidateFilterRange(DateOnly? from, DateOnly? to)
    {
        return from is { } fromDate && to is { } toDate && toDate < fromDate
            ? "\"To\" date must be on or after the \"From\" date."
            : null;
    }

    private static string ShortVesselReference(Guid vesselId) => $"Vessel {vesselId.ToString()[..8]}";

    private static string GetDocumentStatusClass(string status) => status.Trim().ToUpperInvariant() switch
    {
        "ACTIVE" => "documents-status-active",
        "VALID" => "documents-status-valid",
        "EXPIRED" => "documents-status-expired",
        "SUSPENDED" or "INACTIVE" => "documents-status-neutral",
        _ => "documents-status-neutral",
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

    private async Task ToggleFilePanelAsync(Guid documentId)
    {
        if (expandedDocumentId == documentId)
        {
            expandedDocumentId = null;

            return;
        }

        expandedDocumentId = documentId;
        ResetFilePanelState();

        await LoadFileDetailsAsync(documentId);
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

    public void Dispose()
    {
        filterSearchCancellation?.Cancel();
        filterSearchCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

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
