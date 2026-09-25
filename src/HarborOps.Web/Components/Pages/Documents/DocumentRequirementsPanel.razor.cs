using System.Globalization;
using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class DocumentRequirementsPanel : IDisposable
{
    internal const string ManageDocumentsPermission = "documents.manage";
    internal const string EnabledState = "enabled";
    internal const string DisabledState = "disabled";

    private const int MaxValidityDays = 365;
    private const int InstructionMaxLength = 2000;
    private const int ReasonMaxLength = 2000;
    private const int DocumentTypeOptionsPageSize = 100;

    private readonly CancellationTokenSource disposeCancellation = new();

    private int loadVersion;

    private IReadOnlyList<DocumentRequirementRowDto> rows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
    private IReadOnlyList<DocumentTypeListItem> documentTypes = [];
    private DocumentsApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private string stateFilter = string.Empty;
    private int filterVesselPickerResetToken;
    private string? initialVesselLabel;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private int? totalKpi;
    private int? enabledKpi;
    private int? disabledKpi;

    private bool isSaving;
    private DocumentsApiFailure? actionFailure;
    private string? actionSuccessMessage;

    private bool isCreateFormOpen;
    private int createVesselPickerResetToken;
    private CreateRequirementModel createModel = new();

    private DocumentRequirementRowDto? editRow;
    private EditRequirementModel editModel = new();

    private DocumentRequirementRowDto? disableRow;
    private string? disableReason;

    private DocumentRequirementRowDto? historyRow;
    private DocumentRequirementDetailsDto? historyDetails;
    private DocumentsApiFailure? historyFailure;
    private bool isLoadingHistory;

    [Parameter]
    public Guid? InitialVesselId { get; set; }

    private bool CanManage => AuthorizationState.HasPermission(ManageDocumentsPermission);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override Task OnInitializedAsync()
    {
        vesselFilter = InitialVesselId;

        return Task.WhenAll(LoadAsync(), LoadDocumentTypesAsync(), ResolveInitialVesselLabelAsync());
    }

    private async Task ResolveInitialVesselLabelAsync()
    {
        if (InitialVesselId is not { } vesselId)
        {
            return;
        }

        var names = await FleetApi.LookupVesselNamesAsync([vesselId], disposeCancellation.Token);

        initialVesselLabel = names.GetValueOrDefault(vesselId);
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselFilter = null;
        stateFilter = string.Empty;
        initialVesselLabel = null;
        filterVesselPickerResetToken++;
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
        isLoading = true;
        failure = null;

        DocumentsApiResult<DocumentRequirementsPageDto> result;

        try
        {
            result = await DocumentsApi.GetRequirementsAsync(
                new GetDocumentRequirementsQuery(vesselFilter, SelectedEnabledState(), pageNumber, pageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            isLoading = false;
            failure = result.Failure;
            rows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        var names = await FleetApi.LookupVesselNamesAsync(
            page.Items.Select(item => item.VesselId).Distinct().ToList(),
            disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;
        rows = page.Items;
        vesselNames = names;

        await LoadKpisAsync(requestVersion);
    }

    private async Task LoadKpisAsync(int requestVersion)
    {
        var total = DocumentsApi.GetRequirementsAsync(
            new GetDocumentRequirementsQuery(vesselFilter, null, 1, 1), disposeCancellation.Token);
        var enabled = DocumentsApi.GetRequirementsAsync(
            new GetDocumentRequirementsQuery(vesselFilter, true, 1, 1), disposeCancellation.Token);
        var disabled = DocumentsApi.GetRequirementsAsync(
            new GetDocumentRequirementsQuery(vesselFilter, false, 1, 1), disposeCancellation.Token);

        try
        {
            await Task.WhenAll(total, enabled, disabled);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        totalKpi = total.Result.Value?.TotalCount;
        enabledKpi = enabled.Result.Value?.TotalCount;
        disabledKpi = disabled.Result.Value?.TotalCount;
    }

    private async Task LoadDocumentTypesAsync()
    {
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

        documentTypes = result.IsSuccess ? result.Value!.Items : [];
    }

    private bool? SelectedEnabledState() => stateFilter switch
    {
        EnabledState => true,
        DisabledState => false,
        _ => null,
    };

    private void OpenCreateForm()
    {
        createModel = new CreateRequirementModel();
        createVesselPickerResetToken++;
        actionFailure = null;
        actionSuccessMessage = null;
        isCreateFormOpen = true;
    }

    private void CloseCreateForm()
    {
        if (isSaving)
        {
            return;
        }

        isCreateFormOpen = false;
        actionFailure = null;
    }

    private async Task SubmitCreateAsync()
    {
        if (isSaving)
        {
            return;
        }

        isSaving = true;
        actionFailure = null;

        DocumentsApiResult<DocumentRequirementRowDto> result;

        try
        {
            result = await DocumentsApi.CreateRequirementAsync(
                new CreateDocumentRequirementRequest(
                    createModel.VesselId,
                    createModel.DocumentTypeId,
                    createModel.MinimumRemainingValidityDays,
                    createModel.Instruction,
                    createModel.Reason),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isSaving = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            return;
        }

        isCreateFormOpen = false;
        actionSuccessMessage = "The requirement was added.";

        await LoadAsync();
    }

    private void OpenEditForm(DocumentRequirementRowDto row)
    {
        editRow = row;
        editModel = new EditRequirementModel
        {
            IsEnabled = row.IsEnabled,
            MinimumRemainingValidityDays = row.MinimumRemainingValidityDays,
            Instruction = row.Instruction,
        };
        actionFailure = null;
        actionSuccessMessage = null;
    }

    private void CloseEditForm()
    {
        if (isSaving)
        {
            return;
        }

        editRow = null;
        actionFailure = null;
    }

    private async Task SubmitEditAsync()
    {
        if (isSaving || editRow is null)
        {
            return;
        }

        isSaving = true;
        actionFailure = null;

        DocumentsApiResult<DocumentRequirementRowDto> result;

        try
        {
            result = await DocumentsApi.UpdateRequirementAsync(
                editRow.Id,
                new UpdateDocumentRequirementRequest(
                    editModel.IsEnabled,
                    editModel.MinimumRemainingValidityDays,
                    editModel.Instruction,
                    editModel.Reason,
                    editRow.Version),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isSaving = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            return;
        }

        editRow = null;
        actionSuccessMessage = "The requirement was updated.";

        await LoadAsync();
    }

    private void OpenDisableForm(DocumentRequirementRowDto row)
    {
        disableRow = row;
        disableReason = null;
        actionFailure = null;
        actionSuccessMessage = null;
    }

    private void CloseDisableForm()
    {
        if (isSaving)
        {
            return;
        }

        disableRow = null;
        actionFailure = null;
    }

    private async Task SubmitDisableAsync()
    {
        if (isSaving || disableRow is null)
        {
            return;
        }

        isSaving = true;
        actionFailure = null;

        DocumentsApiResult<DocumentRequirementRowDto> result;

        try
        {
            result = await DocumentsApi.DisableRequirementAsync(
                disableRow.Id,
                new DisableDocumentRequirementRequest(disableReason, disableRow.Version),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isSaving = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            return;
        }

        disableRow = null;
        actionSuccessMessage = "The requirement was disabled.";

        await LoadAsync();
    }

    private async Task ReloadAfterConflictAsync()
    {
        editRow = null;
        disableRow = null;
        actionFailure = null;

        await LoadAsync();
    }

    private async Task OpenHistoryAsync(DocumentRequirementRowDto row)
    {
        historyRow = row;
        historyDetails = null;
        historyFailure = null;
        isLoadingHistory = true;

        DocumentsApiResult<DocumentRequirementDetailsDto> result;

        try
        {
            result = await DocumentsApi.GetRequirementAsync(row.Id, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (!ReferenceEquals(historyRow, row))
        {
            return;
        }

        isLoadingHistory = false;
        historyDetails = result.Value;
        historyFailure = result.Failure;
    }

    private void CloseHistory()
    {
        historyRow = null;
        historyDetails = null;
        historyFailure = null;
    }

    private string VesselName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : vesselId.ToString();

    private static string FormatCount(int? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatDays(int days) =>
        days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");

    private static string FormatUtc(DateTime value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value:dd MMM yyyy HH:mm} UTC");

    private sealed class CreateRequirementModel
    {
        public Guid? VesselId { get; set; }

        public int? DocumentTypeId { get; set; }

        public int? MinimumRemainingValidityDays { get; set; } = 0;

        public string? Instruction { get; set; }

        public string? Reason { get; set; }
    }

    private sealed class EditRequirementModel
    {
        public bool IsEnabled { get; set; }

        public int? MinimumRemainingValidityDays { get; set; }

        public string? Instruction { get; set; }

        public string? Reason { get; set; }
    }
}
