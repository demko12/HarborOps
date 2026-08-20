using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PreventiveSchedulesPanel : IDisposable
{
    internal const int SuccessToastMilliseconds = 3000;

    private const int EquipmentOptionsPageSize = 100;

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterScheduleModel registerModel = new();
    private readonly RecordCompletionModel completionModel = new();
    private readonly HashSet<Guid> selectedEquipmentIds = [];

    private int loadVersion;

    private IReadOnlyList<MaintenanceScheduleRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private DateOnly? dueFromFilter;
    private DateOnly? dueToFilter;
    private string overdueFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private bool isRegisterFormOpen;
    private bool isRegistering;
    private MaintenanceApiFailure? registerFailure;
    private string? registerSuccessMessage;

    private IReadOnlyList<EquipmentListItem> equipmentOptions = [];
    private bool isEquipmentLoading;
    private MaintenanceApiFailure? equipmentFailure;

    private CancellationTokenSource? toastCancellation;

    private Guid? detailsScheduleId;
    private MaintenanceScheduleDetails? details;
    private bool isDetailsLoading;
    private MaintenanceApiFailure? detailsFailure;

    private bool isRecordingCompletion;
    private MaintenanceApiFailure? completionFailure;
    private string? completionSuccessMessage;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int OverdueCount => rows.Count(row => row.IsOverdue);

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselFilter = null;
        dueFromFilter = null;
        dueToFilter = null;
        overdueFilter = string.Empty;
        pageNumber = 1;

        return LoadAsync();
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

        var filter = new MaintenanceScheduleFilter(
            vesselFilter,
            null,
            dueFromFilter,
            dueToFilter,
            overdueFilter switch { "true" => true, "false" => false, _ => null },
            pageNumber,
            pageSize);

        MaintenanceApiResult<MaintenanceSchedulesPage> result;

        try
        {
            result = await MaintenanceApi.GetSchedulesAsync(filter, cancellationToken);
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
        IReadOnlyList<MaintenanceScheduleListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new MaintenanceScheduleRow(
                item.ScheduleId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : MaintenanceFormat.ShortVesselReference(item.VesselId),
                item.MaintenanceType,
                item.Frequency,
                item.NextDueDate,
                item.NextDueHours,
                item.IsOverdue,
                item.DaysUntilDue,
                item.EquipmentCount))
            .ToList();
    }

    private void OpenRegisterForm()
    {
        isRegisterFormOpen = true;
        registerModel.Reset();
        selectedEquipmentIds.Clear();
        equipmentOptions = [];
        equipmentFailure = null;
        registerFailure = null;
        registerSuccessMessage = null;
    }

    private void CloseRegisterForm()
    {
        isRegisterFormOpen = false;
        registerFailure = null;
    }

    private async Task SelectRegisterVesselAsync(Guid? vesselId)
    {
        registerModel.VesselId = vesselId;
        selectedEquipmentIds.Clear();
        equipmentOptions = [];
        equipmentFailure = null;

        if (vesselId is null)
        {
            return;
        }

        isEquipmentLoading = true;

        var filter = new EquipmentFilter(vesselId, null, null, null, 1, EquipmentOptionsPageSize);

        MaintenanceApiResult<EquipmentPage> result;

        try
        {
            result = await MaintenanceApi.GetEquipmentAsync(filter, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isEquipmentLoading = false;

        if (!result.IsSuccess)
        {
            equipmentFailure = result.Failure;

            return;
        }

        equipmentOptions = result.Value!.Items;
    }

    private void ToggleEquipment(Guid equipmentId, bool selected)
    {
        if (selected)
        {
            selectedEquipmentIds.Add(equipmentId);
        }
        else
        {
            selectedEquipmentIds.Remove(equipmentId);
        }
    }

    private async Task SubmitRegisterAsync()
    {
        if (isRegistering)
        {
            return;
        }

        isRegistering = true;
        registerFailure = null;
        registerSuccessMessage = null;

        var request = new RegisterMaintenanceScheduleRequest
        {
            MaintenanceType = registerModel.MaintenanceType,
            Frequency = string.IsNullOrEmpty(registerModel.Frequency) ? null : registerModel.Frequency,
            NextDueDate = registerModel.NextDueDate,
            NextDueHours = registerModel.NextDueHours,
            EquipmentIds = selectedEquipmentIds.ToList(),
        };

        MaintenanceApiResult<RegisteredMaintenanceSchedule> result;

        try
        {
            result = await MaintenanceApi.RegisterScheduleAsync(request, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isRegistering = false;

        if (!result.IsSuccess)
        {
            registerFailure = result.Failure;

            return;
        }

        isRegisterFormOpen = false;
        registerModel.Reset();
        selectedEquipmentIds.Clear();
        equipmentOptions = [];

        ShowSuccess("Preventive schedule was registered successfully.");

        await LoadAsync();
    }

    private void ShowSuccess(string message)
    {
        ShowToast(message, value => registerSuccessMessage = value);
    }

    private void ShowCompletionSuccess(string message)
    {
        ShowToast(message, value => completionSuccessMessage = value);
    }

    private void ShowToast(string message, Action<string?> apply)
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        toastCancellation = cancellation;
        apply(message);

        _ = HideToastAfterDelayAsync(cancellation, apply);
    }

    private async Task HideToastAfterDelayAsync(CancellationTokenSource cancellation, Action<string?> apply)
    {
        try
        {
            await Task.Delay(SuccessToastMilliseconds, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        apply(null);

        await InvokeAsync(StateHasChanged);
    }

    private async Task OpenDetailsAsync(Guid scheduleId)
    {
        detailsScheduleId = scheduleId;
        details = null;
        detailsFailure = null;
        completionFailure = null;
        completionSuccessMessage = null;
        completionModel.Reset();

        await LoadDetailsAsync(scheduleId);
    }

    private async Task LoadDetailsAsync(Guid scheduleId)
    {
        isDetailsLoading = true;
        detailsFailure = null;

        MaintenanceApiResult<MaintenanceScheduleDetails> result;

        try
        {
            result = await MaintenanceApi.GetScheduleDetailsAsync(scheduleId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isDetailsLoading = false;

        if (!result.IsSuccess)
        {
            detailsFailure = result.Failure;
            details = null;

            return;
        }

        details = result.Value!;
    }

    private void CloseDetails()
    {
        detailsScheduleId = null;
        details = null;
        detailsFailure = null;
        completionFailure = null;
    }

    private async Task SubmitCompletionAsync()
    {
        if (isRecordingCompletion || detailsScheduleId is not { } scheduleId)
        {
            return;
        }

        isRecordingCompletion = true;
        completionFailure = null;
        completionSuccessMessage = null;

        var request = new UpdateScheduleNextDueRequest
        {
            CompletedOn = completionModel.CompletedOn,
            NextDueHours = completionModel.NextDueHours,
        };

        MaintenanceApiResult<UpdateScheduleNextDueResult> result;

        try
        {
            result = await MaintenanceApi.UpdateScheduleNextDueAsync(
                scheduleId,
                request,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isRecordingCompletion = false;

        if (!result.IsSuccess)
        {
            completionFailure = result.Failure;

            if (result.Failure is { IsNotFound: true })
            {
                CloseDetails();
                await LoadAsync();
            }

            return;
        }

        ShowCompletionSuccess("Completion was recorded and the next due values were updated.");
        completionModel.Reset();

        await LoadDetailsAsync(scheduleId);
        await LoadAsync();
    }

    public void Dispose()
    {
        toastCancellation?.Cancel();
        toastCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class RegisterScheduleModel
    {
        public Guid? VesselId { get; set; }

        public string? MaintenanceType { get; set; }

        public string? Frequency { get; set; } = string.Empty;

        public DateOnly? NextDueDate { get; set; }

        public int? NextDueHours { get; set; }

        public void Reset()
        {
            VesselId = null;
            MaintenanceType = null;
            Frequency = string.Empty;
            NextDueDate = null;
            NextDueHours = null;
        }
    }

    private sealed class RecordCompletionModel
    {
        public DateOnly? CompletedOn { get; set; }

        public int? NextDueHours { get; set; }

        public void Reset()
        {
            CompletedOn = null;
            NextDueHours = null;
        }
    }
}
