using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Voyages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class PortCallClearancePanel : IDisposable
{
    internal const string PendingStatus = "Pending";
    internal const string CompletedStatus = "Completed";
    internal const string ReopenedStatus = "Reopened";
    internal const string ClearedItemStatus = "Cleared";
    internal const string WaivedItemStatus = "Waived";

    internal const string AddAction = "add";
    internal const string EditAction = "edit";
    internal const string ClearAction = "clear";
    internal const string WaiveAction = "waive";
    internal const string CompleteAction = "complete";
    internal const string ReopenAction = "reopen";

    internal const string MissingValue = "—";

    internal static readonly IReadOnlyList<string> Categories =
    [
        "PortAuthority",
        "Customs",
        "Immigration",
        "Health",
        "Security",
        "Other",
    ];

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<Guid, DocumentDetailsDto> documentCache = [];

    private PortCallClearance? clearance;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedStopId;

    private string? activeAction;
    private Guid? activeItemId;
    private bool isSubmitting;
    private VoyagesApiFailure? actionFailure;
    private string? actionValidationMessage;
    private string? actionSuccess;

    private string formCategory = "Customs";
    private string formTitle = string.Empty;
    private bool formIsRequired = true;
    private DateOnly? formDueDate;
    private TimeOnly formDueTime = TimeOnly.MinValue;
    private string formAuthorityReference = string.Empty;
    private Guid? formEvidenceDocumentId;
    private string formReason = string.Empty;

    [Parameter]
    public Guid VoyageId { get; set; }

    [Parameter]
    public Guid VesselId { get; set; }

    [Parameter]
    public Guid StopId { get; set; }

    [Parameter]
    public string StopStatus { get; set; } = PendingStatus;

    [Parameter]
    public DateTime? StopDepartedAtUtc { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    internal bool CanManageVoyages =>
        AuthorizationState.HasPermission(CurrentUserAuthorizationState.VoyagesManagePermission);

    internal bool HasDeparted =>
        StopDepartedAtUtc is not null
        || string.Equals(StopStatus, "Departed", StringComparison.Ordinal);

    internal bool CanAct => CanManageVoyages && !HasDeparted;

    internal string? ActionErrorMessage
    {
        get
        {
            if (actionValidationMessage is not null)
            {
                return actionValidationMessage;
            }

            if (actionFailure is not { } current)
            {
                return null;
            }

            return current.Errors.Count > 0
                ? string.Join(" ", current.Errors.SelectMany(entry => entry.Value))
                : current.Message;
        }
    }

    internal bool ActionErrorIsConflict =>
        actionValidationMessage is null && actionFailure?.IsConflict == true;

    internal static string CategoryLabel(string category)
    {
        return category switch
        {
            "PortAuthority" => "Port authority",
            _ => category,
        };
    }

    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string ItemStatusModifier(string status)
    {
        return status switch
        {
            ClearedItemStatus => "cleared",
            WaivedItemStatus => "waived",
            _ => "pending",
        };
    }

    internal static string ClearanceStatusModifier(string status)
    {
        return status switch
        {
            CompletedStatus => "completed",
            ReopenedStatus => "reopened",
            _ => "pending",
        };
    }

    internal static string EventLabel(string eventType)
    {
        return eventType switch
        {
            "ItemAdded" => "Item added",
            "ItemUpdated" => "Item updated",
            "ItemCleared" => "Item cleared",
            "ItemWaived" => "Item waived",
            "Completed" => "Clearance completed",
            "Reopened" => "Clearance reopened",
            _ => eventType,
        };
    }

    internal static bool IsBlocking(PortCallClearanceItemModel item)
    {
        return item.IsRequired
            && string.Equals(item.Status, PendingStatus, StringComparison.Ordinal);
    }

    internal static string GroupSummary(IEnumerable<PortCallClearanceItemModel> items)
    {
        var grouped = items.ToList();
        var resolved = grouped.Count(item =>
            !string.Equals(item.Status, PendingStatus, StringComparison.Ordinal));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{resolved}/{grouped.Count} resolved");
    }

    internal string DueState(PortCallClearanceItemModel item)
    {
        if (item.DueAtUtc is not { } dueAtUtc || clearance is not { } view)
        {
            return MissingValue;
        }

        if (item.IsOverdue)
        {
            return "Overdue";
        }

        var days = (dueAtUtc.Date - view.EvaluatedAtUtc.Date).Days;

        return days switch
        {
            < 0 => "Past due",
            0 => "Due today",
            1 => "Due in 1 day",
            _ => string.Create(CultureInfo.InvariantCulture, $"Due in {days} days"),
        };
    }

    internal string ResolveDocumentLabel(Guid? documentId)
    {
        if (documentId is not { } id)
        {
            return MissingValue;
        }

        return documentCache.TryGetValue(id, out var document)
            ? $"{document.DocumentTypeName} — {document.Number}"
            : id.ToString();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedStopId == StopId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedStopId = StopId;

        return LoadAsync();
    }

    internal IReadOnlyList<IGrouping<string, PortCallClearanceItemModel>> GroupedItems()
    {
        if (clearance is null)
        {
            return [];
        }

        return clearance.Items
            .GroupBy(item => item.Category, StringComparer.Ordinal)
            .OrderBy(group => IndexOfCategory(group.Key))
            .ToList();
    }

    private static int IndexOfCategory(string category)
    {
        var index = Categories.ToList().IndexOf(category);

        return index < 0 ? Categories.Count : index;
    }

    private async Task LoadAsync()
    {
        var requestedStopId = StopId;
        isLoading = true;
        failure = null;

        VoyagesApiResult<PortCallClearance> result;
        try
        {
            result = await VoyagesApi.GetPortCallClearanceAsync(
                VoyageId,
                requestedStopId,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedStopId != StopId)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            clearance = null;

            return;
        }

        clearance = result.Value;

        await LoadDocumentLabelsAsync();
    }

    private async Task LoadDocumentLabelsAsync()
    {
        if (clearance is null)
        {
            return;
        }

        var documentIds = clearance.Items
            .Where(item => item.EvidenceDocumentId is not null)
            .Select(item => item.EvidenceDocumentId!.Value)
            .Distinct()
            .Where(documentId => !documentCache.ContainsKey(documentId))
            .ToList();

        foreach (var documentId in documentIds)
        {
            DocumentsApiResult<DocumentDetailsDto> result;
            try
            {
                result = await DocumentsApi.GetDocumentByIdAsync(
                    documentId,
                    disposeCancellation.Token);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (result.Value is { } document)
            {
                documentCache[documentId] = document;
            }
        }
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private void BeginAdd()
    {
        ResetForm();
        activeAction = AddAction;
        activeItemId = null;
        formCategory = "Customs";
        formIsRequired = true;
    }

    private void BeginEdit(PortCallClearanceItemModel item)
    {
        ResetForm();
        activeAction = EditAction;
        activeItemId = item.ItemId;
        formCategory = item.Category;
        formTitle = item.Title;
        formIsRequired = item.IsRequired;
        formDueDate = item.DueAtUtc is { } dueAt ? DateOnly.FromDateTime(dueAt) : null;
        formDueTime = item.DueAtUtc is { } due ? TimeOnly.FromTimeSpan(due.TimeOfDay) : TimeOnly.MinValue;
        formAuthorityReference = item.AuthorityReference ?? string.Empty;
        formEvidenceDocumentId = item.EvidenceDocumentId;
    }

    private void BeginDecision(PortCallClearanceItemModel item, string action)
    {
        ResetForm();
        activeAction = action;
        activeItemId = item.ItemId;
        formAuthorityReference = item.AuthorityReference ?? string.Empty;
        formEvidenceDocumentId = item.EvidenceDocumentId;
    }

    private void BeginClearanceAction(string action)
    {
        ResetForm();
        activeAction = action;
        activeItemId = null;
    }

    private void CancelAction(KeyboardEventArgs? args = null)
    {
        if (args is not null && !string.Equals(args.Key, "Escape", StringComparison.Ordinal))
        {
            return;
        }

        ResetForm();
    }

    private void ResetForm()
    {
        activeAction = null;
        activeItemId = null;
        actionFailure = null;
        actionValidationMessage = null;
        formTitle = string.Empty;
        formIsRequired = true;
        formDueDate = null;
        formDueTime = TimeOnly.MinValue;
        formAuthorityReference = string.Empty;
        formEvidenceDocumentId = null;
        formReason = string.Empty;
    }

    private bool TryBuildItemPayload(
        out string category,
        out string title,
        out DateTime? dueAtUtc,
        out string? authorityReference,
        out Guid? evidenceDocumentId)
    {
        category = formCategory;
        title = formTitle.Trim();
        dueAtUtc = null;
        authorityReference = null;
        evidenceDocumentId = null;

        if (title.Length == 0)
        {
            actionValidationMessage = "Title is required.";

            return false;
        }

        if (formDueDate is { } dueDate)
        {
            dueAtUtc = DateTime.SpecifyKind(dueDate.ToDateTime(formDueTime), DateTimeKind.Utc);
        }

        authorityReference = string.IsNullOrWhiteSpace(formAuthorityReference)
            ? null
            : formAuthorityReference.Trim();

        evidenceDocumentId = formEvidenceDocumentId;

        return true;
    }

    private async Task SubmitAddAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (!TryBuildItemPayload(
                out var category,
                out var title,
                out var dueAtUtc,
                out var authorityReference,
                out var evidenceDocumentId))
        {
            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.AddClearanceItemAsync(
                VoyageId,
                StopId,
                new AddClearanceItemWebRequest(
                    category,
                    title,
                    formIsRequired,
                    dueAtUtc,
                    authorityReference,
                    evidenceDocumentId),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Clearance item added.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitEditAsync()
    {
        if (isSubmitting || activeItemId is not { } itemId)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (!TryBuildItemPayload(
                out var category,
                out var title,
                out var dueAtUtc,
                out var authorityReference,
                out var evidenceDocumentId))
        {
            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.UpdateClearanceItemAsync(
                VoyageId,
                StopId,
                itemId,
                new UpdateClearanceItemWebRequest(
                    category,
                    title,
                    formIsRequired,
                    dueAtUtc,
                    authorityReference,
                    evidenceDocumentId),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Clearance item updated.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitClearAsync()
    {
        if (isSubmitting || activeItemId is not { } itemId)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        var evidenceDocumentId = formEvidenceDocumentId;

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.ClearClearanceItemAsync(
                VoyageId,
                StopId,
                itemId,
                new ClearClearanceItemWebRequest(
                    string.IsNullOrWhiteSpace(formAuthorityReference)
                        ? null
                        : formAuthorityReference.Trim(),
                    evidenceDocumentId),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Clearance item cleared.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitWaiveAsync()
    {
        if (isSubmitting || activeItemId is not { } itemId)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (string.IsNullOrWhiteSpace(formReason))
        {
            actionValidationMessage = "Waiver reason is required.";

            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.WaiveClearanceItemAsync(
                VoyageId,
                StopId,
                itemId,
                new WaiveClearanceItemWebRequest(formReason.Trim()),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Clearance item waived.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitCompleteAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;
        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.CompleteClearanceAsync(
                VoyageId,
                StopId,
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Port-call clearance completed.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task SubmitReopenAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        actionFailure = null;
        actionValidationMessage = null;

        if (string.IsNullOrWhiteSpace(formReason))
        {
            actionValidationMessage = "Reopen reason is required.";

            return;
        }

        isSubmitting = true;

        try
        {
            var result = await VoyagesApi.ReopenClearanceAsync(
                VoyageId,
                StopId,
                new ReopenClearanceWebRequest(formReason.Trim()),
                disposeCancellation.Token);

            await AfterActionAsync(result.Failure, "Port-call clearance reopened.");
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task AfterActionAsync(VoyagesApiFailure? actionResultFailure, string successMessage)
    {
        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (actionResultFailure is not null)
        {
            actionFailure = actionResultFailure;

            if (actionResultFailure.IsConflict || actionResultFailure.IsNotFound)
            {
                await LoadAsync();
            }

            return;
        }

        actionSuccess = successMessage;
        ResetForm();

        await LoadAsync();

        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }
}
