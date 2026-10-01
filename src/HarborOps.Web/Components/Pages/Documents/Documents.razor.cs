using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
    internal const int DocumentNumberMaxLength = 100;
    internal const int DocumentIssuingAuthorityMaxLength = 200;

    // RegisterDocumentRequest.Status is required server-side (RegisterDocumentValidator
    // rejects a missing/blank Status) but the Domain does not compute or default it - the
    // caller must supply one. The form does not expose Status as an editable field, so a
    // fixed value is sent instead; "Active" matches the value already used as the
    // conventional initial status in the backend's own tests
    // (RegisterDocumentServiceTests, RegisterDocumentEndpointTests).
    internal const string DefaultDocumentStatus = "Active";

    // Mirrors HarborOps.Modules.Documents.Domain.Document's review-status constants.
    // Duplicated here for the same reason as DefaultDocumentStatus above: Web has no
    // reference to the Application/Domain assembly, so these are the client's own copy.
    internal const string DraftReviewStatus = "Draft";
    internal const string PendingReviewReviewStatus = "PendingReview";
    internal const string ApprovedReviewStatus = "Approved";
    internal const string RejectedReviewStatus = "Rejected";

    internal const int DocumentTypeOptionsPageSize = 100;
    internal const int DueSoonDays = 30;

    // Same known, documented coupling as DocumentsApiClient.MaxUploadSizeBytes - mirrors
    // DocumentFileValidationOptions.MaxSizeBytes (server default, 25 MB), duplicated here
    // for display purposes only ("Maximum allowed size is X") since Api/Documents/* is out
    // of scope for this step. Also used to reject an oversized file before it ever reaches
    // DocumentsApiClient - see the comment in DocumentFilePanel.SubmitFileAsync for why that pre-check exists.
    internal const long MaxUploadSizeBytesForDisplay = 26_214_400;

    // Field names exactly as sent by RegisterDocumentTypeValidator (Application layer) -
    // case-sensitive, matched with StringComparer.Ordinal below.
    private const string NameFieldKey = "name";
    internal const string IssuingAuthorityFieldKey = "issuingAuthority";
    private const string MandatoryValidityPeriodDaysFieldKey = "mandatoryValidityPeriodDays";
    private const string RequiresSurveyFieldKey = "requiresSurvey";

    // Field names exactly as sent by RegisterDocumentValidator (Application layer) -
    // case-sensitive, matched with StringComparer.Ordinal below. IssuingAuthorityFieldKey
    // above is reused as-is: both validators literally send the same key ("issuingAuthority").
    internal const string VesselIdFieldKey = "vesselId";
    internal const string DocumentTypeIdFieldKey = "documentTypeId";
    internal const string DocumentNumberFieldKey = "number";
    internal const string IssueDateFieldKey = "issueDate";
    internal const string ExpiryDateFieldKey = "expiryDate";

    private static readonly IReadOnlyDictionary<string, string> RegisterTypeServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NameFieldKey] = nameof(RegisterDocumentTypeFormModel.Name),
            [IssuingAuthorityFieldKey] = nameof(RegisterDocumentTypeFormModel.IssuingAuthority),
            [MandatoryValidityPeriodDaysFieldKey] = nameof(RegisterDocumentTypeFormModel.MandatoryValidityPeriodDays),
            [RequiresSurveyFieldKey] = nameof(RegisterDocumentTypeFormModel.RequiresSurvey),
        };

    internal static readonly IReadOnlyDictionary<string, string[]> EmptyFileErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private RegisterDocumentTypeFormModel registerTypeModel = new();

    // C# field initializers cannot reference another instance field (CS0236), so the
    // EditContext/ValidationMessageStore pair that must wrap registerTypeModel is built in
    // OnInitialized() instead.
    private EditContext registerTypeEditContext = null!;
    private ValidationMessageStore registerTypeMessageStore = null!;
    private bool isRegisteringType;
    private bool isRegisterTypeFormOpen;
    private string? registerTypeSuccessMessage;
    private string? registerTypeConflictMessage;
    private string? registerTypeGeneralError;
    private IReadOnlyList<string> registerTypeUnmappedErrors = [];

    private IReadOnlyList<DocumentTypeOption> documentTypeOptions = [];
    private bool isLoadingDocumentTypes = true;

    private bool isRegisterDocumentFormOpen;

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

    // File detail panel (DocumentFilePanel): only one row's panel can be open at a time, so
    // a single id is tracked rather than one per document.
    private Guid? expandedDocumentId;

    private ExpiringDocumentsSummary? summary;
    private int summaryVersion;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private bool IsExpiredKpiSelected => appliedFrom is null && appliedTo == Today.AddDays(-1);

    private bool IsDueSoonKpiSelected => appliedFrom == Today && appliedTo == Today.AddDays(DueSoonDays);

    private bool IsPendingReviewKpiSelected => appliedReviewStatus == PendingReviewReviewStatus;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    protected override void OnInitialized()
    {
        registerTypeEditContext = new EditContext(registerTypeModel);
        registerTypeMessageStore = new ValidationMessageStore(registerTypeEditContext);
    }

    protected override Task OnInitializedAsync()
    {
        return Task.WhenAll(LoadAsync(), LoadSummaryAsync(), LoadDocumentTypeOptionsAsync());
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
        // The "All" option posts an empty string (Blazor's @bind on <select> cannot bind
        // a null option value), so it is normalized to null here rather than sent to the
        // API as reviewStatus= - an empty string would fail the backend's known-value check.
        appliedReviewStatus = string.IsNullOrEmpty(pendingReviewStatus) ? null : pendingReviewStatus;
        pageNumber = 1;

        return Task.WhenAll(LoadAsync(), LoadSummaryAsync());
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

        return Task.WhenAll(LoadAsync(), LoadSummaryAsync());
    }

    private Task RetryAsync()
    {
        return Task.WhenAll(LoadAsync(), LoadSummaryAsync());
    }

    private Task ToggleExpiredKpiAsync() =>
        IsExpiredKpiSelected ? ApplyDateFilterAsync(null, null) : ApplyDateFilterAsync(null, Today.AddDays(-1));

    private Task ToggleDueSoonKpiAsync() =>
        IsDueSoonKpiSelected ? ApplyDateFilterAsync(null, null) : ApplyDateFilterAsync(Today, Today.AddDays(DueSoonDays));

    private Task TogglePendingReviewKpiAsync()
    {
        filterSearchCancellation?.Cancel();
        pendingReviewStatus = IsPendingReviewKpiSelected ? null : PendingReviewReviewStatus;

        return ApplyFiltersAsync();
    }

    private Task ToggleMonthAsync(DateOnly monthStart)
    {
        if (IsMonthSelected(monthStart))
        {
            return ApplyDateFilterAsync(null, null);
        }

        var (from, to) = MonthRange(monthStart);

        return ApplyDateFilterAsync(from, to);
    }

    private bool IsMonthSelected(DateOnly monthStart)
    {
        var (from, to) = MonthRange(monthStart);

        return appliedFrom == from && appliedTo == to;
    }

    private static (DateOnly From, DateOnly To) MonthRange(DateOnly monthStart) =>
        (monthStart < Today ? Today : monthStart, monthStart.AddMonths(1).AddDays(-1));

    private static string? MonthTone(DateOnly monthStart) =>
        monthStart <= Today.AddDays(DueSoonDays) ? "warning" : null;

    private Task ApplyDateFilterAsync(DateOnly? from, DateOnly? to)
    {
        filterSearchCancellation?.Cancel();
        fromFilter = from;
        toFilter = to;

        return ApplyFiltersAsync();
    }

    private async Task LoadSummaryAsync()
    {
        var requestVersion = ++summaryVersion;
        var query = new GetExpiringDocumentsQuery(appliedFrom, appliedTo, appliedVesselId, null, null, appliedReviewStatus);

        DocumentsApiResult<ExpiringDocumentsSummary> result;

        try
        {
            result = await DocumentsApi.GetExpiringSummaryAsync(query, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != summaryVersion)
        {
            return;
        }

        summary = result.IsSuccess ? result.Value : null;
    }

    private static string FormatCount(int? count) =>
        count?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";

    internal static string DaysLeftTone(int daysUntilExpiry) => daysUntilExpiry switch
    {
        < 0 => "danger",
        <= DueSoonDays => "warning",
        _ => "success",
    };

    internal static string FormatDaysLeft(int daysUntilExpiry) => daysUntilExpiry switch
    {
        < -1 => $"{-daysUntilExpiry} days overdue",
        -1 => "1 day overdue",
        0 => "Today",
        1 => "1 day",
        _ => $"{daysUntilExpiry} days",
    };

    internal static int ValidityElapsedPercent(DateOnly issueDate, DateOnly expiryDate, int daysUntilExpiry)
    {
        var totalDays = expiryDate.DayNumber - issueDate.DayNumber;
        var today = expiryDate.AddDays(-daysUntilExpiry);
        if (totalDays <= 0) return today >= expiryDate ? 100 : 0;

        return Math.Clamp((int)Math.Round((today.DayNumber - issueDate.DayNumber) * 100d / totalDays), 0, 100);
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

    internal static string GetDocumentStatusClass(string status) => status.Trim().ToUpperInvariant() switch
    {
        "ACTIVE" => "documents-status-active",
        "VALID" => "documents-status-valid",
        "EXPIRED" => "documents-status-expired",
        "SUSPENDED" or "INACTIVE" => "documents-status-neutral",
        _ => "documents-status-neutral",
    };

    internal static string GetReviewStatusClass(string reviewStatus) => reviewStatus.Trim().ToUpperInvariant() switch
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
        documentTypeOptions = result.IsSuccess
            ? result.Value!.Items.Select(type => new DocumentTypeOption(type.DocumentTypeId, type.Name, type.MandatoryValidityPeriodDays)).ToList()
            : [];
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

    private async Task OnDocumentRegisteredAsync()
    {
        // Refresh the grid so a newly registered document appears immediately if it falls
        // within the currently applied date/vessel filters.
        await Task.WhenAll(LoadAsync(), LoadSummaryAsync());
    }

    internal static string ExtractConflictMessage(DocumentsApiFailure failure, string conflictFieldKey)
    {
        return failure.Errors.TryGetValue(conflictFieldKey, out var messages) && messages.Length > 0
            ? messages[0]
            : failure.Message;
    }

    internal static IReadOnlyList<string> ApplyServerValidationErrors(
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

    private void OpenFilePanel(Guid documentId) => expandedDocumentId = documentId;

    private void CloseFilePanel() => expandedDocumentId = null;

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
}
