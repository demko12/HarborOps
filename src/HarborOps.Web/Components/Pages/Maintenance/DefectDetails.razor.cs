using System.Globalization;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class DefectDetails : IDisposable
{
    private const int EquipmentLookupPageSize = 100;

    private const string ReportedStatus = "Reported";
    private const string TriagedStatus = "Triaged";
    private const string ConvertedStatus = "Converted";
    private const string RejectedStatus = "Rejected";

    private static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly TriageModel triageModel = new();

    private DefectDetailsModel? details;
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedDefectId;
    private string? vesselName;
    private string? equipmentName;
    private string? successMessage;
    private Guid? convertedWorkOrderId;

    private bool isTriageFormOpen;
    private bool isTriaging;
    private MaintenanceApiFailure? triageFailure;

    private bool isRejectFormOpen;
    private bool isRejecting;
    private MaintenanceApiFailure? rejectFailure;
    private string? rejectReason;

    private bool isConvertFormOpen;
    private bool isConverting;
    private MaintenanceApiFailure? convertFailure;
    private DateTime? convertDueUtcLocal;

    [Parameter]
    public Guid DefectId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? BoardQuery { get; set; }

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private string PageHeading => details?.Title ?? "Defect";

    private string MaintenanceHref => string.IsNullOrWhiteSpace(BoardQuery)
        ? "/maintenance"
        : $"/maintenance?{BoardQuery}";

    private string VesselDisplayName => vesselName ?? MaintenanceFormat.UnknownVessel();

    private string EquipmentDisplayName => details?.EquipmentId is null
        ? MaintenanceFormat.MissingValue
        : equipmentName ?? "Unknown equipment";

    private string EffectiveSeverity =>
        details?.ConfirmedSeverity ?? details?.ReportedSeverity ?? MaintenanceFormat.MissingValue;

    private bool CanTriage => CanManage && details is { Status: ReportedStatus };

    private bool CanReject => CanManage && details is { Status: ReportedStatus or TriagedStatus };

    private bool CanConvert => CanManage && details is { Status: TriagedStatus };

    private bool IsSettled => details is { Status: ConvertedStatus or RejectedStatus };

    protected override Task OnParametersSetAsync()
    {
        if (loadedDefectId == DefectId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedDefectId = DefectId;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedId = DefectId;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<DefectDetailsModel> result;

        try
        {
            result = await MaintenanceApi.GetDefectAsync(requestedId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedId != DefectId)
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
        await ResolveEquipmentNameAsync(result.Value!, requestedId);
    }

    private async Task ResolveVesselNameAsync(DefectDetailsModel loaded, Guid requestedId)
    {
        var vessels = await FleetApi.LookupVesselNamesAsync([loaded.VesselId], disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestedId != DefectId)
        {
            return;
        }

        vesselName = vessels.TryGetValue(loaded.VesselId, out var name) ? name : null;
    }

    private async Task ResolveEquipmentNameAsync(DefectDetailsModel loaded, Guid requestedId)
    {
        if (loaded.EquipmentId is not { } equipmentId)
        {
            equipmentName = null;

            return;
        }

        var result = await MaintenanceApi.GetEquipmentAsync(
            new EquipmentFilter(loaded.VesselId, null, null, null, 1, EquipmentLookupPageSize),
            disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestedId != DefectId)
        {
            return;
        }

        equipmentName = result.IsSuccess
            ? result.Value!.Items.FirstOrDefault(item => item.EquipmentId == equipmentId)?.Name
            : null;
    }

    private static string ActorLabel(Guid? userId)
    {
        return userId is { } id
            ? id.ToString("N", CultureInfo.InvariantCulture)[..8]
            : MaintenanceFormat.MissingValue;
    }

    private static bool IsConcurrencyConflict(MaintenanceApiFailure failure)
    {
        return string.Equals(failure.Title, "Defect was modified concurrently.", StringComparison.Ordinal);
    }

    private static bool IsBusinessConflict(MaintenanceApiFailure? failure)
    {
        return failure is { IsConflict: true } && !IsConcurrencyConflict(failure);
    }

    private async Task RefreshAsync()
    {
        isTriageFormOpen = false;
        isRejectFormOpen = false;
        isConvertFormOpen = false;
        triageFailure = null;
        rejectFailure = null;
        convertFailure = null;

        await LoadAsync();
    }

    private void OpenTriageForm()
    {
        successMessage = null;
        triageFailure = null;
        triageModel.ConfirmedSeverity = string.Empty;
        triageModel.Priority = string.Empty;
        triageModel.Notes = null;
        isTriageFormOpen = true;
    }

    private void CloseTriageForm()
    {
        if (isTriaging)
        {
            return;
        }

        isTriageFormOpen = false;
        triageFailure = null;
    }

    private bool CanSubmitTriage =>
        !isTriaging
        && !string.IsNullOrEmpty(triageModel.ConfirmedSeverity)
        && !string.IsNullOrEmpty(triageModel.Priority);

    private async Task SubmitTriageAsync()
    {
        if (!CanSubmitTriage)
        {
            return;
        }

        isTriaging = true;
        triageFailure = null;

        try
        {
            var request = new TriageDefectRequest
            {
                ConfirmedSeverity = string.IsNullOrEmpty(triageModel.ConfirmedSeverity)
                    ? null
                    : triageModel.ConfirmedSeverity,
                Priority = string.IsNullOrEmpty(triageModel.Priority) ? null : triageModel.Priority,
                Notes = string.IsNullOrWhiteSpace(triageModel.Notes) ? null : triageModel.Notes,
            };

            var result = await MaintenanceApi.TriageDefectAsync(DefectId, request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                if (IsConcurrencyConflict(result.Failure!))
                {
                    await RefreshAsync();

                    return;
                }

                triageFailure = result.Failure;

                return;
            }

            isTriageFormOpen = false;
            details = result.Value;
            successMessage = "The defect was triaged.";
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isTriaging = false;
        }
    }

    private void OpenRejectForm()
    {
        successMessage = null;
        rejectFailure = null;
        rejectReason = null;
        isRejectFormOpen = true;
    }

    private void CloseRejectForm()
    {
        if (isRejecting)
        {
            return;
        }

        isRejectFormOpen = false;
        rejectFailure = null;
        rejectReason = null;
    }

    private async Task SubmitRejectAsync()
    {
        if (isRejecting || string.IsNullOrWhiteSpace(rejectReason))
        {
            return;
        }

        isRejecting = true;
        rejectFailure = null;

        try
        {
            var request = new RejectDefectRequest { Reason = rejectReason };

            var result = await MaintenanceApi.RejectDefectAsync(DefectId, request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                if (IsConcurrencyConflict(result.Failure!))
                {
                    await RefreshAsync();

                    return;
                }

                rejectFailure = result.Failure;

                return;
            }

            isRejectFormOpen = false;
            rejectReason = null;
            details = result.Value;
            successMessage = "The defect was rejected.";
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isRejecting = false;
        }
    }

    private void OpenConvertForm()
    {
        successMessage = null;
        convertFailure = null;
        convertDueUtcLocal = null;
        isConvertFormOpen = true;
    }

    private void CloseConvertForm()
    {
        if (isConverting)
        {
            return;
        }

        isConvertFormOpen = false;
        convertFailure = null;
    }

    private async Task SubmitConvertAsync()
    {
        if (isConverting || convertDueUtcLocal is null)
        {
            return;
        }

        isConverting = true;
        convertFailure = null;

        try
        {
            var request = new ConvertDefectToWorkOrderRequest
            {
                DueUtc = DateTime.SpecifyKind(convertDueUtcLocal.Value, DateTimeKind.Utc),
            };

            var result = await MaintenanceApi.ConvertDefectToWorkOrderAsync(
                DefectId, request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                if (IsConcurrencyConflict(result.Failure!))
                {
                    await RefreshAsync();

                    return;
                }

                convertFailure = result.Failure;

                return;
            }

            isConvertFormOpen = false;
            convertedWorkOrderId = result.Value!.WorkOrderId;
            successMessage = "The defect was converted to a work order.";

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isConverting = false;
        }
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

    private sealed class TriageModel
    {
        public string ConfirmedSeverity { get; set; } = string.Empty;

        public string Priority { get; set; } = string.Empty;

        public string? Notes { get; set; }
    }
}
