using System.Globalization;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class MaintenanceWorkOrderDetails : IDisposable
{
    internal const int EquipmentPickerPageSize = 100;

    private const string StartAction = "Start";
    private const string CancelAction = "Cancel";
    private const string ReplaceEquipmentAction = "ReplaceEquipment";



    private readonly CancellationTokenSource disposeCancellation = new();

    private MaintenanceWorkOrderDetailsModel? details;
    private MaintenanceApiFailure? failure;
    private MaintenanceApiFailure? actionFailure;
    private bool isLoading = true;
    private bool isActing;
    private Guid loadedWorkOrderId;
    private string? vesselName;
    private string? successMessage;
    private string? pendingAction;

    private bool isEquipmentEditorOpen;
    private bool isEquipmentLoading;
    private bool isSavingEquipment;
    private MaintenanceApiFailure? equipmentLoadFailure;
    private IReadOnlyList<EquipmentListItem> vesselEquipment = [];
    private readonly HashSet<Guid> selectedEquipmentIds = [];

    internal const int InventoryPickerPageSize = 100;

    private const string ReleaseReservationsAction = "ReleaseReservations";

    private IReadOnlyList<WorkOrderPartReservationItem> reservations = [];
    private bool isReservationsLoading;
    private MaintenanceApiFailure? reservationsFailure;

    private bool isReservationEditorOpen;
    private bool isSavingReservations;
    private MaintenanceApiFailure? reservationEditorFailure;
    private readonly List<ReservationRowModel> reservationRows = [];

    private IReadOnlyList<InventoryItemListItem> inventoryOptions = [];
    private string inventorySearchText = string.Empty;
    private bool isInventoryLoading;
    private MaintenanceApiFailure? inventoryFailure;

    private bool CanChangeReservations =>
        CanManage && details is { Status: MaintenanceFormat.OpenStatus };

    private bool CanReleaseReservations =>
        CanManage
        && details is { Status: MaintenanceFormat.OpenStatus or MaintenanceFormat.CanceledStatus }
        && reservations.Count > 0;

    private bool HasReservationShortages => reservations.Any(item => item.HasShortage);

    private decimal TotalReservationShortage => reservations.Sum(item => item.ShortageQuantity);

    private int ShortageCount => reservations.Count(item => item.HasShortage);

    [Parameter]
    public Guid WorkOrderId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? BoardQuery { get; set; }

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private string PageHeading => details?.Title ?? "Work order";

    private string MaintenanceHref => string.IsNullOrWhiteSpace(BoardQuery)
    ? "/maintenance"
    : $"/maintenance?{BoardQuery}";

    private string VesselDisplayName => vesselName ?? MaintenanceFormat.UnknownVessel();

    private bool CanStart => Offers(StartAction);

    private bool CanCancel => Offers(CancelAction);

    private bool CanChangeEquipment => Offers(ReplaceEquipmentAction);

    private bool IsClosed => details is { AllowedActions.Count: 0 };

    private string StateSummary
    {
        get
        {
            if (details is not { } loaded)
            {
                return string.Empty;
            }

            return loaded.Status switch
            {
                MaintenanceFormat.CompletedStatus =>
                    "The work is finished and the record is closed.",
                MaintenanceFormat.CanceledStatus =>
                    "The work order was called off. The record is kept for history.",
                MaintenanceFormat.InProgressStatus =>
                    "An engineer is working on this order.",
                _ when loaded.IsOverdue =>
                    "This work order is past its due date and has not started.",
                _ => "The work order is open and waiting to start.",
            };
        }
    }

    private int SelectedEquipmentCount => selectedEquipmentIds.Count;

    private bool Offers(string action)
    {
        return CanManage
            && details is { } loaded
            && loaded.AllowedActions.Contains(action, StringComparer.Ordinal);
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedWorkOrderId == WorkOrderId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedWorkOrderId = WorkOrderId;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedId = WorkOrderId;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<MaintenanceWorkOrderDetailsModel> result;
        try
        {
            result = await MaintenanceApi.GetWorkOrderDetailsAsync(requestedId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedId != WorkOrderId)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            details = null;

            return;
        }

        details = result.Value;

        await LoadReservationsAsync(requestedId);

        await ResolveVesselNameAsync(result.Value!, requestedId);
    }

    private async Task ResolveVesselNameAsync(MaintenanceWorkOrderDetailsModel loaded, Guid requestedId)
    {
        var vessels = await FleetApi.LookupVesselNamesAsync([loaded.VesselId], disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestedId != WorkOrderId)
        {
            return;
        }

        vesselName = vessels.TryGetValue(loaded.VesselId, out var name) ? name : null;
    }

    private void AskToConfirm(string action)
    {
        actionFailure = null;
        successMessage = null;
        pendingAction = action;
    }

    private void DismissConfirmation()
    {
        if (isActing)
        {
            return;
        }

        pendingAction = null;
    }

    private Task ConfirmPendingActionAsync()
    {
        return pendingAction switch
        {
            StartAction => RunActionAsync(
                token => MaintenanceApi.StartWorkOrderAsync(WorkOrderId, token),
                "The work order was started."),
            CancelAction => RunActionAsync(
                token => MaintenanceApi.CancelWorkOrderAsync(WorkOrderId, token),
                "The work order was canceled."),
            ReleaseReservationsAction => ReleaseReservationsAsync(),
            _ => Task.CompletedTask,
        };
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task<MaintenanceApiResult<MaintenanceWorkOrderTransition>>> action,
        string success)
    {
        isActing = true;

        try
        {
            var result = await action(disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    pendingAction = null;

                    await LoadAsync();
                }

                return;
            }

            pendingAction = null;
            actionFailure = null;
            successMessage = success;

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isActing = false;
        }
    }

    private async Task OpenEquipmentEditorAsync()
    {
        if (details is not { } loaded)
        {
            return;
        }

        actionFailure = null;
        successMessage = null;
        isEquipmentEditorOpen = true;

        selectedEquipmentIds.Clear();
        foreach (var item in loaded.Equipment)
        {
            selectedEquipmentIds.Add(item.EquipmentId);
        }

        await LoadVesselEquipmentAsync(loaded.VesselId);
    }

    private async Task LoadVesselEquipmentAsync(Guid vesselId)
    {
        isEquipmentLoading = true;
        equipmentLoadFailure = null;

        try
        {
            var result = await MaintenanceApi.GetEquipmentAsync(
                new EquipmentFilter(vesselId, null, null, null, 1, EquipmentPickerPageSize),
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                equipmentLoadFailure = result.Failure;
                vesselEquipment = [];

                return;
            }

            vesselEquipment = result.Value!.Items;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isEquipmentLoading = false;
        }
    }

    private Task RetryEquipmentAsync()
    {
        return details is { } loaded ? LoadVesselEquipmentAsync(loaded.VesselId) : Task.CompletedTask;
    }

    private void CloseEquipmentEditor()
    {
        if (isSavingEquipment)
        {
            return;
        }

        isEquipmentEditorOpen = false;
        equipmentLoadFailure = null;
        actionFailure = null;
        selectedEquipmentIds.Clear();
    }

    private void ToggleEquipment(Guid equipmentId, bool isSelected)
    {
        if (isSelected)
        {
            selectedEquipmentIds.Add(equipmentId);
        }
        else
        {
            selectedEquipmentIds.Remove(equipmentId);
        }
    }

    private bool IsEquipmentSelected(Guid equipmentId)
    {
        return selectedEquipmentIds.Contains(equipmentId);
    }

    private async Task SaveEquipmentAsync()
    {
        if (isSavingEquipment)
        {
            return;
        }

        isSavingEquipment = true;
        actionFailure = null;

        try
        {
            var request = new ReplaceWorkOrderEquipmentWebRequest(selectedEquipmentIds.ToArray());

            var result = await MaintenanceApi.ReplaceWorkOrderEquipmentAsync(
                WorkOrderId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    isEquipmentEditorOpen = false;

                    await LoadAsync();
                }

                return;
            }

            isEquipmentEditorOpen = false;
            selectedEquipmentIds.Clear();
            successMessage = ScopeSavedMessage(result.Value!.Equipment.Count);

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSavingEquipment = false;
        }
    }

    private static string ScopeSavedMessage(int count)
    {
        return count switch
        {
            0 => "The equipment scope was cleared.",
            1 => "The work order is now scoped to 1 equipment item.",
            _ => $"The work order is now scoped to {count.ToString(CultureInfo.InvariantCulture)} equipment items.",
        };
    }

    private static string ConfirmationQuestion(string action)
    {
        return action switch
        {
            StartAction =>
                "Start this work order? It is recorded as being under work and can no longer be started again.",
            CancelAction =>
                "Cancel this work order? The record is kept for history and can no longer be started or completed.",
            ReleaseReservationsAction =>
                "Release the remaining part reservations? The stock returns to the available pool. Parts already used stay recorded.",
            _ => "Continue?",
        };
    }

    private void DismissSuccess()
    {
        successMessage = null;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private async Task LoadReservationsAsync(Guid requestedId)
    {
        isReservationsLoading = true;
        reservationsFailure = null;

        MaintenanceApiResult<WorkOrderPartReservationsResult> result;

        try
        {
            result = await MaintenanceApi.GetWorkOrderPartReservationsAsync(
                requestedId,
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedId != WorkOrderId)
        {
            return;
        }

        isReservationsLoading = false;

        if (!result.IsSuccess)
        {
            reservationsFailure = result.Failure;
            reservations = [];

            return;
        }

        reservations = result.Value?.Items ?? [];
    }

    private Task RetryReservationsAsync()
    {
        return LoadReservationsAsync(WorkOrderId);
    }

    private async Task OpenReservationEditorAsync()
    {
        actionFailure = null;
        successMessage = null;
        reservationEditorFailure = null;
        isReservationEditorOpen = true;

        reservationRows.Clear();

        foreach (var item in reservations)
        {
            reservationRows.Add(new ReservationRowModel
            {
                InventoryItemIdText = item.InventoryItemId.ToString(),
                Quantity = item.QuantityRequested,
                Name = item.Name,
                PartNumber = item.PartNumber,
            });
        }

        if (reservationRows.Count == 0)
        {
            reservationRows.Add(new ReservationRowModel());
        }

        await SearchInventoryAsync();
    }

    private void CloseReservationEditor()
    {
        if (isSavingReservations)
        {
            return;
        }

        isReservationEditorOpen = false;
        reservationEditorFailure = null;
        reservationRows.Clear();
    }

    private void AddReservationRow()
    {
        reservationRows.Add(new ReservationRowModel());
    }

    private void RemoveReservationRow(ReservationRowModel row)
    {
        reservationRows.Remove(row);
    }

    private bool IsDuplicateReservationRow(ReservationRowModel row)
    {
        return row.InventoryItemId is { } inventoryItemId
            && reservationRows.Count(candidate => candidate.InventoryItemId == inventoryItemId) > 1;
    }

    private bool CanSaveReservations =>
        !isSavingReservations
        && reservationRows.All(row => row.InventoryItemId is not null && row.Quantity > 0)
        && reservationRows.TrueForAll(row => !IsDuplicateReservationRow(row));

    private async Task SearchInventoryAsync()
    {
        isInventoryLoading = true;
        inventoryFailure = null;

        var filter = new InventoryItemFilter(
            string.IsNullOrWhiteSpace(inventorySearchText) ? null : inventorySearchText,
            null,
            1,
            InventoryPickerPageSize);

        try
        {
            var result = await MaintenanceApi.GetInventoryItemsAsync(filter, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                inventoryFailure = result.Failure;
                inventoryOptions = [];

                return;
            }

            inventoryOptions = result.Value!.Items;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isInventoryLoading = false;
        }
    }

    private bool IsMissingFromOptions(ReservationRowModel row)
    {
        return row.InventoryItemId is { } inventoryItemId
            && !inventoryOptions.Any(option => option.InventoryItemId == inventoryItemId);
    }

    private async Task SaveReservationsAsync()
    {
        if (isSavingReservations)
        {
            return;
        }

        isSavingReservations = true;
        reservationEditorFailure = null;

        try
        {
            var request = new ReplaceWorkOrderPartReservationsWebRequest(
                reservationRows
                    .Where(row => row.InventoryItemId is not null)
                    .Select(row => new WorkOrderPartReservationEntry(
                        row.InventoryItemId!.Value,
                        row.Quantity ?? 0m))
                    .ToList());

            var result = await MaintenanceApi.ReplaceWorkOrderPartReservationsAsync(
                WorkOrderId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                reservationEditorFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    isReservationEditorOpen = false;

                    await LoadAsync();
                }

                return;
            }

            isReservationEditorOpen = false;
            reservationRows.Clear();
            successMessage = ReservationsSavedMessage(result.Value!);

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSavingReservations = false;
        }
    }

    private async Task ReleaseReservationsAsync()
    {
        isActing = true;

        try
        {
            var result = await MaintenanceApi.ReleaseWorkOrderPartReservationsAsync(
                WorkOrderId,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict || result.Failure.IsNotFound)
                {
                    pendingAction = null;

                    await LoadAsync();
                }

                return;
            }

            pendingAction = null;
            actionFailure = null;
            successMessage = "The remaining part reservations were released back to stock.";

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isActing = false;
        }
    }

    private static string ReservationsSavedMessage(ReplaceWorkOrderPartReservationsResult result)
    {
        if (result.Items.Count == 0)
        {
            return "The part reservations were cleared.";
        }

        if (result.HasShortages)
        {
            var shortages = result.Items.Count(item => item.HasShortage);

            return shortages == 1
                ? "The reservations were saved. 1 part is short and needs replenishment."
                : $"The reservations were saved. {shortages.ToString(CultureInfo.InvariantCulture)} parts are short and need replenishment.";
        }

        return result.Items.Count == 1
            ? "1 part is reserved for this work order."
            : $"{result.Items.Count.ToString(CultureInfo.InvariantCulture)} parts are reserved for this work order.";
    }

    private sealed class ReservationRowModel
    {
        public Guid Key { get; } = Guid.NewGuid();

        public string InventoryItemIdText { get; set; } = string.Empty;

        public Guid? InventoryItemId =>
            Guid.TryParse(InventoryItemIdText, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        public decimal? Quantity { get; set; }

        public string? Name { get; set; }

        public string? PartNumber { get; set; }

        public string Label => string.IsNullOrWhiteSpace(PartNumber)
            ? Name ?? "Selected part"
            : $"{Name} ({PartNumber})";
    }
}
