using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PreventiveWorkPanel : IDisposable
{
    internal const int SuccessToastMilliseconds = 3000;

    private const int DefaultWindowDays = 30;
    private const string NextThirtyDaysPreset = "next-30";
    private const string NextNinetyDaysPreset = "next-90";
    private const string OverduePreset = "overdue";
    private const string CustomPreset = "custom";
    private const string DefaultPriority = "Medium";

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly GenerateModel generateModel = new();

    private int loadVersion;

    private IReadOnlyList<PreventiveWorkRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private string windowPreset = NextThirtyDaysPreset;
    private DateOnly fromFilter;
    private DateOnly toFilter;
    private Guid? vesselFilter;
    private string stateFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private int pendingCount;
    private int generatedCount;
    private int overdueCount;

    private bool isGenerateOpen;
    private bool isPreviewing;
    private bool isGenerating;
    private MaintenanceApiFailure? generateFailure;
    private PreventiveWorkPreview? preview;
    private IReadOnlyList<PreventiveWorkRow> previewRows = [];
    private GeneratePreventiveWorkWebRequest? previewedRequest;
    private PreventiveWorkGenerationResult? generationResult;

    private PreventiveWorkRow? pendingReplacement;
    private bool isReplacing;
    private MaintenanceApiFailure? replacementFailure;

    private string? successMessage;
    private Guid? successWorkOrderId;
    private CancellationTokenSource? toastCancellation;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private bool IsGenerateBusy => isPreviewing || isGenerating;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        SetWindow(windowPreset);

        return LoadAsync();
    }

    private static string WorkOrderHref(Guid workOrderId)
    {
        return $"/maintenance/work-orders/{workOrderId}";
    }

    private bool CanReplace(PreventiveWorkRow row)
    {
        return CanManage && row.OccurrenceId is not null && MaintenanceFormat.IsReplaceable(row.State);
    }

    private void SetWindow(string preset)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        (fromFilter, toFilter) = preset switch
        {
            OverduePreset => (today.AddDays(-365), today.AddDays(-1)),
            NextNinetyDaysPreset => (today, today.AddDays(90)),
            _ => (today, today.AddDays(DefaultWindowDays)),
        };
    }

    private Task ApplyWindowPresetAsync()
    {
        if (windowPreset == CustomPreset)
        {
            return Task.CompletedTask;
        }

        SetWindow(windowPreset);

        return ApplyFiltersAsync();
    }

    private Task ApplyCustomWindowAsync()
    {
        windowPreset = CustomPreset;

        return ApplyFiltersAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ResetFiltersAsync()
    {
        windowPreset = NextThirtyDaysPreset;
        SetWindow(windowPreset);
        vesselFilter = null;
        stateFilter = string.Empty;

        return ApplyFiltersAsync();
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
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

        var filter = new PreventiveWorkForecastFilter(
            fromFilter,
            toFilter,
            vesselFilter,
            string.IsNullOrEmpty(stateFilter) ? null : stateFilter,
            pageNumber,
            pageSize);

        MaintenanceApiResult<PreventiveWorkForecastPage> result;
        IReadOnlyList<PreventiveWorkRow> composed = [];

        try
        {
            result = await MaintenanceApi.GetPreventiveWorkForecastAsync(filter, cancellationToken);

            if (result.IsSuccess)
            {
                composed = await ToRowsAsync(result.Value!.Items, cancellationToken);
            }
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
            pendingCount = 0;
            generatedCount = 0;
            overdueCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;
        pendingCount = page.PendingCount;
        generatedCount = page.GeneratedCount;
        overdueCount = page.OverdueCount;
        rows = composed;
    }

    private async Task<IReadOnlyList<PreventiveWorkRow>> ToRowsAsync(
        IReadOnlyList<PreventiveWorkOccurrenceItem> items,
        CancellationToken cancellationToken)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        return items
            .Select(item => new PreventiveWorkRow(
                item.ScheduleId,
                item.OccurrenceId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name) ? name : MaintenanceFormat.UnknownVessel(),
                item.MaintenanceType,
                item.Frequency,
                item.DueDate,
                item.DueUtc,
                item.State,
                item.IsOverdue,
                item.WorkOrderId,
                item.EquipmentIds.Count))
            .ToList();
    }

    private void OpenGenerateDialog()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        generateModel.Through = toFilter >= today ? toFilter : today.AddDays(DefaultWindowDays);
        generateModel.VesselId = vesselFilter;
        generateModel.Priority = DefaultPriority;
        ResetPreview();
        isGenerateOpen = true;
    }

    private void CloseGenerateDialog()
    {
        if (IsGenerateBusy)
        {
            return;
        }

        isGenerateOpen = false;
        ResetPreview();
    }

    private void ResetPreview()
    {
        preview = null;
        previewRows = [];
        previewedRequest = null;
        generateFailure = null;
        generationResult = null;
    }

    private void SelectGenerateVessel(Guid? vesselId)
    {
        generateModel.VesselId = vesselId;
        ResetPreview();
    }

    private async Task PreviewAsync()
    {
        if (IsGenerateBusy)
        {
            return;
        }

        isPreviewing = true;
        generateFailure = null;
        generationResult = null;
        preview = null;
        previewRows = [];

        var request = generateModel.ToRequest();
        var cancellationToken = disposeCancellation.Token;

        MaintenanceApiResult<PreventiveWorkPreview> result;
        IReadOnlyList<PreventiveWorkRow> composed = [];

        try
        {
            result = await MaintenanceApi.PreviewPreventiveWorkAsync(request, cancellationToken);

            if (result.IsSuccess)
            {
                composed = await ToRowsAsync(result.Value!.Items, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isPreviewing = false;

        if (!result.IsSuccess)
        {
            generateFailure = result.Failure;
            previewedRequest = null;

            return;
        }

        preview = result.Value!;
        previewRows = composed;
        previewedRequest = request;
    }

    private async Task ConfirmGenerateAsync()
    {
        if (IsGenerateBusy || previewedRequest is not { } request || preview is not { EligibleCount: > 0 })
        {
            return;
        }

        isGenerating = true;
        generateFailure = null;

        MaintenanceApiResult<PreventiveWorkGenerationResult> result;

        try
        {
            result = await MaintenanceApi.GeneratePreventiveWorkAsync(request, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isGenerating = false;

        if (!result.IsSuccess)
        {
            generateFailure = result.Failure;

            if (result.Failure is { IsConflict: true })
            {
                preview = null;
                previewRows = [];
                previewedRequest = null;
            }

            return;
        }

        generationResult = result.Value!;
        preview = null;
        previewRows = [];
        previewedRequest = null;

        await LoadAsync();
    }

    private void AskToReplace(PreventiveWorkRow row)
    {
        replacementFailure = null;
        pendingReplacement = row;
    }

    private void DismissReplacement()
    {
        if (isReplacing)
        {
            return;
        }

        pendingReplacement = null;
        replacementFailure = null;
    }

    private async Task ConfirmReplacementAsync()
    {
        if (isReplacing || pendingReplacement is not { OccurrenceId: { } occurrenceId })
        {
            return;
        }

        isReplacing = true;
        replacementFailure = null;

        MaintenanceApiResult<PreventiveWorkReplacement> result;

        try
        {
            result = await MaintenanceApi.ReplacePreventiveWorkOrderAsync(
                occurrenceId,
                new GeneratePreventiveWorkWebRequest(),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isReplacing = false;

        if (!result.IsSuccess)
        {
            replacementFailure = result.Failure;

            if (result.Failure is { IsConflict: true } or { IsNotFound: true })
            {
                await LoadAsync();
            }

            return;
        }

        pendingReplacement = null;
        ShowSuccess("A replacement work order was created.", result.Value!.WorkOrderId);

        await LoadAsync();
    }

    private void ShowSuccess(string message, Guid? workOrderId)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        successMessage = message;
        successWorkOrderId = workOrderId;

        _ = HideToastAfterDelayAsync(cancellation);
    }

    private async Task HideToastAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SuccessToastMilliseconds, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        successMessage = null;
        successWorkOrderId = null;

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class GenerateModel
    {
        public DateOnly? Through { get; set; }

        public Guid? VesselId { get; set; }

        public string Priority { get; set; } = DefaultPriority;

        public GeneratePreventiveWorkWebRequest ToRequest()
        {
            return new GeneratePreventiveWorkWebRequest
            {
                Through = Through,
                VesselId = VesselId,
                Priority = Priority,
            };
        }
    }
}
