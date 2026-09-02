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
}
